using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IProposalRepository
    {
        Task<Proposal?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<List<Proposal>> GetAllAsync(ProposalFilters filters, CancellationToken ct = default);
        Task<Proposal> CreateAsync(Proposal proposal, CancellationToken ct = default);
        Task<Proposal> UpdateAsync(Proposal proposal, CancellationToken ct = default);
        Task<ProposalIteration> AddIterationAsync(Guid proposalId, ProposalIteration iteration, CancellationToken ct = default);
        Task<ProposalComment> AddCommentAsync(ProposalComment comment, CancellationToken ct = default);
        Task<ProposalApprovalStep> UpdateApprovalStepAsync(ProposalApprovalStep step, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}
