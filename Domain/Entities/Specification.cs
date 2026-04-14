namespace Domain.Entities
{
    public class Specification
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProjectId { get; set; }
        public Guid? ParentSpecId { get; set; }
        public SpecLevel Level { get; set; }
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Version semver (X.Y.Z). Se asigna 1.0.0 al crear.
        /// </summary>
        public string Version { get; set; } = "1.0.0";

        public SpecStatus Status { get; set; } = SpecStatus.DRAFT;

        /// <summary>
        /// Contenido estructurado por nivel (JSONB). Clasificación: Confidencial — propiedad intelectual.
        /// </summary>
        public Dictionary<string, object> Content { get; set; } = new();

        /// <summary>
        /// Clasificación de datos por entidad. Requerido para L1 (ADR-006).
        /// </summary>
        public Dictionary<string, object>? DataClassification { get; set; }

        public Guid? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Modelo LLM usado para generación (si aplica). ADR-P001: Opus para L1, Sonnet para L2/L3.
        /// </summary>
        public string? Model { get; set; }

        /// <summary>
        /// Tokens consumidos durante generación IA (si aplica).
        /// </summary>
        public int? TokensUsed { get; set; }

        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
