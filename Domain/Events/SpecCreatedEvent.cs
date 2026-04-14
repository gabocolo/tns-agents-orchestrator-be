using Domain.Entities;

namespace Domain.Events
{
    public record SpecCreatedEvent
    {
        public Guid SpecId { get; init; }
        public Guid ProjectId { get; init; }
        public SpecLevel Level { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public Guid CreatedBy { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
