namespace Domain.Entities
{
    public class ProposalComment
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProposalId { get; set; }
        public string AuthorId { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public ProposalRole AuthorRole { get; set; }
        public string Body { get; set; } = string.Empty;

        /// <summary>
        /// Versión de la iteración sobre la que aplica el comentario.
        /// </summary>
        public int IterationVersion { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ResolvedAt { get; set; }
    }
}
