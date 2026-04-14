namespace Domain.Entities
{
    public class SpecGenerationJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProjectId { get; set; }
        public SpecLevel Level { get; set; }
        public Guid? ParentSpecId { get; set; }

        /// <summary>
        /// Input del usuario (sanitizado por DLP). NO almacenar input original.
        /// </summary>
        public string SanitizedInput { get; set; } = string.Empty;

        public string? AdditionalContext { get; set; }
        public SpecJobStatus Status { get; set; } = SpecJobStatus.QUEUED;
        public Guid? ResultSpecId { get; set; }
        public string? Error { get; set; }
        public int RetryCount { get; set; }
        public Guid CreatedBy { get; set; }
        public Guid CorrelationId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? CompletedAt { get; set; }
    }
}
