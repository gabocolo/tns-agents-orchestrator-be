using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Data.SqlClient;
using System.Text;

namespace Infrastructure.Persistence
{
    public class ProposalRepository : IProposalRepository
    {
        private readonly string _connectionString;
        private readonly ILogger<ProposalRepository> _logger;

        public ProposalRepository(string connectionString, ILogger<ProposalRepository> logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        // ─── GetByIdAsync ─────────────────────────────────────────────────────────

        public async Task<Proposal?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var proposal = await QueryProposalAsync(conn, id, ct);
            if (proposal == null) return null;

            proposal.Iterations  = await QueryIterationsAsync(conn, id, ct);
            proposal.Comments    = await QueryCommentsAsync(conn, id, ct);
            proposal.ApprovalFlow = await QueryApprovalFlowAsync(conn, id, ct);

            return proposal;
        }

        // ─── GetAllAsync ──────────────────────────────────────────────────────────

        public async Task<List<Proposal>> GetAllAsync(ProposalFilters filters, CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var sql = new StringBuilder("""
                SELECT p.Id, p.Name, p.ProjectName, p.Status, p.SessionId,
                       p.CreatedByUserId, p.Tags, p.CreatedAt, p.UpdatedAt
                FROM Proposals p
                WHERE 1=1
                """);

            var cmd = conn.CreateCommand();

            if (filters.Status.HasValue)
            {
                sql.Append(" AND p.Status = @Status");
                cmd.Parameters.AddWithValue("@Status", (int)filters.Status.Value);
            }

            if (!string.IsNullOrEmpty(filters.UserId))
            {
                if (filters.Role.HasValue)
                {
                    sql.Append("""
                         AND EXISTS (
                             SELECT 1 FROM ProposalApprovalSteps s
                             WHERE s.ProposalId = p.Id
                               AND s.UserId = @UserId
                               AND s.Role = @Role
                         )
                        """);
                    cmd.Parameters.AddWithValue("@UserId", filters.UserId);
                    cmd.Parameters.AddWithValue("@Role", (int)filters.Role.Value);
                }
                else
                {
                    sql.Append("""
                         AND EXISTS (
                             SELECT 1 FROM ProposalApprovalSteps s
                             WHERE s.ProposalId = p.Id AND s.UserId = @UserId
                         )
                        """);
                    cmd.Parameters.AddWithValue("@UserId", filters.UserId);
                }
            }

            sql.Append(" ORDER BY p.CreatedAt DESC");
            cmd.CommandText = sql.ToString();

            var proposals = new List<Proposal>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                proposals.Add(MapProposal(reader));
            await reader.CloseAsync();

            foreach (var proposal in proposals)
            {
                proposal.Iterations   = await QueryIterationsAsync(conn, proposal.Id, ct);
                proposal.Comments     = await QueryCommentsAsync(conn, proposal.Id, ct);
                proposal.ApprovalFlow = await QueryApprovalFlowAsync(conn, proposal.Id, ct);
            }

            return proposals;
        }

        // ─── CreateAsync ──────────────────────────────────────────────────────────

        public async Task<Proposal> CreateAsync(Proposal proposal, CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = conn.BeginTransaction();

            try
            {
                var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = """
                    INSERT INTO Proposals
                        (Id, Name, ProjectName, Status, SessionId, CreatedByUserId, Tags, CreatedAt, UpdatedAt)
                    VALUES
                        (@Id, @Name, @ProjectName, @Status, @SessionId, @CreatedByUserId, @Tags, @CreatedAt, @UpdatedAt)
                    """;

                cmd.Parameters.AddWithValue("@Id", proposal.Id);
                cmd.Parameters.AddWithValue("@Name", proposal.Name);
                cmd.Parameters.AddWithValue("@ProjectName", proposal.ProjectName);
                cmd.Parameters.AddWithValue("@Status", (int)proposal.Status);
                cmd.Parameters.AddWithValue("@SessionId", proposal.SessionId);
                cmd.Parameters.AddWithValue("@CreatedByUserId", proposal.CreatedByUserId);
                cmd.Parameters.AddWithValue("@Tags", string.Join(",", proposal.Tags));
                cmd.Parameters.AddWithValue("@CreatedAt", proposal.CreatedAt);
                cmd.Parameters.AddWithValue("@UpdatedAt", proposal.UpdatedAt);

                await cmd.ExecuteNonQueryAsync(ct);

                foreach (var step in proposal.ApprovalFlow)
                {
                    step.ProposalId = proposal.Id;
                    await InsertApprovalStepAsync(conn, tx, step, ct);
                }

                await tx.CommitAsync(ct);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }

            _logger.LogInformation("[ProposalRepository] Propuesta creada. Id={Id}", proposal.Id);
            return proposal;
        }

        // ─── UpdateAsync ──────────────────────────────────────────────────────────

        public async Task<Proposal> UpdateAsync(Proposal proposal, CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE Proposals
                SET Status = @Status, UpdatedAt = @UpdatedAt
                WHERE Id = @Id
                """;

            cmd.Parameters.AddWithValue("@Status", (int)proposal.Status);
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow);
            cmd.Parameters.AddWithValue("@Id", proposal.Id);

            await cmd.ExecuteNonQueryAsync(ct);
            return proposal;
        }

        // ─── AddIterationAsync ────────────────────────────────────────────────────

        public async Task<ProposalIteration> AddIterationAsync(
            Guid proposalId,
            ProposalIteration iteration,
            CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ProposalIterations
                    (ProposalId, Version, Content, Components, TeamSize, DurationWeeks, BudgetUsd, RiskLevel, CreatedAt)
                VALUES
                    (@ProposalId, @Version, @Content, @Components, @TeamSize, @DurationWeeks, @BudgetUsd, @RiskLevel, @CreatedAt)
                """;

            cmd.Parameters.AddWithValue("@ProposalId", proposalId);
            cmd.Parameters.AddWithValue("@Version", iteration.Version);
            cmd.Parameters.AddWithValue("@Content", iteration.Content);
            cmd.Parameters.AddWithValue("@Components", string.Join(",", iteration.Components));
            cmd.Parameters.AddWithValue("@TeamSize", iteration.TeamSize);
            cmd.Parameters.AddWithValue("@DurationWeeks", iteration.DurationWeeks);
            cmd.Parameters.AddWithValue("@BudgetUsd", iteration.BudgetUsd);
            cmd.Parameters.AddWithValue("@RiskLevel", (int)iteration.RiskLevel);
            cmd.Parameters.AddWithValue("@CreatedAt", iteration.CreatedAt);

            await cmd.ExecuteNonQueryAsync(ct);
            return iteration;
        }

        // ─── AddCommentAsync ──────────────────────────────────────────────────────

        public async Task<ProposalComment> AddCommentAsync(
            ProposalComment comment,
            CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO ProposalComments
                    (Id, ProposalId, AuthorId, AuthorName, AuthorRole, Body, IterationVersion, CreatedAt)
                VALUES
                    (@Id, @ProposalId, @AuthorId, @AuthorName, @AuthorRole, @Body, @IterationVersion, @CreatedAt)
                """;

            cmd.Parameters.AddWithValue("@Id", comment.Id);
            cmd.Parameters.AddWithValue("@ProposalId", comment.ProposalId);
            cmd.Parameters.AddWithValue("@AuthorId", comment.AuthorId);
            cmd.Parameters.AddWithValue("@AuthorName", comment.AuthorName);
            cmd.Parameters.AddWithValue("@AuthorRole", (int)comment.AuthorRole);
            cmd.Parameters.AddWithValue("@Body", comment.Body);
            cmd.Parameters.AddWithValue("@IterationVersion", comment.IterationVersion);
            cmd.Parameters.AddWithValue("@CreatedAt", comment.CreatedAt);

            await cmd.ExecuteNonQueryAsync(ct);
            return comment;
        }

        // ─── UpdateApprovalStepAsync ──────────────────────────────────────────────

        public async Task<ProposalApprovalStep> UpdateApprovalStepAsync(
            ProposalApprovalStep step,
            CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                UPDATE ProposalApprovalSteps
                SET Status = @Status, Note = @Note, DecidedAt = @DecidedAt
                WHERE Id = @Id
                """;

            cmd.Parameters.AddWithValue("@Status", (int)step.Status);
            cmd.Parameters.AddWithValue("@Note", (object?)step.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DecidedAt", (object?)step.DecidedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Id", step.Id);

            await cmd.ExecuteNonQueryAsync(ct);
            return step;
        }

        // ─── DeleteAsync ──────────────────────────────────────────────────────────

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);

            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Proposals WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", id);

            var rows = await cmd.ExecuteNonQueryAsync(ct);
            return rows > 0;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private static async Task<Proposal?> QueryProposalAsync(
            SqlConnection conn, Guid id, CancellationToken ct)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Name, ProjectName, Status, SessionId,
                       CreatedByUserId, Tags, CreatedAt, UpdatedAt
                FROM Proposals
                WHERE Id = @Id
                """;
            cmd.Parameters.AddWithValue("@Id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return MapProposal(reader);
        }

        private static async Task<List<ProposalIteration>> QueryIterationsAsync(
            SqlConnection conn, Guid proposalId, CancellationToken ct)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Version, Content, Components, TeamSize, DurationWeeks, BudgetUsd, RiskLevel, CreatedAt
                FROM ProposalIterations
                WHERE ProposalId = @ProposalId
                ORDER BY Version ASC
                """;
            cmd.Parameters.AddWithValue("@ProposalId", proposalId);

            var list = new List<ProposalIteration>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                var componentsRaw = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                list.Add(new ProposalIteration
                {
                    Version       = reader.GetInt32(0),
                    Content       = reader.GetString(1),
                    Components    = string.IsNullOrEmpty(componentsRaw)
                                        ? new()
                                        : componentsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    TeamSize      = reader.IsDBNull(3) ? 0 : reader.GetInt32(3),
                    DurationWeeks = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
                    BudgetUsd     = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                    RiskLevel     = (RiskLevel)Convert.ToInt32(reader[6]),
                    CreatedAt     = reader.GetDateTime(7)
                });
            }

            return list;
        }

        private static async Task<List<ProposalComment>> QueryCommentsAsync(
            SqlConnection conn, Guid proposalId, CancellationToken ct)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ProposalId, AuthorId, AuthorName, AuthorRole,
                       Body, IterationVersion, CreatedAt, ResolvedAt
                FROM ProposalComments
                WHERE ProposalId = @ProposalId
                ORDER BY CreatedAt ASC
                """;
            cmd.Parameters.AddWithValue("@ProposalId", proposalId);

            var list = new List<ProposalComment>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                list.Add(new ProposalComment
                {
                    Id               = reader.GetGuid(0),
                    ProposalId       = reader.GetGuid(1),
                    AuthorId         = reader.GetString(2),
                    AuthorName       = reader.GetString(3),
                    AuthorRole       = (ProposalRole)Convert.ToInt32(reader[4]),
                    Body             = reader.GetString(5),
                    IterationVersion = reader.GetInt32(6),
                    CreatedAt        = reader.GetDateTime(7),
                    ResolvedAt       = reader.IsDBNull(8) ? null : reader.GetDateTime(8)
                });
            }

            return list;
        }

        private static async Task<List<ProposalApprovalStep>> QueryApprovalFlowAsync(
            SqlConnection conn, Guid proposalId, CancellationToken ct)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT Id, ProposalId, Role, UserId, UserName, Status, Note, DecidedAt
                FROM ProposalApprovalSteps
                WHERE ProposalId = @ProposalId
                ORDER BY Role ASC
                """;
            cmd.Parameters.AddWithValue("@ProposalId", proposalId);

            var list = new List<ProposalApprovalStep>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            while (await reader.ReadAsync(ct))
            {
                list.Add(new ProposalApprovalStep
                {
                    Id         = reader.GetGuid(0),
                    ProposalId = reader.GetGuid(1),
                    Role       = (ProposalRole)Convert.ToInt32(reader[2]),
                    UserId     = reader.GetString(3),
                    UserName   = reader.GetString(4),
                    Status     = (ApprovalStepStatus)Convert.ToInt32(reader[5]),
                    Note       = reader.IsDBNull(6) ? null : reader.GetString(6),
                    DecidedAt  = reader.IsDBNull(7) ? null : reader.GetDateTime(7)
                });
            }

            return list;
        }

        private static async Task InsertApprovalStepAsync(
            SqlConnection conn,
            SqlTransaction tx,
            ProposalApprovalStep step,
            CancellationToken ct)
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO ProposalApprovalSteps
                    (Id, ProposalId, Role, UserId, UserName, Status, Note, DecidedAt)
                VALUES
                    (@Id, @ProposalId, @Role, @UserId, @UserName, @Status, @Note, @DecidedAt)
                """;

            cmd.Parameters.AddWithValue("@Id", step.Id);
            cmd.Parameters.AddWithValue("@ProposalId", step.ProposalId);
            cmd.Parameters.AddWithValue("@Role", (int)step.Role);
            cmd.Parameters.AddWithValue("@UserId", step.UserId);
            cmd.Parameters.AddWithValue("@UserName", step.UserName);
            cmd.Parameters.AddWithValue("@Status", (int)step.Status);
            cmd.Parameters.AddWithValue("@Note", (object?)step.Note ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DecidedAt", (object?)step.DecidedAt ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static Proposal MapProposal(SqlDataReader reader)
        {
            var tagsRaw = reader.IsDBNull(6) ? string.Empty : reader.GetString(6);
            return new Proposal
            {
                Id              = reader.GetGuid(0),
                Name            = reader.GetString(1),
                ProjectName     = reader.GetString(2),
                Status          = (ProposalStatus)Convert.ToInt32(reader[3]),
                SessionId       = reader.GetGuid(4),
                CreatedByUserId = reader.GetString(5),
                Tags            = string.IsNullOrEmpty(tagsRaw)
                                      ? []
                                      : [.. tagsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries)],
                CreatedAt       = reader.GetDateTime(7),
                UpdatedAt       = reader.GetDateTime(8)
            };
        }
    }
}
