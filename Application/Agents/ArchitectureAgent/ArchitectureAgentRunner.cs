using Application.Shared;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Agents.ArchitectureAgent
{
    /// <summary>
    /// Agente especializado en diseño de arquitecturas de software.
    /// Es conversacional + SSE, igual que ProjectManagerAgentRunner.
    ///
    /// Antes de cada respuesta el LLM puede invocar automáticamente
    /// SearchKnowledgeBaseAsync para consultar lineamientos en Qdrant.
    ///
    /// Cuando el agente genera una iteración completa (marcada con
    /// ##ITERATION_START## / ##ITERATION_END##), el ArchitectureIterationOutputHandler
    /// la extrae y persiste automáticamente en la propuesta correspondiente.
    ///
    /// Metadata requerida en el request:
    ///   - sessionId: Guid de la conversación
    ///   - proposalId: Guid de la propuesta a la que pertenece la iteración
    /// </summary>
    public class ArchitectureAgentRunner : BaseConversationalAgentRunner
    {
        public override AgentType AgentType => AgentType.ArchitectureAgent;

        protected override double Temperature => 0.5;
        protected override int MaxTokens => 10000;

        private readonly IKnowledgeIngestionService _knowledgeService;

        public ArchitectureAgentRunner(
            KernelConfig kernelConfig,
            ILoggerFactory loggerFactory,
            IDecisionLogger decisionLogger,
            ConversationService conversationService,
            IKnowledgeIngestionService knowledgeService)
            : base(
                kernelConfig,
                loggerFactory,
                decisionLogger,
                conversationService,
                loggerFactory.CreateLogger<ArchitectureAgentRunner>())
        {
            _knowledgeService = knowledgeService;
        }

        // ─── Pre-retrieval RAG: siempre se ejecuta antes del LLM ─────────────

        protected override async Task<string?> FetchRagContextAsync(
            string userMessage, CancellationToken ct)
        {
            var result = await _knowledgeService.SearchAsync(userMessage, topK: 5);

            if (!result.Items.Any())
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("=== LINEAMIENTOS ORGANIZACIONALES OBLIGATORIOS ===");
            sb.AppendLine("Los siguientes lineamientos aprobados por la organización son");
            sb.AppendLine("OBLIGATORIOS para esta respuesta. NO puedes recomendar tecnologías,");
            sb.AppendLine("patrones ni prácticas que los contradigan.");
            sb.AppendLine();

            foreach (var item in result.Items)
            {
                sb.AppendLine($"[FUENTE: {item.FileName} | RELEVANCIA: {item.Score:P0} | CATEGORÍA: {item.Category}]");
                sb.AppendLine(item.Content);
                sb.AppendLine("---");
            }

            sb.AppendLine("=== FIN LINEAMIENTOS ===");
            sb.AppendLine("Al final de tu respuesta incluye el bloque ##REFERENCES_START## con las fuentes usadas.");

            return sb.ToString();
        }

        // ─── Kernel: registrar este objeto como plugin (búsquedas adicionales) ──

        protected override void ConfigureKernel(Kernel kernel)
        {
            kernel.ImportPluginFromObject(this, "KnowledgeBasePlugin");
        }

        // ─── Habilitar auto-invocación de funciones ───────────────────────────

        protected override OpenAIPromptExecutionSettings BuildExecutionSettings()
            => new()
            {
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

        // ─── KernelFunction: consulta RAG ─────────────────────────────────────

        [KernelFunction("search_knowledge_base")]
        [Description("Busca lineamientos y estándares de arquitectura " +
                     "aprobados por la organización. Úsala SIEMPRE antes " +
                     "de recomendar tecnologías, patrones de integración, " +
                     "nomenclatura de base de datos o estándares de seguridad.")]
        public async Task<string> SearchKnowledgeBaseAsync(
            [Description("Query de búsqueda en español sobre el lineamiento necesario")]
            string query,
            Kernel kernel)
        {
            var result = await _knowledgeService.SearchAsync(query, topK: 4);

            if (!result.Items.Any())
                return "No se encontraron lineamientos específicos para esta consulta.";

            var sb = new StringBuilder();
            sb.AppendLine("=== LINEAMIENTOS ORGANIZACIONALES ENCONTRADOS ===");

            foreach (var item in result.Items)
            {
                sb.AppendLine($"[FUENTE: {item.FileName} | RELEVANCIA: {item.Score:P0}]");
                sb.AppendLine(item.Content);
                sb.AppendLine("---");
            }

            sb.AppendLine("=== FIN LINEAMIENTOS ===");
            sb.AppendLine("IMPORTANTE: Basa tu respuesta en estos lineamientos. " +
                          "Al final de tu respuesta incluye el bloque ##REFERENCES_START## " +
                          "con las fuentes que usaste.");

            return sb.ToString();
        }

        // ─── System prompt ────────────────────────────────────────────────────

        protected override string BuildSystemPrompt(AgentRequest request, Guid sessionId)
        {
            var proposalId = request.Metadata.GetValueOrDefault("proposalId", "sin-propuesta");

            return LoadPromptTemplate()
                .Replace("{{userName}}", request.UserName)
                .Replace("{{sessionId}}", sessionId.ToString())
                .Replace("{{proposalId}}", proposalId);
        }

        // ─── Limpieza del response ────────────────────────────────────────────

        protected override string CleanResponse(string rawResponse)
        {
            // Quitar marcadores de iteración
            var clean = rawResponse
                .Replace("##ITERATION_START##", string.Empty)
                .Replace("##ITERATION_END##", string.Empty);

            // Quitar bloque de referencias (ya fue emitido como evento SSE separado)
            clean = Regex.Replace(
                clean,
                @"##REFERENCES_START##[\s\S]*?##REFERENCES_END##",
                string.Empty,
                RegexOptions.None);

            return clean.Trim();
        }

        // ─── Artifacts ────────────────────────────────────────────────────────

        protected override List<AgentArtifact> BuildArtifacts(string rawResponse, Guid sessionId)
        {
            if (!rawResponse.Contains("##ITERATION_START##"))
                return new();

            return new List<AgentArtifact>
            {
                new()
                {
                    Type  = "ArchitectureIteration",
                    Label = "Iteración de arquitectura generada",
                    Value = sessionId.ToString()
                }
            };
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static string LoadPromptTemplate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Application.Agents.ArchitectureAgent.Prompts.SystemPrompt.txt";

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"No se encontró el recurso embebido: {resourceName}");

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
