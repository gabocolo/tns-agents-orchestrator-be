using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Text.Json;

namespace Infrastructure.Persistence
{
    public class QualityGateRepository : IQualityGateRepository
    {
        private readonly string _pgConn;
        private readonly ILogger<QualityGateRepository> _logger;

        public QualityGateRepository(string pgConn, ILogger<QualityGateRepository> logger)
        {
            _pgConn = pgConn;
            _logger = logger;
        }

        public async Task UpsertProjectGatesAsync(
            Guid projectId,
            List<QualityGateCheck> gates,
            CancellationToken ct = default)
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);

            try
            {
                // Eliminar gates existentes del proyecto
                var delCmd = conn.CreateCommand();
                delCmd.Transaction = tx;
                delCmd.CommandText = "DELETE FROM quality_gate_checks WHERE project_id = @projectId";
                delCmd.Parameters.AddWithValue("projectId", projectId);
                await delCmd.ExecuteNonQueryAsync(ct);

                // Insertar los nuevos gates
                foreach (var gate in gates)
                {
                    var insCmd = conn.CreateCommand();
                    insCmd.Transaction = tx;
                    insCmd.CommandText = @"
                        INSERT INTO quality_gate_checks
                            (id, project_id, gate_number, name, validations, blocking, created_at)
                        VALUES
                            (@id, @projectId, @gateNumber, @name, @validations::jsonb, @blocking, @createdAt)";

                    insCmd.Parameters.AddWithValue("id", gate.Id);
                    insCmd.Parameters.AddWithValue("projectId", projectId);
                    insCmd.Parameters.AddWithValue("gateNumber", gate.GateNumber);
                    insCmd.Parameters.AddWithValue("name", gate.Name);
                    insCmd.Parameters.AddWithValue("validations", JsonSerializer.Serialize(gate.Validations));
                    insCmd.Parameters.AddWithValue("blocking", gate.Blocking);
                    insCmd.Parameters.AddWithValue("createdAt", gate.CreatedAt);

                    await insCmd.ExecuteNonQueryAsync(ct);
                }

                await tx.CommitAsync(ct);

                _logger.LogInformation(
                    "[QualityGateRepository] Gates configurados. ProjectId={ProjectId} Count={Count}",
                    projectId, gates.Count);
            }
            catch
            {
                await tx.RollbackAsync(ct);
                throw;
            }
        }

        public async Task<List<QualityGateCheck>> ListByProjectAsync(Guid projectId, CancellationToken ct = default)
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
    }
}
