using Application.Shared;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace Application.Agents.ProjectManagerAgent
{
    /// <summary>
    /// Agente especializado en estimación de proyectos.
    /// Toda la lógica de conversación multi-turno está en BaseConversationalAgentRunner.
    /// Este runner solo define: su prompt, cómo limpia el response y sus artifacts.
    /// </summary>
    public class ProjectManagerAgentRunner : BaseConversationalAgentRunner
    {
        public override AgentType AgentType => AgentType.ProjectManagerAgent;

        // Más temperatura que UnitTestAgent — necesita creatividad para estimar
        protected override double Temperature => 0.4;
        protected override int MaxTokens => 4000;

        public ProjectManagerAgentRunner(
            KernelConfig kernelConfig,
            ILoggerFactory loggerFactory,
            IDecisionLogger decisionLogger,
            ConversationService conversationService)
            : base(
                kernelConfig,
                loggerFactory,
                decisionLogger,
                conversationService,
                loggerFactory.CreateLogger<ProjectManagerAgentRunner>())
        { }

        protected override string BuildSystemPrompt(AgentRequest request, Guid sessionId)
        {
            var template = LoadPromptTemplate();
            return template
                .Replace("{{userName}}", request.UserName)
                .Replace("{{sessionId}}", sessionId.ToString());
        }

        protected override string CleanResponse(string rawResponse)
        {
            return rawResponse
                .Replace("```ESTIMATION_START```", string.Empty)
                .Replace("```ESTIMATION_END```", string.Empty)
                .Replace("ESTIMATION_START", string.Empty)
                .Replace("ESTIMATION_END", string.Empty)
                .Trim();
        }

        protected override List<AgentArtifact> BuildArtifacts(string rawResponse, Guid sessionId)
        {
            if (!rawResponse.Contains("ESTIMATION_START"))
                return new();

            return new List<AgentArtifact>
        {
            new()
            {
                Type  = "Estimation",
                Label = "Estimación completa generada",
                Value = sessionId.ToString()
            }
        };
        }

        private static string LoadPromptTemplate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Application.Agents.ProjectManagerAgent.Prompts.SystemPrompt.txt";

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"No se encontró: {resourceName}");

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
