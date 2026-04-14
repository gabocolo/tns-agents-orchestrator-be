using Application.Specs;
using Application.Specs.Exceptions;
using Application.Specs.Requests;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers
{
    [ApiController]
    [Route("specs")]
    public class SpecController : ControllerBase
    {
        private readonly ISpecManagementService _service;
        private readonly ILogger<SpecController> _logger;

        public SpecController(ISpecManagementService service, ILogger<SpecController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // ── POST /specs/generate/l1 ──────────────────────────────────────────

        [HttpPost("generate/l1")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> GenerateSpecL1(
            [FromBody] GenerateSpecL1HttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new GenerateSpecL1Request
                {
                    ProjectId = req.ProjectId,
                    Description = req.Description,
                    AdditionalContext = req.AdditionalContext
                };

                var result = await _service.GenerateSpecL1Async(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[SpecController] Job L1 encolado. JobId={JobId} ProjectId={ProjectId}",
                    result.JobId, req.ProjectId);

                return Accepted(result);
            }
            catch (SpecException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── POST /specs/generate/l2 ──────────────────────────────────────────

        [HttpPost("generate/l2")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> GenerateSpecL2(
            [FromBody] GenerateSpecL2HttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new GenerateSpecL2Request
                {
                    ProjectId = req.ProjectId,
                    ParentSpecId = req.ParentSpecId,
                    UseCaseId = req.UseCaseId,
                    TechnicalContext = req.TechnicalContext
                };

                var result = await _service.GenerateSpecL2Async(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[SpecController] Job L2 encolado. JobId={JobId} ParentSpecId={ParentSpecId}",
                    result.JobId, req.ParentSpecId);

                return Accepted(result);
            }
            catch (SpecException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── POST /specs/generate/l3 ──────────────────────────────────────────

        [HttpPost("generate/l3")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> GenerateSpecL3(
            [FromBody] GenerateSpecL3HttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new GenerateSpecL3Request
                {
                    ProjectId = req.ProjectId,
                    ParentSpecId = req.ParentSpecId,
                    ChangeDescription = req.ChangeDescription
                };

                var result = await _service.GenerateSpecL3Async(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[SpecController] Job L3 encolado. JobId={JobId} ParentSpecId={ParentSpecId}",
                    result.JobId, req.ParentSpecId);

                return Accepted(result);
            }
            catch (SpecException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── POST /specs/draft ────────────────────────────────────────────────

        [HttpPost("draft")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> SaveSpecDraft(
            [FromBody] SaveSpecDraftHttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new SaveSpecDraftRequest
                {
                    SpecId = req.SpecId,
                    ProjectId = req.ProjectId,
                    Level = req.Level,
                    ParentSpecId = req.ParentSpecId,
                    Title = req.Title,
                    Content = req.Content,
                    DataClassification = req.DataClassification
                };

                var result = await _service.SaveSpecDraftAsync(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[SpecController] Spec draft guardado. SpecId={SpecId} Level={Level}",
                    result.SpecId, result.Level);

                return StatusCode(StatusCodes.Status201Created, result);
            }
            catch (SpecException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── GET /specs/jobs/{jobId} ──────────────────────────────────────────

        [HttpGet("jobs/{jobId:guid}")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetJobStatus(Guid jobId, CancellationToken ct)
        {
            try
            {
                var result = await _service.GetJobStatusAsync(jobId, ct);

                _logger.LogInformation(
                    "[SpecController] Estado de job consultado. JobId={JobId} Status={Status}",
                    jobId, result.Status);

                return Ok(result);
            }
            catch (SpecException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── POST /specs/{specId}/regenerate-section ──────────────────────────

        [HttpPost("{specId:guid}/regenerate-section")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> RegenerateSection(
            Guid specId,
            [FromBody] RegenerateSectionHttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new RegenerateSectionRequest
                {
                    SpecId = specId,
                    Section = req.Section,
                    AdditionalInstructions = req.AdditionalInstructions
                };

                var result = await _service.RegenerateSectionAsync(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[SpecController] Sección regenerada. SpecId={SpecId} Section={Section} Model={Model}",
                    specId, result.Section, result.Model);

                return Ok(result);
            }
            catch (SpecException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private RequestContext ExtractContext()
        {
            var userIdHeader = Request.Headers["X-User-Id"].FirstOrDefault();
            var roleHeader = Request.Headers["X-User-Role"].FirstOrDefault();
            var correlationHeader = Request.Headers["X-Correlation-Id"].FirstOrDefault();

            if (string.IsNullOrWhiteSpace(userIdHeader) || !Guid.TryParse(userIdHeader, out var actorId))
                return new RequestContext { Error = "Header 'X-User-Id' requerido y debe ser un GUID válido." };

            if (string.IsNullOrWhiteSpace(roleHeader) || !Enum.TryParse<UserRole>(roleHeader, true, out var actorRole))
                return new RequestContext { Error = $"Header 'X-User-Role' requerido. Valores válidos: {string.Join(", ", Enum.GetNames<UserRole>())}." };

            var correlationId = Guid.TryParse(correlationHeader, out var cid) ? cid : Guid.NewGuid();

            return new RequestContext { ActorId = actorId, ActorRole = actorRole, CorrelationId = correlationId };
        }

        private sealed class RequestContext
        {
            public Guid ActorId { get; init; }
            public UserRole ActorRole { get; init; }
            public Guid CorrelationId { get; init; }
            public string? Error { get; init; }
        }
    }

    // ── HTTP Request records ─────────────────────────────────────────────────

    public record GenerateSpecL1HttpRequest(
        Guid ProjectId,
        string Description,
        string? AdditionalContext);

    public record GenerateSpecL2HttpRequest(
        Guid ProjectId,
        Guid ParentSpecId,
        string UseCaseId,
        string? TechnicalContext);

    public record GenerateSpecL3HttpRequest(
        Guid ProjectId,
        Guid ParentSpecId,
        string ChangeDescription);

    public record SaveSpecDraftHttpRequest(
        Guid? SpecId,
        Guid ProjectId,
        SpecLevel Level,
        Guid? ParentSpecId,
        string Title,
        Dictionary<string, object> Content,
        Dictionary<string, object>? DataClassification);

    public record RegenerateSectionHttpRequest(
        string Section,
        string? AdditionalInstructions);
}
