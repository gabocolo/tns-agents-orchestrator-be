using Application.Shared;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Application.Agents.ArchitectureAgent
{
    /// <summary>
    /// Agente especializado en diseño de arquitecturas de software.
    /// Es conversacional + SSE, igual que ProjectManagerAgentRunner.
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
        protected override int MaxTokens => 6000;

        public ArchitectureAgentRunner(
            KernelConfig kernelConfig,
            ILoggerFactory loggerFactory,
            IDecisionLogger decisionLogger,
            ConversationService conversationService)
            : base(
                kernelConfig,
                loggerFactory,
                decisionLogger,
                conversationService,
                loggerFactory.CreateLogger<ArchitectureAgentRunner>())
        { }

        protected override string BuildSystemPrompt(AgentRequest request, Guid sessionId)
        {
            var proposalId = request.Metadata.GetValueOrDefault("proposalId", "sin-propuesta");

            return LoadPromptTemplate()
                .Replace("{{userName}}", request.UserName)
                .Replace("{{sessionId}}", sessionId.ToString())
                .Replace("{{proposalId}}", proposalId);
        }

        protected override string CleanResponse(string rawResponse)
        {
            return rawResponse
                .Replace("##ITERATION_START##", string.Empty)
                .Replace("##ITERATION_END##", string.Empty)
                .Trim();
        }

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
