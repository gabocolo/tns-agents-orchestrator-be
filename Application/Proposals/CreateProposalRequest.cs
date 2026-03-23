namespace Application.Proposals
{
    public class CreateProposalRequest
    {
        public required string Name { get; init; }
        public required string ProjectName { get; init; }

        // Builder = quien crea la propuesta
        public required string CreatedByUserId { get; init; }
        public required string CreatedByUserName { get; init; }

        // Reviewer asignado
        public required string ReviewerUserId { get; init; }
        public required string ReviewerUserName { get; init; }

        // Approver asignado
        public required string ApproverUserId { get; init; }
        public required string ApproverUserName { get; init; }

        public List<string> Tags { get; init; } = new();
    }
}
