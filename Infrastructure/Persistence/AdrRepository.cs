using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Persistence
{
    public class AdrRepository : IAdrRepository
    {
        private readonly string _pgConn;
        private readonly ILogger<AdrRepository> _logger;

        public AdrRepository(string pgConn, ILogger<AdrRepository> logger)
        {
            _pgConn = pgConn;
            _logger = logger;
        }

        public async Task<bool> ExistsByNumberAsync(Guid projectId, int number, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM adrs WHERE project_id = @projectId AND number = @number";
            cmd.Parameters.AddWithValue("projectId", projectId);
            cmd.Parameters.AddWithValue("number", number);
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result) > 0;
        }

        public async Task<Adr?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, project_id, number, title, status, content, created_by, created_at, updated_at
                FROM adrs
                WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return MapAdr(reader);
        }

        public async Task<Adr> CreateAsync(Adr adr, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO adrs (id, project_id, number, title, status, content, created_by, created_at, updated_at)
                VALUES (@id, @projectId, @number, @title, @status, @content, @createdBy, @createdAt, @updatedAt)
                RETURNING id, created_at";

            cmd.Parameters.AddWithValue("id", adr.Id);
            cmd.Parameters.AddWithValue("projectId", adr.ProjectId);
            cmd.Parameters.AddWithValue("number", adr.Number);
            cmd.Parameters.AddWithValue("title", adr.Title);
            cmd.Parameters.AddWithValue("status", adr.Status.ToString());
            cmd.Parameters.AddWithValue("content", adr.Content);
            cmd.Parameters.AddWithValue("createdBy", adr.CreatedBy);
            cmd.Parameters.AddWithValue("createdAt", adr.CreatedAt);
            cmd.Parameters.AddWithValue("updatedAt", adr.UpdatedAt);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct))
                adr.CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"));

            _logger.LogInformation(
                "[AdrRepository] ADR persistido. AdrId={AdrId} ProjectId={ProjectId} Number={Number}",
                adr.Id, adr.ProjectId, adr.Number);

            return adr;
        }

        public async Task<List<Adr>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, project_id, number, title, status, content, created_by, created_at, updated_at
                FROM adrs
                WHERE project_id = @projectId
                ORDER BY number";
            cmd.Parameters.AddWithValue("projectId", projectId);

            var result = new List<Adr>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result.Add(MapAdr(reader));

            return result;
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private static Adr MapAdr(NpgsqlDataReader reader)
        {
            return new Adr
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                Number = reader.GetInt32(reader.GetOrdinal("number")),
                Title = reader.GetString(reader.GetOrdinal("title")),
                Status = Enum.Parse<AdrStatus>(reader.GetString(reader.GetOrdinal("status"))),
                Content = reader.GetString(reader.GetOrdinal("content")),
                CreatedBy = reader.GetGuid(reader.GetOrdinal("created_by")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            };
        }
    }
}
