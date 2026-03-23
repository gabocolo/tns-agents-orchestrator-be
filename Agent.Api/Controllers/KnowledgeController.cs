using Agent.Api.Models;
using Domain.Interfaces;
using Infrastructure.Settings;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers
{
    [ApiController]
    [Route("api/knowledge")]
    [Produces("application/json")]
    public class KnowledgeController : ControllerBase
    {
        private readonly IKnowledgeIngestionService _service;
        private readonly KnowledgeBaseConfig _config;
        private readonly ILogger<KnowledgeController> _logger;

        private const int MaxFilesPerRequest = 20;

        public KnowledgeController(
            IKnowledgeIngestionService service,
            KnowledgeBaseConfig config,
            ILogger<KnowledgeController> logger)
        {
            _service = service;
            _config = config;
            _logger = logger;
        }

        // ─── POST /api/knowledge/ingest ───────────────────────────────────────

        /// <summary>
        /// Ingesta archivos de texto (.md, .txt) hacia Qdrant.
        /// Acepta multipart/form-data con múltiples archivos.
        /// </summary>
        [HttpPost("ingest")]
        [RequestSizeLimit(200_000_000)] // 200 MB límite total del request
        [ProducesResponseType(typeof(ApiResponse<IngestionResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Ingest(
            IFormFileCollection files,
            [FromForm] string category = "general",
            CancellationToken ct = default)
        {
            if (files == null || files.Count == 0)
                return BadRequest(ApiResponse<object>.Fail("Debes enviar al menos un archivo."));

            if (files.Count > MaxFilesPerRequest)
                return BadRequest(ApiResponse<object>.Fail(
                    $"Máximo {MaxFilesPerRequest} archivos por request. Recibidos: {files.Count}."));

            var inputs = new List<KnowledgeFileInput>();
            var validationErrors = new List<string>();

            foreach (var file in files)
            {
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

                if (!_config.AllowedExtensions.Contains(ext))
                {
                    validationErrors.Add($"{file.FileName}: extensión '{ext}' no permitida. Permitidas: {string.Join(", ", _config.AllowedExtensions)}");
                    continue;
                }

                if (file.Length > _config.MaxFileSizeBytes)
                {
                    validationErrors.Add($"{file.FileName}: excede el tamaño máximo de {_config.MaxFileSizeBytes / 1_048_576} MB.");
                    continue;
                }

                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);

                inputs.Add(new KnowledgeFileInput(
                    FileName: file.FileName,
                    ContentType: file.ContentType,
                    Content: ms.ToArray()
                ));
            }

            if (inputs.Count == 0)
            {
                return BadRequest(ApiResponse<object>.Fail(
                    $"Ningún archivo pasó la validación. Errores: {string.Join("; ", validationErrors)}"));
            }

            _logger.LogInformation(
                "[KnowledgeController] Ingestando {Count} archivos. Category={Category}",
                inputs.Count, category);

            var result = await _service.IngestFilesAsync(inputs, category, ct);

            // Adjunta los errores de validación a los errores del resultado
            if (validationErrors.Count > 0)
            {
                result = result with { Errors = result.Errors.Concat(validationErrors).ToList() };
            }

            return Ok(ApiResponse<IngestionResult>.Ok(result));
        }

        // ─── GET /api/knowledge/search ────────────────────────────────────────

        /// <summary>
        /// Busca en el índice vectorial. Útil para verificar que la ingesta funcionó.
        /// </summary>
        [HttpGet("search")]
        [ProducesResponseType(typeof(ApiResponse<KnowledgeSearchResult>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Search(
            [FromQuery] string q,
            [FromQuery] int topK = 5,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(q))
                return BadRequest(ApiResponse<object>.Fail("El parámetro 'q' es requerido."));

            if (topK < 1 || topK > 50)
                return BadRequest(ApiResponse<object>.Fail("'topK' debe estar entre 1 y 50."));

            var result = await _service.SearchAsync(q, topK, ct);
            return Ok(ApiResponse<KnowledgeSearchResult>.Ok(result));
        }

        // ─── GET /api/knowledge/status ────────────────────────────────────────

        /// <summary>
        /// Estado de la colección en Qdrant: si existe y cuántos vectores tiene.
        /// </summary>
        [HttpGet("status")]
        [ProducesResponseType(typeof(ApiResponse<CollectionStatus>), StatusCodes.Status200OK)]
        public async Task<IActionResult> Status(CancellationToken ct)
        {
            var status = await _service.GetStatusAsync(ct);
            return Ok(ApiResponse<CollectionStatus>.Ok(status));
        }

        // ─── DELETE /api/knowledge/collection ────────────────────────────────

        /// <summary>
        /// Elimina la colección completa. Usar para reindexar desde cero.
        /// </summary>
        [HttpDelete("collection")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeleteCollection(CancellationToken ct)
        {
            await _service.DeleteCollectionAsync(ct);
            _logger.LogWarning("[KnowledgeController] Colección eliminada por solicitud administrativa.");
            return NoContent();
        }
    }
}
