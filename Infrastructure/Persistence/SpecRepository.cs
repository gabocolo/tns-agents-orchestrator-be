using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Infrastructure.Persistence
{
    public class SpecRepository : ISpecRepository
    {
        private readonly string _pgConn;
        private readonly ILogger<SpecRepository> _logger;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public SpecRepository(string pgConn, ILogger<SpecRepository> logger)
        {
            _pgConn = pgConn;
            _logger = logger;
        }

        public async Task<Specification?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, project_id, parent_spec_id, level, title, version, status,
                       content, data_classification, approved_by, approved_at,
                       model, tokens_used, created_by, created_at, updated_at
                FROM specifications
                WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return MapSpec(reader);
        }

        public async Task<Specification> CreateAsync(Specification spec, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO specifications
                    (id, project_id, parent_spec_id, level, title, version, status,
                     content, data_classification, model, tokens_used,
                     created_by, created_at, updated_at)
                VALUES
                    (@id, @projectId, @parentSpecId, @level, @title, @version, @status,
                     @content::jsonb, @dataClassification::jsonb, @model, @tokensUsed,
                     @createdBy, @createdAt, @updatedAt)
                RETURNING id, created_at";

            AddSpecParameters(cmd, spec);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                spec.CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"));

            _logger.LogInformation(
                "[SpecRepository] Spec creada. SpecId={SpecId} Level={Level} ProjectId={ProjectId}",
                spec.Id, spec.Level, spec.ProjectId);

            return spec;
        }

        public async Task<Specification> UpdateAsync(Specification spec, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                UPDATE specifications
                SET title = @title,
                    content = @content::jsonb,
                    data_classification = @dataClassification::jsonb,
                    status = @status,
                    model = @model,
                    tokens_used = @tokensUsed,
                    approved_by = @approvedBy,
                    approved_at = @approvedAt,
                    updated_at = @updatedAt
                WHERE id = @id";

            cmd.Parameters.AddWithValue("id", spec.Id);
            cmd.Parameters.AddWithValue("title", spec.Title);
            cmd.Parameters.AddWithValue("content", JsonSerializer.Serialize(spec.Content, JsonOptions));
            cmd.Parameters.AddWithValue("dataClassification",
                spec.DataClassification != null
                    ? JsonSerializer.Serialize(spec.DataClassification, JsonOptions)
                    : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("status", spec.Status.ToString());
            cmd.Parameters.AddWithValue("model", (object?)spec.Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("tokensUsed", (object?)spec.TokensUsed ?? DBNull.Value);
            cmd.Parameters.AddWithValue("approvedBy", (object?)spec.ApprovedBy ?? DBNull.Value);
            cmd.Parameters.AddWithValue("approvedAt", (object?)spec.ApprovedAt ?? DBNull.Value);
            cmd.Parameters.AddWithValue("updatedAt", spec.UpdatedAt);

            await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation(
                "[SpecRepository] Spec actualizada. SpecId={SpecId}", spec.Id);

            return spec;
        }

        public async Task<List<Specification>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, project_id, parent_spec_id, level, title, version, status,
                       content, data_classification, approved_by, approved_at,
                       model, tokens_used, created_by, created_at, updated_at
                FROM specifications
                WHERE project_id = @projectId
                ORDER BY level, created_at";
            cmd.Parameters.AddWithValue("projectId", projectId);

            var result = new List<Specification>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(MapSpec(reader));

            return result;
        }

        public async Task<bool> ExistsDuplicateAsync(
            Guid projectId, string title, SpecLevel level, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COUNT(1) FROM specifications
                WHERE project_id = @projectId
                  AND title = @title
                  AND level = @level
                  AND status IN ('DRAFT', 'IN_REVIEW')";
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("title", title);
            cmd.Parameters.AddWithValue("level", level.ToString());

            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result) > 0;
        }

        public async Task<string?> GetLatestVersionAsync(
            Guid projectId, string title, SpecLevel level, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT version FROM specifications
                WHERE project_id = @projectId
                  AND title = @title
                  AND level = @level
                ORDER BY created_at DESC
                LIMIT 1";
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("title", title);
            cmd.Parameters.AddWithValue("level", level.ToString());

            var result = await cmd.ExecuteScalarAsync(ct);
            return result as string;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void AddSpecParameters(NpgsqlCommand cmd, Specification spec)
        {
            cmd.Parameters.AddWithValue("id", spec.Id);
            cmd.Parameters.AddWithValue("projectId", spec.ProjectId);
            cmd.Parameters.AddWithValue("parentSpecId", (object?)spec.ParentSpecId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("level", spec.Level.ToString());
            cmd.Parameters.AddWithValue("title", spec.Title);
            cmd.Parameters.AddWithValue("version", spec.Version);
            cmd.Parameters.AddWithValue("status", spec.Status.ToString());
            cmd.Parameters.AddWithValue("content", JsonSerializer.Serialize(spec.Content, JsonOptions));
            cmd.Parameters.AddWithValue("dataClassification",
                spec.DataClassification != null
                    ? JsonSerializer.Serialize(spec.DataClassification, JsonOptions)
                    : (object)DBNull.Value);
            cmd.Parameters.AddWithValue("model", (object?)spec.Model ?? DBNull.Value);
            cmd.Parameters.AddWithValue("tokensUsed", (object?)spec.TokensUsed ?? DBNull.Value);
            cmd.Parameters.AddWithValue("createdBy", spec.CreatedBy);
            cmd.Parameters.AddWithValue("createdAt", spec.CreatedAt);
            cmd.Parameters.AddWithValue("updatedAt", spec.UpdatedAt);
        }

        private static Specification MapSpec(NpgsqlDataReader reader)
        {
            var contentJson = reader.GetString(reader.GetOrdinal("content"));
            var dataClassOrd = reader.GetOrdinal("data_classification");
            var approvedByOrd = reader.GetOrdinal("approved_by");
            var approvedAtOrd = reader.GetOrdinal("approved_at");
            var modelOrd = reader.GetOrdinal("model");
            var tokensOrd = reader.GetOrdinal("tokens_used");

            return new Specification
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                ParentSpecId = reader.IsDBNull(reader.GetOrdinal("parent_spec_id"))
                    ? null
                    : reader.GetGuid(reader.GetOrdinal("parent_spec_id")),
                Level = Enum.Parse<SpecLevel>(reader.GetString(reader.GetOrdinal("level"))),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Version = reader.GetString(reader.GetOrdinal("version")),
                Status = Enum.Parse<SpecStatus>(reader.GetString(reader.GetOrdinal("status"))),
                Content = JsonSerializer.Deserialize<Dictionary<string, object>>(contentJson, JsonOptions)
                    ?? new Dictionary<string, object>(),
                DataClassification = reader.IsDBNull(dataClassOrd)
                    ? null
                    : JsonSerializer.Deserialize<Dictionary<string, object>>(
                        reader.GetString(dataClassOrd), JsonOptions),
                ApprovedBy = reader.IsDBNull(approvedByOrd) ? null : reader.GetGuid(approvedByOrd),
                ApprovedAt = reader.IsDBNull(approvedAtOrd) ? null : reader.GetDateTime(approvedAtOrd),
                Model = reader.IsDBNull(modelOrd) ? null : reader.GetString(modelOrd),
                TokensUsed = reader.IsDBNull(tokensOrd) ? null : reader.GetInt32(tokensOrd),
                CreatedBy = reader.GetGuid(reader.GetOrdinal("created_by")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            };
        }
    }
}
