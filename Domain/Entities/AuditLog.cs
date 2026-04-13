namespace Domain.Entities
{
    public class AuditLog
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid? ProjectId { get; set; }
        public string EntityType { get; set; } = string.Empty;
        public Guid EntityId { get; set; }
        public string Action { get; set; } = string.Empty;
        public Guid ActorId { get; set; }

        /// <summary>
        /// Detalles adicionales serializados como JSONB. NO incluir PII, PATs ni contenido completo de specs.
        /// </summary>
        public Dictionary<string, object>? Details { get; set; }

        /// <summary>
        /// CorrelationId requerido para trazabilidad end-to-end (ADR-003).
        /// </summary>
        public Guid CorrelationId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
