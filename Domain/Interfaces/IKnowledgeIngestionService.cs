namespace Domain.Interfaces
{
    public interface IKnowledgeIngestionService
    {
        Task<IngestionResult> IngestFilesAsync(
            List<KnowledgeFileInput> files,
            string category,
            CancellationToken ct = default);

        Task<KnowledgeSearchResult> SearchAsync(
            string query,
            int topK = 5,
            CancellationToken ct = default);

        Task<CollectionStatus> GetStatusAsync(CancellationToken ct = default);

        Task DeleteCollectionAsync(CancellationToken ct = default);
    }

    public record KnowledgeFileInput(
        string FileName,
        string ContentType,
        byte[] Content
    );

    public record IngestionResult(
        bool Success,
        int FilesProcessed,
        int ChunksIndexed,
        List<string> FileNames,
        List<string> Errors,
        DateTime ProcessedAt
    );

    public record KnowledgeSearchResult(
        List<KnowledgeSearchItem> Items
    );

    public record KnowledgeSearchItem(
        string FileName,
        string Content,
        float Score,
        string Category
    );

    public record CollectionStatus(
        bool Exists,
        long VectorCount,
        string CollectionName
    );
}
