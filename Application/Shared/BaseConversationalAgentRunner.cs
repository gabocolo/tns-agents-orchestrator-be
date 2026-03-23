using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace Application.Shared
{
    /// <summary>
    /// Clase base para agentes que necesitan conversación multi-turno.
    /// Hereda de BaseAgentRunner y agrega manejo automático de historial.
    ///
    /// Para crear un agente conversacional:
    /// 1. Hereda de esta clase
    /// 2. Implementa BuildSystemPrompt()
    /// 3. Implementa CleanResponse() si necesitas procesar el output
    /// Eso es todo — el historial y la persistencia son automáticos.
    /// </summary>
    public abstract class BaseConversationalAgentRunner : BaseAgentRunner, IStreamingAgentRunner
    {
        protected readonly ConversationService ConversationService;
        protected readonly KernelConfig KernelConfig;
        protected readonly ILoggerFactory LoggerFactory;

        protected BaseConversationalAgentRunner(
            KernelConfig kernelConfig,
            ILoggerFactory loggerFactory,
            IDecisionLogger decisionLogger,
            ConversationService conversationService,
            ILogger logger)
            : base(decisionLogger, logger)
        {
            KernelConfig = kernelConfig;
            LoggerFactory = loggerFactory;
            ConversationService = conversationService;
        }

        protected override async Task<AgentResult> ExecuteAsync(
            AgentRequest request,
            CancellationToken ct)
        {
            // 1. Extrae sessionId — igual para todos los agentes conversacionales
            Guid sessionId;
            try
            {
                sessionId = ConversationService.ExtractSessionId(request);
            }
            catch (ArgumentException ex)
            {
                return AgentResult.Fail(request.RequestId, ex.Message, ex.Message);
            }

            // 2. Construye el system prompt específico de este agente
            var systemPrompt = BuildSystemPrompt(request, sessionId);

            // 3. Prepara el historial — guarda mensaje usuario + construye ChatHistory
            var chatHistory = await ConversationService.PrepareAsync(
                request, sessionId, systemPrompt, ct
            );

            // 3b. Pre-retrieval RAG — inyectar contexto antes de llamar al LLM
            var ragContext = await FetchRagContextAsync(request.UserMessage, ct);
            if (!string.IsNullOrEmpty(ragContext))
                chatHistory.AddSystemMessage(ragContext);

            // 4. Llama al LLM
            var kernel = KernelFactory.Create(KernelConfig, LoggerFactory);
            ConfigureKernel(kernel);
            var chatService = kernel.GetRequiredService<IChatCompletionService>();

            var settings = BuildExecutionSettings();

            var response = await chatService.GetChatMessageContentAsync(
                chatHistory, settings, kernel, ct
            );

            var rawResponse = response.Content ?? string.Empty;

            // 5. Guarda respuesta + dispara output handler si existe
            await ConversationService.CommitAsync(request, sessionId, rawResponse, ct);

            // 6. Limpia y procesa el response antes de devolverlo al FE
            var cleanResponse = CleanResponse(rawResponse);
            var artifacts = BuildArtifacts(rawResponse, sessionId);

            return AgentResult.Ok(request.RequestId, cleanResponse, artifacts);
        }

        /// <summary>
        /// Cada agente define su propio system prompt.
        /// </summary>
        protected abstract string BuildSystemPrompt(AgentRequest request, Guid sessionId);

        /// <summary>
        /// Limpia el response antes de enviarlo al FE.
        /// Sobrescribe si necesitas quitar marcadores o transformar el output.
        /// </summary>
        protected virtual string CleanResponse(string rawResponse) => rawResponse;

        /// <summary>
        /// Construye artifacts del resultado si los hay.
        /// Sobrescribe si el agente produce artifacts específicos.
        /// </summary>
        protected virtual List<AgentArtifact> BuildArtifacts(string rawResponse, Guid sessionId)
            => new();

        /// <summary>
        /// Temperatura del LLM para este agente.
        /// Sobrescribe en el agente concreto si necesitas ajustar.
        /// </summary>
        protected virtual double Temperature => 0.3;

        /// <summary>
        /// Max tokens para este agente.
        /// </summary>
        protected virtual int MaxTokens => 3000;

        /// <summary>
        /// Pre-retrieval RAG hook. Se llama ANTES de cada invocación al LLM.
        /// El texto retornado se inyecta como sistema message en el chat history,
        /// garantizando que el LLM vea los lineamientos sin depender de function calling.
        /// Retorna null si el agente no usa RAG.
        /// </summary>
        protected virtual Task<string?> FetchRagContextAsync(string userMessage, CancellationToken ct)
            => Task.FromResult<string?>(null);

        /// <summary>
        /// Hook llamado después de crear el Kernel.
        /// Los agentes concretos pueden importar plugins o configurar el kernel aquí.
        /// </summary>
        protected virtual void ConfigureKernel(Kernel kernel) { }

        /// <summary>
        /// Construye los settings de ejecución del LLM.
        /// Los agentes concretos pueden sobrescribir para añadir ToolCallBehavior, etc.
        /// </summary>
        protected virtual OpenAIPromptExecutionSettings BuildExecutionSettings()
            => new() { Temperature = Temperature, MaxTokens = MaxTokens };

        /// <summary>
        /// Versión streaming del Execute.
        /// Devuelve tokens uno por uno mientras el LLM responde.
        /// El historial se guarda igual que en la versión normal.
        /// </summary>
        public async IAsyncEnumerable<string> RunStreamingAsync(
            AgentRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            Guid sessionId = Guid.Empty;
            string? errorMessage = null;
            
            try
            {
                sessionId = ConversationService.ExtractSessionId(request);
            }
            catch (ArgumentException ex)
            {
                errorMessage = $"ERROR: {ex.Message}";
            }

            if (errorMessage != null)
            {
                yield return errorMessage;
                yield break;
            }

            var systemPrompt = BuildSystemPrompt(request, sessionId);

            var chatHistory = await ConversationService.PrepareAsync(
                request, sessionId, systemPrompt, ct
            );

            // Pre-retrieval RAG — inyectar contexto antes de llamar al LLM
            var ragContext = await FetchRagContextAsync(request.UserMessage, ct);
            if (!string.IsNullOrEmpty(ragContext))
                chatHistory.AddSystemMessage(ragContext);

            var kernel = KernelFactory.Create(KernelConfig, LoggerFactory);
            ConfigureKernel(kernel);
            var chatService = kernel.GetRequiredService<IChatCompletionService>();

            var settings = BuildExecutionSettings();

            // Acumula el response completo para guardarlo en BD al final
            var fullResponse = new System.Text.StringBuilder();

            // StreamingAsync devuelve tokens a medida que el LLM los genera
            await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                chatHistory, settings, kernel, ct))
            {
                var token = chunk.Content ?? string.Empty;
                if (string.IsNullOrEmpty(token)) continue;

                fullResponse.Append(token);

                // Limpia el token antes de enviarlo al FE
                // (quita marcadores internos si aparecen en medio del stream)
                var cleanToken = token
                    .Replace("ESTIMATION_START", string.Empty)
                    .Replace("ESTIMATION_END", string.Empty);

                yield return cleanToken;
            }

            // Guarda el response completo en BD después de terminar el stream
            var rawFull = fullResponse.ToString();
            await ConversationService.CommitAsync(request, sessionId, rawFull, ct);
        }
    }
}
