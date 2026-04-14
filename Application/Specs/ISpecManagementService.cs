using Application.Specs.Requests;
using Domain.Entities;

namespace Application.Specs
{
    // ── Response DTOs ─────────────────────────────────────────────────────────

    public record GenerationJobResponse
    {
        public Guid JobId { get; init; }
        public string Status { get; init; } = "QUEUED";
        public int EstimatedTime { get; init; }
    }

    public record SpecDraftResponse
    {
        public Guid SpecId { get; init; }
        public Guid ProjectId { get; init; }
        public SpecLevel Level { get; init; }
        public Guid? ParentSpecId { get; init; }
        public string Version { get; init; } = string.Empty;
        public SpecStatus Status { get; init; }
        public string Title { get; init; } = string.Empty;
        public Dictionary<string, object> Content { get; init; } = new();
        public Dictionary<string, object>? DataClassification { get; init; }
        public string? Model { get; init; }
        public int? TokensUsed { get; init; }
        public Guid CreatedBy { get; init; }
        public DateTime CreatedAt { get; init; }
    }

    public record RegenerateSectionResponse
    {
        public Guid SpecId { get; init; }
        public string Section { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public int TokensUsed { get; init; }
        public DateTime UpdatedAt { get; init; }
    }

    public record JobStatusResponse
    {
        public Guid JobId { get; init; }
        public string Status { get; init; } = string.Empty;
        public Guid? ResultSpecId { get; init; }
        public string? Error { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
    }

    // ── Interface ─────────────────────────────────────────────────────────────

    public interface ISpecManagementService
    {
        /// <summary>
        /// Genera una spec L1 de forma asincrona. Retorna jobId para tracking (ADR-P002).
        /// </summary>
        Task<GenerationJobResponse> GenerateSpecL1Async(
            GenerateSpecL1Request request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        /// <summary>
        /// Genera una spec L2 desde una L1 aprobada. Asincrono via Service Bus.
        /// </summary>
        Task<GenerationJobResponse> GenerateSpecL2Async(
            GenerateSpecL2Request request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        /// <summary>
        /// Genera una spec L3 desde una L2 aprobada. Asincrono via Service Bus.
        /// </summary>
        Task<GenerationJobResponse> GenerateSpecL3Async(
            GenerateSpecL3Request request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        /// <summary>
        /// Guarda un borrador de spec manualmente (CRUD sin IA). Idempotente por specId (ADR-002).
        /// </summary>
        Task<SpecDraftResponse> SaveSpecDraftAsync(
            SaveSpecDraftRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        /// <summary>
        /// Regenera una seccion individual de una spec existente con IA (ADR-P001: Sonnet).
        /// </summary>
        Task<RegenerateSectionResponse> RegenerateSectionAsync(
            RegenerateSectionRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default);

        /// <summary>
        /// Obtiene el estado actual de un job de generacion asincrona.
        /// Readable por cualquier usuario autenticado (no requiere rol especifico).
        /// </summary>
        Task<JobStatusResponse> GetJobStatusAsync(Guid jobId, CancellationToken ct = default);
    }
}
