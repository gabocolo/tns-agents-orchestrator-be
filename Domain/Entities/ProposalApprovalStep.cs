namespace Domain.Entities
{
    public class ProposalApprovalStep
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProposalId { get; set; }
        public ProposalRole Role { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public ApprovalStepStatus Status { get; set; } = ApprovalStepStatus.Pending;
        public string? Note { get; set; }
        public DateTime? DecidedAt { get; set; }
    }
}
