using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Agents.ArchitectureAgent
{
    /// <summary>
    /// Persiste iteraciones generadas por el ArchitectureAgent.
    /// Se dispara automáticamente desde ConversationService cuando
    /// el agente emite una respuesta con marcadores ##ITERATION_START## / ##ITERATION_END##.
    /// </summary>
    public class ArchitectureIterationOutputHandler : IAgentOutputHandler
    {
        private readonly IProposalRepository _proposalRepo;
        private readonly ILogger<ArchitectureIterationOutputHandler> _logger;

        public AgentType AgentType => AgentType.ArchitectureAgent;

        public ArchitectureIterationOutputHandler(
            IProposalRepository proposalRepo,
            ILogger<ArchitectureIterationOutputHandler> logger)
        {
            _proposalRepo = proposalRepo;
            _logger = logger;
        }

        public async Task HandleAsync(
            AgentRequest request,
            Guid sessionId,
            string agentResponse,
            CancellationToken ct = default)
        {
            if (!agentResponse.Contains("##ITERATION_START##") ||
                !agentResponse.Contains("##ITERATION_END##"))
                return;

            if (!request.Metadata.TryGetValue("proposalId", out var proposalIdStr)
                || !Guid.TryParse(proposalIdStr, out var proposalId))
            {
                _logger.LogWarning(
                    "[ArchitectureIterationHandler] Metadata no contiene 'proposalId' válido. SessionId={SessionId}",
                    sessionId
                );
                return;
            }

            var proposal = await _proposalRepo.GetByIdAsync(proposalId, ct);
            if (proposal == null)
            {
                _logger.LogWarning(
                    "[ArchitectureIterationHandler] Propuesta no encontrada. ProposalId={ProposalId}",
                    proposalId
                );
                return;
            }

            var content = ExtractContent(agentResponse);
            var nextVersion = proposal.Iterations.Count == 0
                ? 1
                : proposal.Iterations.Max(i => i.Version) + 1;

            var iteration = new ProposalIteration
            {
                Version = nextVersion,
                Content = content,
                Components = ExtractComponents(content),
                RiskLevel = ExtractRiskLevel(content),
                CreatedAt = DateTime.UtcNow
            };

            await _proposalRepo.AddIterationAsync(proposalId, iteration, ct);

            _logger.LogInformation(
                "[ArchitectureIterationHandler] Iteración v{Version} guardada. ProposalId={ProposalId}",
                nextVersion, proposalId
            );
        }

        private static string ExtractContent(string response)
        {
            var start = response.IndexOf("##ITERATION_START##") + "##ITERATION_START##".Length;
            var end = response.IndexOf("##ITERATION_END##");
            return start >= 0 && end > start
                ? response[start..end].Trim()
                : response;
        }

        private static List<string> ExtractComponents(string content)
        {
            // Extrae líneas que parecen nombres de componentes en diagramas mermaid o listas
            return content
                .Split('\n')
                .Where(l => l.TrimStart().StartsWith("- ") || l.TrimStart().StartsWith("* "))
                .Select(l => l.TrimStart('-', '*', ' ').Trim())
                .Where(l => l.Length > 2 && l.Length < 80)
                .Take(20)
                .ToList();
        }

        private static RiskLevel ExtractRiskLevel(string content)
        {
            var lower = content.ToLowerInvariant();
            if (lower.Contains("riesgo: critical") || lower.Contains("risk: critical")) return RiskLevel.Critical;
            if (lower.Contains("riesgo: high") || lower.Contains("risk: high")) return RiskLevel.High;
            if (lower.Contains("riesgo: low") || lower.Contains("risk: low")) return RiskLevel.Low;
            return RiskLevel.Medium;
        }
    }
}
