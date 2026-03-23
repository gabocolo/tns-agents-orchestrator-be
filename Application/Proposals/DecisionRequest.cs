namespace Application.Proposals
{
    public class DecisionRequest
    {
        public required string UserId { get; init; }

        /// <summary>
        /// "Approve", "Reject", "RequestChanges"
        /// </summary>
        public required string Decision { get; init; }

        public string? Note { get; init; }
    }
}
