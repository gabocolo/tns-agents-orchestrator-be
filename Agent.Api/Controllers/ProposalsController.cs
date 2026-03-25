using Agent.Api.Models;
using Application.Proposals;
using Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Agent.Api.Controllers
{
    /// <summary>
    /// CRUD transaccional de propuestas de arquitectura.
    /// El flujo de IA (conversación + generación de iteraciones)
    /// ocurre en EntryPoints.Web via SSE con el ArchitectureAgent.
    /// </summary>
    [ApiController]
    [Route("proposals")]
    public class ProposalsController : ControllerBase
    {
        private readonly IProposalService _service;
        private readonly ILogger<ProposalsController> _logger;

        public ProposalsController(IProposalService service, ILogger<ProposalsController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // ─── GET api/proposals ────────────────────────────────────────────────

        [HttpGet]
        [ProducesResponseType(typeof(List<ProposalDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetAll(
            [FromQuery] string? status,
            [FromQuery] string? userId,
            [FromQuery] string? role,
            CancellationToken ct)
        {
            var filters = new ProposalFilters
            {
                Status = Enum.TryParse<ProposalStatus>(status, true, out var s) ? s : null,
                UserId = userId,
                Role = Enum.TryParse<ProposalRole>(role, true, out var r) ? r : null
            };

            var proposals = await _service.GetAllProposalsAsync(filters, ct);
            return Ok(proposals.Select(ProposalMapper.ToDto));
        }

        // ─── GET api/proposals/{id} ───────────────────────────────────────────

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
        {
            var proposal = await _service.GetProposalByIdAsync(id, ct);
            if (proposal == null) return NotFound();
            return Ok(ProposalMapper.ToDto(proposal));
        }

        // ─── POST api/proposals ───────────────────────────────────────────────

        [HttpPost]
        [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Create(
            [FromBody] CreateProposalHttpRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var createRequest = new CreateProposalRequest
            {
                Name = request.Name,
                ProjectName = request.ProjectName,
                CreatedByUserId = request.CreatedByUserId,
                CreatedByUserName = request.CreatedByUserName,
                ReviewerUserId = request.ReviewerUserId,
                ReviewerUserName = request.ReviewerUserName,
                ApproverUserId = request.ApproverUserId,
                ApproverUserName = request.ApproverUserName,
                Tags = request.Tags
            };

            var proposal = await _service.CreateProposalAsync(createRequest, ct);

            _logger.LogInformation("[ProposalsController] Propuesta creada. Id={Id}", proposal.Id);

            return CreatedAtAction(
                nameof(GetById),
                new { id = proposal.Id },
                ProposalMapper.ToDto(proposal)
            );
        }

        // ─── POST proposals/{id}/iterations ──────────────────────────────────
        // "Guardar como iteración" — checkpoint manual del usuario

        [HttpPost("{id:guid}/iterations")]
        [ProducesResponseType(typeof(ProposalIterationDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddIteration(
            Guid id,
            [FromBody] AddIterationHttpRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var iteration = new ProposalIteration
                {
                    Content       = request.Content,
                    Components    = request.Components,
                    TeamSize      = request.TeamSize,
                    DurationWeeks = request.DurationWeeks,
                    BudgetUsd     = request.BudgetUsd,
                    RiskLevel     = request.RiskLevel
                };

                var saved = await _service.AddIterationAsync(id, iteration, ct);

                _logger.LogInformation(
                    "[ProposalsController] Iteración v{Version} guardada. ProposalId={Id}",
                    saved.Version, id);

                return StatusCode(StatusCodes.Status201Created, new ProposalIterationDto
                {
                    Version       = saved.Version,
                    Content       = saved.Content,
                    Components    = saved.Components,
                    TeamSize      = saved.TeamSize,
                    DurationWeeks = saved.DurationWeeks,
                    BudgetUsd     = saved.BudgetUsd,
                    RiskLevel     = saved.RiskLevel.ToString(),
                    CreatedAt     = saved.CreatedAt
                });
            }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        // ─── POST proposals/{id}/submit ───────────────────────────────────────

        [HttpPost("{id:guid}/submit")]
        [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Submit(
            Guid id,
            [FromBody] SubmitForReviewHttpRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var proposal = await _service.SubmitForReviewAsync(id, request.UserId, ct);
                return Ok(ProposalMapper.ToDto(proposal));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        // ─── POST api/proposals/{id}/comments ────────────────────────────────

        [HttpPost("{id:guid}/comments")]
        [ProducesResponseType(typeof(ProposalCommentDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> AddComment(
            Guid id,
            [FromBody] AddCommentHttpRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var addRequest = new AddCommentRequest
                {
                    AuthorId = request.AuthorId,
                    AuthorName = request.AuthorName,
                    AuthorRole = request.AuthorRole,
                    Body = request.Body,
                    IterationVersion = request.IterationVersion
                };

                var comment = await _service.AddCommentAsync(id, addRequest, ct);

                return StatusCode(StatusCodes.Status201Created, new ProposalCommentDto
                {
                    Id = comment.Id,
                    AuthorId = comment.AuthorId,
                    AuthorName = comment.AuthorName,
                    AuthorRole = comment.AuthorRole.ToString(),
                    Body = comment.Body,
                    IterationVersion = comment.IterationVersion,
                    CreatedAt = comment.CreatedAt
                });
            }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        // ─── POST proposals/{id}/decisions (y /decide por compatibilidad) ──

        [HttpPost("{id:guid}/decisions")]
        [HttpPost("{id:guid}/decide")]
        [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Decide(
            Guid id,
            [FromBody] DecisionHttpRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                var decisionRequest = new DecisionRequest
                {
                    UserId = request.UserId,
                    Decision = request.Decision,
                    Note = request.Note
                };

                var proposal = await _service.DecideAsync(id, decisionRequest, ct);
                return Ok(ProposalMapper.ToDto(proposal));
            }
            catch (KeyNotFoundException) { return NotFound(); }
            catch (UnauthorizedAccessException ex) { return Forbid(ex.Message); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        }

        // ─── PATCH proposals/{id} ─────────────────────────────────────────────
        // Actualización directa de status desde el tablero Kanban (drag & drop).
        // No reemplaza los flujos de workflow (submit, decide) — es solo para admin/manager.

        [HttpPatch("{id:guid}")]
        [ProducesResponseType(typeof(ProposalDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> PatchStatus(
            Guid id,
            [FromBody] PatchStatusHttpRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (!Enum.IsDefined(typeof(ProposalStatus), request.Status))
                return BadRequest(new { error = $"Estado inválido: {request.Status}. Valores válidos: 0=Borrador, 1=En revisión, 2=Pendiente de aprobación, 3=Aprobada, 4=Rechazada." });

            try
            {
                var newStatus = (ProposalStatus)request.Status;
                var proposal = await _service.UpdateStatusAsync(id, newStatus, ct);
                return Ok(ProposalMapper.ToDto(proposal));
            }
            catch (KeyNotFoundException) { return NotFound(); }
        }

        // ─── DELETE api/proposals/{id} ────────────────────────────────────────

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            try
            {
                var deleted = await _service.DeleteProposalAsync(id, ct);
                if (!deleted) return NotFound();
                return NoContent();
            }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }
    }
}
