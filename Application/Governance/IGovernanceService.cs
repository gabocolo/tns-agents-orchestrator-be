using Application.Governance.Requests;
using Domain.Entities;

namespace Application.Governance
{
    // ── Response DTOs ─────────────────────────────────────────────────────────

    public record CreateProjectResult
    {
        public Guid ProjectId { get; init; }
        public string Name { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }

    public record CreateAdrResult
    {
        public Guid AdrId { get; init; }
        public Guid ProjectId { get; init; }
        public int Number { get; init; }
        public string Title { get; init; } = string.Empty;
        public AdrStatus Status { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    public record CreateAdrWithAiResult
    {
        public Guid AdrId { get; init; }
        public Guid ProjectId { get; init; }
        public string Title { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public int TokensUsed { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    public record ConfigureGatesResult
    {
        public Guid ProjectId { get; init; }
        public int Count { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    // ── Interface ─────────────────────────────────────────────────────────────

    public interface IGovernanceService
    {
        Task<CreateProjectResult> CreateProjectAsync(
            CreateProjectRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        Task<CreateAdrResult> CreateAdrAsync(
            CreateAdrRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        Task<CreateAdrWithAiResult> CreateAdrWithAiAsync(
            CreateAdrWithAiRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        Task<ConfigureGatesResult> ConfigureQualityGatesAsync(
            ConfigureQualityGatesRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);
    }
}
