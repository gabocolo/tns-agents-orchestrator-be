using Domain.Interfaces;
using Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Embeddings;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace Infrastructure.Services
{
    /// <summary>
    /// Ingesta documentos de texto hacia Qdrant.
    /// Divide cada archivo en chunks con solapamiento, genera embeddings
    /// via OpenAI y hace upsert en batch al índice vectorial.
    /// </summary>
    public class KnowledgeIngestionService : IKnowledgeIngestionService
    {
        private readonly QdrantClient _qdrant;
        private readonly ITextEmbeddingGenerationService _embedding;
        private readonly KnowledgeBaseConfig _config;
        private readonly ILogger<KnowledgeIngestionService> _logger;

        public KnowledgeIngestionService(
            QdrantClient qdrant,
            ITextEmbeddingGenerationService embedding,
            KnowledgeBaseConfig config,
            ILogger<KnowledgeIngestionService> logger)
        {
            _qdrant = qdrant;
            _embedding = embedding;
            _config = config;
            _logger = logger;
        }

        // ─── IngestFilesAsync ─────────────────────────────────────────────────

        public async Task<IngestionResult> IngestFilesAsync(
            List<KnowledgeFileInput> files,
            string category,
            CancellationToken ct = default)
        {
            await EnsureCollectionAsync(ct);

            var errors = new List<string>();
            var fileNames = new List<string>();
            int totalChunks = 0;

            foreach (var file in files)
            {
                try
                {
                    var text = System.Text.Encoding.UTF8.GetString(file.Content);
                    var chunks = SplitIntoChunks(text, _config.ChunkSize, _config.ChunkOverlap);

                    _logger.LogInformation(
                        "[KnowledgeIngestion] Procesando {File} → {Chunks} chunks",
                        file.FileName, chunks.Count);

                    var points = new List<PointStruct>();

                    for (int i = 0; i < chunks.Count; i++)
                    {
                        var embeddings = await _embedding.GenerateEmbeddingsAsync(
                            new List<string> { chunks[i] }, cancellationToken: ct);

                        var vector = embeddings[0].ToArray();

                        var point = new PointStruct
                        {
                            Id = Guid.NewGuid(),
                            Vectors = vector,
                            Payload =
                            {
                                ["fileName"]    = file.FileName,
                                ["content"]     = chunks[i],
                                ["chunkIndex"]  = i,
                                ["totalChunks"] = chunks.Count,
                                ["category"]    = category,
                                ["ingestedAt"]  = DateTime.UtcNow.ToString("O")
                            }
                        };

                        points.Add(point);

                        // Batch upsert cada 100 puntos
                        if (points.Count >= 100)
                        {
                            await _qdrant.UpsertAsync(_config.CollectionName, points, cancellationToken: ct);
                            totalChunks += points.Count;
                            points.Clear();
                        }
                    }

                    // Flush del batch restante
                    if (points.Count > 0)
                    {
                        await _qdrant.UpsertAsync(_config.CollectionName, points, cancellationToken: ct);
                        totalChunks += points.Count;
                    }

                    fileNames.Add(file.FileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[KnowledgeIngestion] Error procesando {File}", file.FileName);
                    errors.Add($"{file.FileName}: {ex.Message}");
                }
            }

            var success = errors.Count < files.Count; // al menos uno procesado
            _logger.LogInformation(
                "[KnowledgeIngestion] Completado. Archivos={Files} Chunks={Chunks} Errores={Errors}",
                fileNames.Count, totalChunks, errors.Count);

            return new IngestionResult(
                Success: success,
                FilesProcessed: fileNames.Count,
                ChunksIndexed: totalChunks,
                FileNames: fileNames,
                Errors: errors,
                ProcessedAt: DateTime.UtcNow
            );
        }

        // ─── SearchAsync ──────────────────────────────────────────────────────

        public async Task<KnowledgeSearchResult> SearchAsync(
            string query,
            int topK = 5,
            CancellationToken ct = default)
        {
            await EnsureCollectionAsync(ct);

            var embeddings = await _embedding.GenerateEmbeddingsAsync(
                new List<string> { query }, cancellationToken: ct);

            var vector = embeddings[0].ToArray();

            var results = await _qdrant.SearchAsync(
                collectionName: _config.CollectionName,
                vector: vector,
                limit: (ulong)topK,
                cancellationToken: ct);

            var items = results.Select(r => new KnowledgeSearchItem(
                FileName: r.Payload.TryGetValue("fileName", out var fn) ? fn.StringValue : string.Empty,
                Content:  r.Payload.TryGetValue("content",  out var ct2) ? ct2.StringValue : string.Empty,
                Score:    r.Score,
                Category: r.Payload.TryGetValue("category", out var cat) ? cat.StringValue : "general"
            )).ToList();

            return new KnowledgeSearchResult(items);
        }

        // ─── GetStatusAsync ───────────────────────────────────────────────────

        public async Task<Domain.Interfaces.CollectionStatus> GetStatusAsync(CancellationToken ct = default)
        {
            var exists = await _qdrant.CollectionExistsAsync(_config.CollectionName, ct);
            if (!exists)
                return new Domain.Interfaces.CollectionStatus(false, 0, _config.CollectionName);

            var info = await _qdrant.GetCollectionInfoAsync(_config.CollectionName, ct);

            return new Domain.Interfaces.CollectionStatus(
                Exists: true,
                VectorCount: (long)info.VectorsCount,
                CollectionName: _config.CollectionName
            );
        }

        // ─── DeleteCollectionAsync ────────────────────────────────────────────

        public async Task DeleteCollectionAsync(CancellationToken ct = default)
        {
            var exists = await _qdrant.CollectionExistsAsync(_config.CollectionName, ct);
            if (!exists) return;

            await _qdrant.DeleteCollectionAsync(_config.CollectionName, cancellationToken: ct);
            _logger.LogWarning(
                "[KnowledgeIngestion] Colección '{Name}' eliminada.", _config.CollectionName);
        }

        // ─── Helpers privados ─────────────────────────────────────────────────

        private async Task EnsureCollectionAsync(CancellationToken ct)
        {
            var exists = await _qdrant.CollectionExistsAsync(_config.CollectionName, ct);
            if (exists) return;

            await _qdrant.CreateCollectionAsync(
                collectionName: _config.CollectionName,
                vectorsConfig: new VectorParams
                {
                    Size = 1536,          // dimensión de text-embedding-ada-002
                    Distance = Distance.Cosine
                },
                cancellationToken: ct);

            _logger.LogInformation(
                "[KnowledgeIngestion] Colección '{Name}' creada.", _config.CollectionName);
        }

        private static List<string> SplitIntoChunks(string text, int size, int overlap)
        {
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var chunks = new List<string>();
            var step = size - overlap;

            for (int i = 0; i < words.Length; i += step)
            {
                var chunk = string.Join(" ", words.Skip(i).Take(size));
                if (!string.IsNullOrWhiteSpace(chunk))
                    chunks.Add(chunk);
            }

            return chunks;
        }
    }
}
