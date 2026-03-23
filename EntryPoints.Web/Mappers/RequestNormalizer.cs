using Domain.Entities;
using EntryPoints.Web.Models;

namespace EntryPoints.Web.Mappers
{
    /// <summary>
    /// Traduce el modelo HTTP al modelo interno de Domain.
    /// Un solo lugar para esta conversión.
    /// </summary>
    public static class RequestNormalizer
    {
        public static AgentRequest ToAgentRequest(AgentHttpRequest httpRequest, string userName)
        {
            var agentType = ParseAgentType(httpRequest.Agent);

            return new AgentRequest
            {
                UserMessage = httpRequest.Message,
                TargetAgent = agentType,
                Channel = RequestChannel.WebPortal,
                UserName = userName,
                Metadata = httpRequest.Metadata
            };
        }

        public static AgentHttpResponse ToHttpResponse(AgentResult result)
        {
            return new AgentHttpResponse
            {
                RequestId = result.RequestId,
                Success = result.Success,
                Summary = result.Summary,
                ErrorDetail = result.ErrorDetail,
                CompletedAt = result.CompletedAt,
                Artifacts = [.. result.Artifacts.Select(a => new ArtifactDto
                {
                    Type = a.Type,
                    Label = a.Label,
                    Value = a.Value
                })]
            };
        }

        private static AgentType ParseAgentType(string agent)
        {
            return agent.ToLowerInvariant() switch
            {
                "unittestagent" => AgentType.UnitTestAgent,
                "projectmanageragent" => AgentType.ProjectManagerAgent,
                "architectureagent" => AgentType.ArchitectureAgent,
                _ => throw new ArgumentException($"Agente desconocido: '{agent}'")
            };
        }
    }
}
