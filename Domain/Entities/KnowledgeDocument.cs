namespace Domain.Entities
{
    public class KnowledgeDocument
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public long SizeBytes { get; set; }
        public string Category { get; set; } = "general";
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
        public int ChunksGenerated { get; set; }
        public string UploadedBy { get; set; } = string.Empty;
    }
}
