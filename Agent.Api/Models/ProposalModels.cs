using Domain.Entities;
using System.ComponentModel.DataAnnotations;

namespace Agent.Api.Models
{
    // ─── Request Models ───────────────────────────────────────────────────────

    public class CreateProposalHttpRequest
    {
        [Required]
        [MaxLength(200)]
        public required string Name { get; init; }

        [Required]
        [MaxLength(200)]
        public required string ProjectName { get; init; }

        [Required]
        public required string CreatedByUserId { get; init; }

        [Required]
        public required string CreatedByUserName { get; init; }

        [Required]
        public required string ReviewerUserId { get; init; }

        [Required]
        public required string ReviewerUserName { get; init; }

        [Required]
        public required string ApproverUserId { get; init; }

        [Required]
        public required string ApproverUserName { get; init; }

        public List<string> Tags { get; init; } = new();
    }

    public class AddCommentHttpRequest
    {
        [Required]
        public required string AuthorId { get; init; }

        [Required]
        public required string AuthorName { get; init; }

        [Required]
        public required ProposalRole AuthorRole { get; init; }

        [Required]
        [MaxLength(4000)]
        public required string Body { get; init; }

        public int IterationVersion { get; init; } = 1;
    }

    public class DecisionHttpRequest
    {
        [Required]
        public required string UserId { get; init; }

        /// <summary>
        /// "Approve", "Reject", "RequestChanges"
        /// </summary>
        [Required]
        public required string Decision { get; init; }

        [MaxLength(1000)]
        public string? Note { get; init; }
    }

    public class SubmitForReviewHttpRequest
    {
        [Required]
        public required string UserId { get; init; }
    }

    public class AddIterationHttpRequest
    {
        /// <summary>
        /// Markdown completo de la última respuesta del agente.
        /// El frontend lo toma de lastCompletedContent y lo envía aquí.
        /// </summary>
        [Required]
        public required string Content { get; init; }

        public List<string> Components { get; init; } = new();
        public int TeamSize { get; init; }
        public int DurationWeeks { get; init; }
        public decimal BudgetUsd { get; init; }
        public RiskLevel RiskLevel { get; init; } = RiskLevel.Medium;
    }

    // ─── Response Models ──────────────────────────────────────────────────────

    public class ProposalDto
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string ProjectName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public Guid SessionId { get; init; }
        public string CreatedByUserId { get; init; } = string.Empty;
        public List<string> Tags { get; init; } = new();
        public DateTime CreatedAt { get; init; }
        public DateTime UpdatedAt { get; init; }
        public List<ProposalIterationDto> Iterations { get; init; } = new();
        public List<ProposalCommentDto> Comments { get; init; } = new();
        public List<ProposalApprovalStepDto> ApprovalFlow { get; init; } = new();
    }

    public class ProposalIterationDto
    {
        public int Version { get; init; }
        public string Content { get; init; } = string.Empty;
        public List<string> Components { get; init; } = new();
        public int TeamSize { get; init; }
        public int DurationWeeks { get; init; }
        public decimal BudgetUsd { get; init; }
        public string RiskLevel { get; init; } = string.Empty;
        public DateTime CreatedAt { get; init; }
    }

    public class ProposalCommentDto
    {
        public Guid Id { get; init; }
        public string AuthorId { get; init; } = string.Empty;
        public string AuthorName { get; init; } = string.Empty;
        public string AuthorRole { get; init; } = string.Empty;
        public string Body { get; init; } = string.Empty;
        public int IterationVersion { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? ResolvedAt { get; init; }
    }

    public class ProposalApprovalStepDto
    {
        public Guid Id { get; init; }
        public string Role { get; init; } = string.Empty;
        public string UserId { get; init; } = string.Empty;
        public string UserName { get; init; } = string.Empty;
        public string Status { get; init; } = string.Empty;
        public string? Note { get; init; }
        public DateTime? DecidedAt { get; init; }
    }

    // ─── Mapper ───────────────────────────────────────────────────────────────

    public static class ProposalMapper
    {
        public static ProposalDto ToDto(Proposal p) => new()
        {
            Id = p.Id,
            Name = p.Name,
            ProjectName = p.ProjectName,
            Status = p.Status.ToString(),
            SessionId = p.SessionId,
            CreatedByUserId = p.CreatedByUserId,
            Tags = p.Tags,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            Iterations = p.Iterations.Select(i => new ProposalIterationDto
            {
                Version = i.Version,
                Content = i.Content,
                Components = i.Components,
                TeamSize = i.TeamSize,
                DurationWeeks = i.DurationWeeks,
                BudgetUsd = i.BudgetUsd,
                RiskLevel = i.RiskLevel.ToString(),
                CreatedAt = i.CreatedAt
            }).ToList(),
            Comments = p.Comments.Select(c => new ProposalCommentDto
            {
                Id = c.Id,
                AuthorId = c.AuthorId,
                AuthorName = c.AuthorName,
                AuthorRole = c.AuthorRole.ToString(),
                Body = c.Body,
                IterationVersion = c.IterationVersion,
                CreatedAt = c.CreatedAt,
                ResolvedAt = c.ResolvedAt
            }).ToList(),
            ApprovalFlow = p.ApprovalFlow.Select(s => new ProposalApprovalStepDto
            {
                Id = s.Id,
                Role = s.Role.ToString(),
                UserId = s.UserId,
                UserName = s.UserName,
                Status = s.Status.ToString(),
                Note = s.Note,
                DecidedAt = s.DecidedAt
            }).ToList()
        };
    }
}
