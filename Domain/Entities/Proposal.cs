namespace Domain.Entities
{
    public class Proposal
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public ProposalStatus Status { get; set; } = ProposalStatus.Draft;

        /// <summary>
        /// SessionId compartido con el ArchitectureAgent para el historial de conversación.
        /// </summary>
        public Guid SessionId { get; set; }

        public string CreatedByUserId { get; set; } = string.Empty;

        public List<ProposalIteration> Iterations { get; set; } = new();
        public List<ProposalComment> Comments { get; set; } = new();
        public List<ProposalApprovalStep> ApprovalFlow { get; set; } = new();
        public List<string> Tags { get; set; } = new();

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
