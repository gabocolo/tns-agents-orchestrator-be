using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace Infrastructure.Persistence
{
    public class ProjectRepository : IProjectRepository
    {
        private readonly string _pgConn;
        private readonly ILogger<ProjectRepository> _logger;

        public ProjectRepository(string pgConn, ILogger<ProjectRepository> logger)
        {
            _pgConn = pgConn;
            _logger = logger;
        }

        public async Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(1) FROM projects WHERE name = @name";
            cmd.Parameters.AddWithValue("name", name);
            var result = await cmd.ExecuteScalarAsync(ct);
            return Convert.ToInt64(result) > 0;
        }

        public async Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, name, description, git_repo_url, owner_id, created_at, updated_at
                FROM projects
                WHERE id = @id";
            cmd.Parameters.AddWithValue("id", id);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return MapProject(reader);
        }

        public async Task<Project> CreateAsync(Project project, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO projects (id, name, description, git_repo_url, owner_id, created_at, updated_at)
                VALUES (@id, @name, @description, @gitRepoUrl, @ownerId, @createdAt, @updatedAt)
                ON CONFLICT (name) DO NOTHING
                RETURNING id";

            cmd.Parameters.AddWithValue("id", project.Id);
            cmd.Parameters.AddWithValue("name", project.Name);
            cmd.Parameters.AddWithValue("description", (object?)project.Description ?? DBNull.Value);
            cmd.Parameters.AddWithValue("gitRepoUrl", (object?)project.GitRepoUrl ?? DBNull.Value);
            cmd.Parameters.AddWithValue("ownerId", project.OwnerId);
            cmd.Parameters.AddWithValue("createdAt", project.CreatedAt);
            cmd.Parameters.AddWithValue("updatedAt", project.UpdatedAt);

            await cmd.ExecuteScalarAsync(ct);

            _logger.LogInformation(
                "[ProjectRepository] Proyecto persistido. ProjectId={ProjectId} Name={Name}",
                project.Id, project.Name);

            return project;
        }

        public async Task<List<Adr>> GetAdrsByProjectIdAsync(Guid projectId, CancellationToken ct = default)
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

        public async Task<List<QualityGateCheck>> GetGatesByProjectIdAsync(Guid projectId, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, project_id, gate_number, name, validations::text, blocking, created_at
                FROM quality_gate_checks
                WHERE project_id = @projectId
                ORDER BY gate_number";
            cmd.Parameters.AddWithValue("projectId", projectId);

            var result = new List<QualityGateCheck>();
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var validationsJson = reader.GetString(reader.GetOrdinal("validations"));
                result.Add(new QualityGateCheck
                {
                    Id = reader.GetGuid(reader.GetOrdinal("id")),
                    ProjectId = reader.GetGuid(reader.GetOrdinal("project_id")),
                    GateNumber = reader.GetInt32(reader.GetOrdinal("gate_number")),
                    Name = reader.GetString(reader.GetOrdinal("name")),
                    Validations = JsonSerializer.Deserialize<List<string>>(validationsJson) ?? new List<string>(),
                    Blocking = reader.GetBoolean(reader.GetOrdinal("blocking")),
                    CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at"))
                });
            }

            return result;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static Project MapProject(NpgsqlDataReader reader)
        {
            return new Project
            {
                Id = reader.GetGuid(reader.GetOrdinal("id")),
                Name = reader.GetString(reader.GetOrdinal("name")),
                Description = reader.IsDBNull(reader.GetOrdinal("description"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("description")),
                GitRepoUrl = reader.IsDBNull(reader.GetOrdinal("git_repo_url"))
                    ? null
                    : reader.GetString(reader.GetOrdinal("git_repo_url")),
                OwnerId = reader.GetGuid(reader.GetOrdinal("owner_id")),
                CreatedAt = reader.GetDateTime(reader.GetOrdinal("created_at")),
                UpdatedAt = reader.GetDateTime(reader.GetOrdinal("updated_at"))
            };
        }

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
