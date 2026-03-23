namespace Infrastructure.Settings
{
    public class KnowledgeBaseConfig
    {
        public string QdrantHost { get; set; } = "localhost";
        public int QdrantPort { get; set; } = 6333;    // REST/HTTP (dashboard)
        public int QdrantGrpcPort { get; set; } = 6334; // gRPC (Qdrant.Client)
        public string CollectionName { get; set; } = "architecture-guidelines";
        public int ChunkSize { get; set; } = 500;
        public int ChunkOverlap { get; set; } = 50;
        public string EmbeddingDeployment { get; set; } = "text-embedding-ada-002";
        public List<string> AllowedExtensions { get; set; } = new() { ".md", ".txt" };
        public long MaxFileSizeBytes { get; set; } = 10_485_760; // 10 MB
    }
}
