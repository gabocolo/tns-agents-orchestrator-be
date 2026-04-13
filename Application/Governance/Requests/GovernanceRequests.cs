using Domain.Entities;

namespace Application.Governance.Requests
{
    public class CreateProjectRequest
    {
        public string Name { get; init; } = string.Empty;
        public string? Description { get; init; }
        public string? GitRepoUrl { get; init; }
    }

    public class CreateAdrRequest
    {
        public Guid ProjectId { get; init; }
        public int Number { get; init; }
        public string Title { get; init; } = string.Empty;
        public AdrStatus Status { get; init; } = AdrStatus.PROPOSED;
        public string Content { get; init; } = string.Empty;
    }

    public class CreateAdrWithAiRequest
    {
        public Guid ProjectId { get; init; }
        public string Description { get; init; } = string.Empty;
        public string? Context { get; init; }
    }

    public class ConfigureQualityGatesRequest
    {
        public Guid ProjectId { get; init; }
        public List<QualityGateConfig> Gates { get; init; } = new();
    }

    /// <summary>
    /// Configuración de un quality gate recibida desde la API.
    /// </summary>
    public record QualityGateConfig
    {
        public int GateNumber { get; init; }
        public string Name { get; init; } = string.Empty;
        public List<string> Validations { get; init; } = new();
        public bool Blocking { get; init; } = true;
    }
}
