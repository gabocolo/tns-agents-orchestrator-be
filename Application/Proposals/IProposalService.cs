using Domain.Entities;

namespace Application.Proposals
{
    public interface IProposalService
    {
        Task<Proposal> CreateProposalAsync(CreateProposalRequest request, CancellationToken ct = default);
        Task<Proposal?> GetProposalByIdAsync(Guid id, CancellationToken ct = default);
        Task<List<Proposal>> GetAllProposalsAsync(ProposalFilters filters, CancellationToken ct = default);
        Task<ProposalIteration> AddIterationAsync(Guid proposalId, ProposalIteration iteration, CancellationToken ct = default);
        Task<Proposal> SubmitForReviewAsync(Guid proposalId, string userId, CancellationToken ct = default);
        Task<ProposalComment> AddCommentAsync(Guid proposalId, AddCommentRequest request, CancellationToken ct = default);
        Task<Proposal> DecideAsync(Guid proposalId, DecisionRequest request, CancellationToken ct = default);
        Task<bool> DeleteProposalAsync(Guid proposalId, CancellationToken ct = default);
    }
}
