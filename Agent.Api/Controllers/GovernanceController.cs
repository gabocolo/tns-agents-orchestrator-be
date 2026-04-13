using Application.Governance;
using Application.Governance.Exceptions;
using Application.Governance.Requests;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers
{
    [ApiController]
    [Route("governance")]
    public class GovernanceController : ControllerBase
    {
        private readonly IGovernanceService _service;
        private readonly ILogger<GovernanceController> _logger;

        public GovernanceController(IGovernanceService service, ILogger<GovernanceController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // ── POST /governance/projects ─────────────────────────────────────────

        [HttpPost("projects")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreateProject(
            [FromBody] CreateProjectHttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new CreateProjectRequest
                {
                    Name = req.Name,
                    Description = req.Description,
                    GitRepoUrl = req.GitRepoUrl
                };

                var result = await _service.CreateProjectAsync(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[GovernanceController] Proyecto creado. ProjectId={ProjectId}",
                    result.ProjectId);

                return StatusCode(StatusCodes.Status201Created, result);
            }
            catch (GovernanceException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── POST /governance/projects/{projectId}/adrs ────────────────────────

        [HttpPost("projects/{projectId:guid}/adrs")]
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> CreateAdr(
            Guid projectId,
            [FromBody] CreateAdrHttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new CreateAdrRequest
                {
                    ProjectId = projectId,
                    Number = req.Number,
                    Title = req.Title,
                    Status = req.Status,
                    Content = req.Content
                };

                var result = await _service.CreateAdrAsync(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[GovernanceController] ADR creado. AdrId={AdrId} ProjectId={ProjectId}",
                    result.AdrId, projectId);

                return StatusCode(StatusCodes.Status201Created, result);
            }
            catch (GovernanceException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── POST /governance/projects/{projectId}/adrs/ai ─────────────────────

        [HttpPost("projects/{projectId:guid}/adrs/ai")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> CreateAdrWithAi(
            Guid projectId,
            [FromBody] CreateAdrWithAiHttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new CreateAdrWithAiRequest
                {
                    ProjectId = projectId,
                    Description = req.Description,
                    Context = req.Context
                };

                var result = await _service.CreateAdrWithAiAsync(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[GovernanceController] ADR AI generado. ProjectId={ProjectId} Model={Model}",
                    projectId, result.Model);

                return Accepted(result);
            }
            catch (GovernanceException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── PUT /governance/projects/{projectId}/gates ────────────────────────

        [HttpPut("projects/{projectId:guid}/gates")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> ConfigureGates(
            Guid projectId,
            [FromBody] ConfigureGatesHttpRequest req,
            CancellationToken ct)
        {
            var context = ExtractContext();
            if (context.Error != null)
                return BadRequest(new { code = "BAD_REQUEST", message = context.Error });

            try
            {
                var request = new ConfigureQualityGatesRequest
                {
                    ProjectId = projectId,
                    Gates = req.Gates
                };

                var result = await _service.ConfigureQualityGatesAsync(
                    request, context.ActorId, context.ActorRole, context.CorrelationId, ct);

                _logger.LogInformation(
                    "[GovernanceController] Quality gates configurados. ProjectId={ProjectId} Count={Count}",
                    projectId, result.Count);

                return Ok(result);
            }
            catch (GovernanceException ex)
            {
                return StatusCode(ex.HttpStatus, new { code = ex.ErrorCode, message = ex.Message });
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

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

    // ── HTTP Request records ──────────────────────────────────────────────────

    public record CreateProjectHttpRequest(
        string Name,
        string? Description,
        string? GitRepoUrl);

    public record CreateAdrHttpRequest(
        int Number,
        string Title,
        AdrStatus Status,
        string Content);

    public record CreateAdrWithAiHttpRequest(
        string Description,
        string? Context);

    public record ConfigureGatesHttpRequest(
        List<QualityGateConfig> Gates);
}
