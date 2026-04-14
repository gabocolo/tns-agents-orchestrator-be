using Domain.Entities;

namespace Application.Specs.Requests
{
    public class GenerateSpecL1Request
    {
        public Guid ProjectId { get; init; }
        public string Description { get; init; } = string.Empty;
        public string? AdditionalContext { get; init; }
    }

    public class GenerateSpecL2Request
    {
        public Guid ProjectId { get; init; }
        public Guid ParentSpecId { get; init; }
        public string UseCaseId { get; init; } = string.Empty;
        public string? TechnicalContext { get; init; }
    }

    public class GenerateSpecL3Request
    {
        public Guid ProjectId { get; init; }
        public Guid ParentSpecId { get; init; }
        public string ChangeDescription { get; init; } = string.Empty;
    }

    public class SaveSpecDraftRequest
    {
        public Guid? SpecId { get; init; }
        public Guid ProjectId { get; init; }
        public SpecLevel Level { get; init; }
        public Guid? ParentSpecId { get; init; }
        public string Title { get; init; } = string.Empty;
        public Dictionary<string, object> Content { get; init; } = new();
        public Dictionary<string, object>? DataClassification { get; init; }
    }

    public class RegenerateSectionRequest
    {
        public Guid SpecId { get; init; }
        public string Section { get; init; } = string.Empty;
        public string? AdditionalInstructions { get; init; }
    }
}
