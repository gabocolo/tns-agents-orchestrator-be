using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Persistence
{
    public class SpecGenerationJobRepository : ISpecGenerationJobRepository
    {
        private readonly string _pgConn;
        private readonly ILogger<SpecGenerationJobRepository> _logger;

        public SpecGenerationJobRepository(string pgConn, ILogger<SpecGenerationJobRepository> logger)
        {
            _pgConn = pgConn;
            _logger = logger;
        }

        public async Task<SpecGenerationJob?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, project_id, level, parent_spec_id, sanitized_input,
                       additional_context, status, result_spec_id, error,
                       retry_count, created_by, correlation_id, created_at, completed_at
                FROM spec_generation_jobs
                WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return MapJob(reader);
        }

        public async Task<SpecGenerationJob> CreateAsync(SpecGenerationJob job, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO spec_generation_jobs
                    (id, project_id, level, parent_spec_id, sanitized_input,
                     additional_context, status, retry_count, created_by,
                     correlation_id, created_at)
                VALUES
                    (@id, @projectId, @level, @parentSpecId, @sanitizedInput,
                     @additionalContext, @status, @retryCount, @createdBy,
                     @correlationId, @createdAt)";

            cmd.Parameters.AddWithValue("id", job.Id);
            cmd.Parameters.AddWithValue("projectId", job.ProjectId);
            cmd.Parameters.AddWithValue("level", job.Level.ToString());
            cmd.Parameters.AddWithValue("parentSpecId", (object?)job.ParentSpecId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("sanitizedInput", job.SanitizedInput);
            cmd.Parameters.AddWithValue("additionalContext", (object?)job.AdditionalContext ?? DBNull.Value);
            cmd.Parameters.AddWithValue("status", job.Status.ToString());
            cmd.Parameters.AddWithValue("retryCount", job.RetryCount);
            cmd.Parameters.AddWithValue("createdBy", job.CreatedBy);
            cmd.Parameters.AddWithValue("correlationId", job.CorrelationId);
            cmd.Parameters.AddWithValue("createdAt", job.CreatedAt);

            await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation(
                "[SpecGenerationJobRepository] Job creado. JobId={JobId} Level={Level}",
                job.Id, job.Level);

            return job;
        }

        public async Task UpdateAsync(SpecGenerationJob job, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE spec_generation_jobs
                SET status = @status,
                    result_spec_id = @resultSpecId,
                    error = @error,
                    retry_count = @retryCount,
                    completed_at = @completedAt
                WHERE id = @id";

            cmd.Parameters.AddWithValue("id", job.Id);
            cmd.Parameters.AddWithValue("status", job.Status.ToString());
            cmd.Parameters.AddWithValue("resultSpecId", (object?)job.ResultSpecId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("error", (object?)job.Error ?? DBNull.Value);
            cmd.Parameters.AddWithValue("retryCount", job.RetryCount);
            cmd.Parameters.AddWithValue("completedAt", (object?)job.CompletedAt ?? DBNull.Value);

            await cmd.ExecuteNonQueryAsync(ct);
        }

        private static SpecGenerationJob MapJob(NpgsqlDataReader reader)
        {
            var parentOrd = reader.GetOrdinal("parent_spec_id");
            var contextOrd = reader.GetOrdinal("additional_context");
            var resultOrd = reader.GetOrdinal("result_spec_id");
            var errorOrd = reader.GetOrdinal("error");
            var completedOrd = reader.GetOrdinal("completed_at");

            return new SpecGenerationJob
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                Level = Enum.Parse<SpecLevel>(reader.GetString(reader.GetOrdinal("level"))),
                ParentSpecId = reader.IsDBNull(parentOrd) ? null : reader.GetGuid(parentOrd),
                SanitizedInput = reader.GetString(reader.GetOrdinal("sanitized_input")),
                AdditionalContext = reader.IsDBNull(contextOrd) ? null : reader.GetString(contextOrd),
                Status = Enum.Parse<SpecJobStatus>(reader.GetString(reader.GetOrdinal("status"))),
                ResultSpecId = reader.IsDBNull(resultOrd) ? null : reader.GetGuid(resultOrd),
                Error = reader.IsDBNull(errorOrd) ? null : reader.GetString(errorOrd),
                RetryCount = reader.GetInt32(reader.GetOrdinal("retry_count")),
                CreatedBy = reader.GetGuid(reader.GetOrdinal("created_by")),
                CorrelationId = reader.GetGuid(reader.GetOrdinal("correlation_id")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                CompletedAt = reader.IsDBNull(completedOrd) ? null : reader.GetDateTime(completedOrd)
            };
        }
    }
}
