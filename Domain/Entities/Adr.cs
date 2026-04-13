namespace Domain.Entities
{
    public class Adr
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProjectId { get; set; }
        public int Number { get; set; }
        public string Title { get; set; } = string.Empty;
        public AdrStatus Status { get; set; } = AdrStatus.PROPOSED;

        /// <summary>
        /// Contenido en markdown. Clasificación: Confidencial — sanitizar antes de enviar a LLM.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        public Guid CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
