using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Proposals
{
    public class ProposalService : IProposalService
    {
        private readonly IProposalRepository _repo;
        private readonly ILogger<ProposalService> _logger;

        public ProposalService(IProposalRepository repo, ILogger<ProposalService> logger)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<Proposal> CreateProposalAsync(CreateProposalRequest request, CancellationToken ct = default)
        {
            var proposal = new Proposal
            {
                Id = Guid.NewGuid(),
                SessionId = Guid.NewGuid(),
                Name = request.Name,
                ProjectName = request.ProjectName,
                CreatedByUserId = request.CreatedByUserId,
                Status = ProposalStatus.Draft,
                Tags = request.Tags,
                ApprovalFlow = new List<ProposalApprovalStep>
                {
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Role = ProposalRole.Builder,
                        UserId = request.CreatedByUserId,
                        UserName = request.CreatedByUserName,
                        Status = ApprovalStepStatus.Pending
                    },
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Role = ProposalRole.Reviewer,
                        UserId = request.ReviewerUserId,
                        UserName = request.ReviewerUserName,
                        Status = ApprovalStepStatus.Pending
                    },
                    new()
                    {
                        Id = Guid.NewGuid(),
                        Role = ProposalRole.Approver,
                        UserId = request.ApproverUserId,
                        UserName = request.ApproverUserName,
                        Status = ApprovalStepStatus.Pending
                    }
                }
            };

            var created = await _repo.CreateAsync(proposal, ct);

            _logger.LogInformation(
                "[ProposalService] Propuesta creada. Id={Id} Name={Name} SessionId={SessionId}",
                created.Id, created.Name, created.SessionId
            );

            return created;
        }

        public Task<Proposal?> GetProposalByIdAsync(Guid id, CancellationToken ct = default)
            => _repo.GetByIdAsync(id, ct);

        public Task<List<Proposal>> GetAllProposalsAsync(ProposalFilters filters, CancellationToken ct = default)
            => _repo.GetAllAsync(filters, ct);

        public async Task<ProposalIteration> AddIterationAsync(
            Guid proposalId,
            ProposalIteration iteration,
            CancellationToken ct = default)
        {
            var proposal = await _repo.GetByIdAsync(proposalId, ct)
                ?? throw new KeyNotFoundException($"Propuesta {proposalId} no encontrada.");

            var nextVersion = proposal.Iterations.Count == 0
                ? 1
                : proposal.Iterations.Max(i => i.Version) + 1;

            iteration.Version = nextVersion;
            iteration.CreatedAt = DateTime.UtcNow;

            var saved = await _repo.AddIterationAsync(proposalId, iteration, ct);

            _logger.LogInformation(
                "[ProposalService] Iteración v{Version} agregada. ProposalId={Id}",
                saved.Version, proposalId
            );

            return saved;
        }

        public async Task<Proposal> SubmitForReviewAsync(
            Guid proposalId,
            string userId,
            CancellationToken ct = default)
        {
            var proposal = await _repo.GetByIdAsync(proposalId, ct)
                ?? throw new KeyNotFoundException($"Propuesta {proposalId} no encontrada.");

            if (proposal.CreatedByUserId != userId)
                throw new UnauthorizedAccessException("Solo el builder puede enviar la propuesta a revisión.");

            if (proposal.Status != ProposalStatus.Draft)
                throw new InvalidOperationException($"Solo se puede enviar a revisión una propuesta en Draft. Estado actual: {proposal.Status}.");

            // Aprueba el step del builder y cambia el estado
            var builderStep = proposal.ApprovalFlow.First(s => s.Role == ProposalRole.Builder);
            builderStep.Status = ApprovalStepStatus.Approved;
            builderStep.DecidedAt = DateTime.UtcNow;

            proposal.Status = ProposalStatus.InReview;
            proposal.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateApprovalStepAsync(builderStep, ct);
            var updated = await _repo.UpdateAsync(proposal, ct);

            _logger.LogInformation(
                "[ProposalService] Propuesta enviada a revisión. Id={Id}", proposalId
            );

            return updated;
        }

        public async Task<ProposalComment> AddCommentAsync(
            Guid proposalId,
            AddCommentRequest request,
            CancellationToken ct = default)
        {
            _ = await _repo.GetByIdAsync(proposalId, ct)
                ?? throw new KeyNotFoundException($"Propuesta {proposalId} no encontrada.");

            var comment = new ProposalComment
            {
                Id = Guid.NewGuid(),
                ProposalId = proposalId,
                AuthorId = request.AuthorId,
                AuthorName = request.AuthorName,
                AuthorRole = request.AuthorRole,
                Body = request.Body,
                IterationVersion = request.IterationVersion,
                CreatedAt = DateTime.UtcNow
            };

            return await _repo.AddCommentAsync(comment, ct);
        }

        public async Task<Proposal> DecideAsync(
            Guid proposalId,
            DecisionRequest request,
            CancellationToken ct = default)
        {
            var proposal = await _repo.GetByIdAsync(proposalId, ct)
                ?? throw new KeyNotFoundException($"Propuesta {proposalId} no encontrada.");

            var decision = request.Decision.ToLowerInvariant();

            // Determina qué step corresponde al userId
            var step = proposal.ApprovalFlow.FirstOrDefault(s => s.UserId == request.UserId)
                ?? throw new UnauthorizedAccessException($"El usuario {request.UserId} no participa en el flujo de aprobación.");

            switch (step.Role)
            {
                case ProposalRole.Reviewer:
                    ValidateStatus(proposal, ProposalStatus.InReview, "El reviewer solo puede decidir cuando la propuesta está InReview.");

                    if (decision == "approve")
                    {
                        step.Status = ApprovalStepStatus.Approved;
                        proposal.Status = ProposalStatus.PendingApproval;
                    }
                    else if (decision == "requestchanges")
                    {
                        step.Status = ApprovalStepStatus.ChangesRequested;
                        // Resetea el step del builder para que vuelva a trabajar en ella
                        var builderStep = proposal.ApprovalFlow.First(s => s.Role == ProposalRole.Builder);
                        builderStep.Status = ApprovalStepStatus.Pending;
                        builderStep.DecidedAt = null;
                        await _repo.UpdateApprovalStepAsync(builderStep, ct);
                        proposal.Status = ProposalStatus.Draft;
                    }
                    else
                    {
                        throw new ArgumentException($"Decisión no válida para Reviewer: '{request.Decision}'. Use 'Approve' o 'RequestChanges'.");
                    }
                    break;

                case ProposalRole.Approver:
                    ValidateStatus(proposal, ProposalStatus.PendingApproval, "El approver solo puede decidir cuando la propuesta está PendingApproval.");

                    if (decision == "approve")
                    {
                        step.Status = ApprovalStepStatus.Approved;
                        proposal.Status = ProposalStatus.Approved;
                    }
                    else if (decision == "reject")
                    {
                        step.Status = ApprovalStepStatus.Rejected;
                        proposal.Status = ProposalStatus.Rejected;
                    }
                    else
                    {
                        throw new ArgumentException($"Decisión no válida para Approver: '{request.Decision}'. Use 'Approve' o 'Reject'.");
                    }
                    break;

                default:
                    throw new InvalidOperationException($"El rol {step.Role} no puede tomar decisiones en el flujo.");
            }

            step.Note = request.Note;
            step.DecidedAt = DateTime.UtcNow;
            proposal.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateApprovalStepAsync(step, ct);
            var updated = await _repo.UpdateAsync(proposal, ct);

            _logger.LogInformation(
                "[ProposalService] Decisión registrada. ProposalId={Id} Role={Role} Decision={Decision}",
                proposalId, step.Role, request.Decision
            );

            return updated;
        }

        public async Task<Proposal> UpdateStatusAsync(
            Guid proposalId,
            ProposalStatus newStatus,
            CancellationToken ct = default)
        {
            var proposal = await _repo.GetByIdAsync(proposalId, ct)
                ?? throw new KeyNotFoundException($"Propuesta {proposalId} no encontrada.");

            proposal.Status = newStatus;
            proposal.UpdatedAt = DateTime.UtcNow;

            var updated = await _repo.UpdateAsync(proposal, ct);

            _logger.LogInformation(
                "[ProposalService] Status actualizado via Kanban. Id={Id} NuevoStatus={Status}",
                proposalId, newStatus);

            return updated;
        }

        public async Task<bool> DeleteProposalAsync(Guid proposalId, CancellationToken ct = default)
        {
            var deleted = await _repo.DeleteAsync(proposalId, ct);
            if (deleted)
                _logger.LogInformation("[ProposalService] Propuesta eliminada. Id={Id}", proposalId);
            return deleted;
        }

        private static void ValidateStatus(Proposal proposal, ProposalStatus expected, string message)
        {
            if (proposal.Status != expected)
                throw new InvalidOperationException(message);
        }
    }
}
