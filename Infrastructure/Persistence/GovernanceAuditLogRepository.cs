using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using System.Text.Json;

namespace Infrastructure.Persistence
{
    public class GovernanceAuditLogRepository : IAuditLogRepository
    {
        private readonly string _pgConn;
        private readonly ILogger<GovernanceAuditLogRepository> _logger;

        public GovernanceAuditLogRepository(string pgConn, ILogger<GovernanceAuditLogRepository> logger)
        {
            _pgConn = pgConn;
            _logger = logger;
        }

        public async Task CreateAsync(AuditLog log, CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO audit_logs
                    (id, project_id, entity_type, entity_id, action, actor_id, details, correlation_id, created_at)
                VALUES
                    (@id, @projectId, @entityType, @entityId, @action, @actorId, @details, @correlationId, @createdAt)";

            cmd.Parameters.AddWithValue("id", log.Id);
            cmd.Parameters.AddWithValue("projectId", (object?)log.ProjectId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("entityType", log.EntityType);
            cmd.Parameters.AddWithValue("entityId", log.EntityId);
            cmd.Parameters.AddWithValue("action", log.Action);
            cmd.Parameters.AddWithValue("actorId", log.ActorId);
            cmd.Parameters.AddWithValue("correlationId", log.CorrelationId);
            cmd.Parameters.AddWithValue("createdAt", log.CreatedAt);

            if (log.Details != null)
            {
                var detailsParam = cmd.Parameters.Add("details", NpgsqlDbType.Jsonb);
                detailsParam.Value = JsonSerializer.Serialize(log.Details);
            }
            else
            {
                cmd.Parameters.AddWithValue("details", DBNull.Value);
            }

            await cmd.ExecuteNonQueryAsync(ct);

            _logger.LogInformation(
                "[AuditLog] {Action} EntityId={EntityId} CorrelationId={CorrelationId}",
                log.Action, log.EntityId, log.CorrelationId);
        }
    }
}
