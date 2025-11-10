using System;
using System.Threading.Tasks;
using Npgsql;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Core.Database
{
    public class PostgresStatusChecker
    {
        private readonly string _connectionString;
        private readonly ILogger _logger;

        public PostgresStatusChecker(string connectionString, ILogger logger)
        {
            _connectionString = connectionString;
            _logger = logger;
        }

        /// <summary>
        /// Consulta directa a PostgreSQL para verificar estado del cliente
        /// </summary>
        public async Task<ClientStatus?> GetClientStatusAsync(string farmaciaId)
        {
            try
            {
                _logger.Information("Consultando estado del cliente {FarmaciaId} en PostgreSQL", farmaciaId);

                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    SELECT 
                        farmacia_id, 
                        active, 
                        inactive_reason,
                        last_check_ts
                    FROM clients 
                    WHERE farmacia_id = @FarmaciaId
                    LIMIT 1";

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("@FarmaciaId", farmaciaId);

                await using var reader = await command.ExecuteReaderAsync();

                if (await reader.ReadAsync())
                {
                    var status = new ClientStatus
                    {
                        FarmaciaId = reader.GetString(0),
                        Active = reader.GetBoolean(1),
                        Reason = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                        LastCheck = reader.IsDBNull(3) ? DateTime.UtcNow : reader.GetDateTime(3)
                    };

                    _logger.Information("Cliente encontrado. Estado: {Active}", status.Active);
                    return status;
                }
                else
                {
                    _logger.Warning("Cliente {FarmaciaId} no encontrado en base de datos", farmaciaId);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al consultar PostgreSQL");
                return null;
            }
        }

        /// <summary>
        /// Actualiza timestamp de última actividad del agente
        /// </summary>
        public async Task UpdateLastActivityAsync(string farmaciaId)
        {
            try
            {
                await using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync();

                var query = @"
                    UPDATE clients 
                    SET 
                        last_agent_activity = @Timestamp,
                        agent_version = @Version
                    WHERE farmacia_id = @FarmaciaId";

                await using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("@FarmaciaId", farmaciaId);
                command.Parameters.AddWithValue("@Timestamp", DateTime.UtcNow);
                command.Parameters.AddWithValue("@Version", "1.0.0");

                await command.ExecuteNonQueryAsync();

                _logger.Information("Última actividad actualizada para {FarmaciaId}", farmaciaId);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudo actualizar última actividad (no crítico)");
            }
        }
    }
}
