using Domain.Entities;

namespace Application.Proposals
{
    public class AddCommentRequest
    {
        public required string AuthorId { get; init; }
        public required string AuthorName { get; init; }
        public required ProposalRole AuthorRole { get; init; }
        public required string Body { get; init; }
        public int IterationVersion { get; init; }
    }
}
