using System;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace FarmacopilotAgent.Exporters
{
    /// <summary>
    /// Detecta y configura Change Data Capture en Oracle
    /// </summary>
    public class OracleCdcDetector
    {
        private readonly ILogger _logger;

        public OracleCdcDetector(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Verifica si CDC está habilitado en la tabla
        /// </summary>
        public async Task<bool> IsCdcEnabledAsync(OracleConnection connection, string tableName)
        {
            try
            {
                var query = @"
                    SELECT COUNT(*) 
                    FROM DBA_CAPTURE 
                    WHERE TABLE_NAME = :tableName 
                    AND STATUS = 'ENABLED'";
                
                await using var command = new OracleCommand(query, connection);
                command.Parameters.Add(new OracleParameter("tableName", tableName.ToUpper()));
                
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                
                _logger.Information("CDC en {Table}: {Enabled}", 
                    tableName, count > 0 ? "habilitado" : "deshabilitado");
                
                return count > 0;
            }
            catch (OracleException ex)
            {
                // Si no tiene permisos para DBA_CAPTURE, asumir CDC no disponible
                _logger.Warning("No se pudo verificar CDC (permisos insuficientes): {Error}", 
                    ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Obtiene cambios desde la última sincronización usando LogMiner
        /// </summary>
        public async Task<string> GetChangesSinceScnAsync(
            OracleConnection connection,
            string tableName,
            long lastScn)
        {
            try
            {
                _logger.Information("Obteniendo cambios desde SCN {Scn} para {Table}", 
                    lastScn, tableName);
                
                // Construir query usando LogMiner para detectar cambios
                var query = $@"
                    SELECT ORA_ROWSCN, t.*
                    FROM {tableName} t
                    WHERE ORA_ROWSCN > :lastScn
                    ORDER BY ORA_ROWSCN";
                
                return query;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error construyendo query CDC para {Table}", tableName);
                throw;
            }
        }

        /// <summary>
        /// Obtiene el SCN actual para tracking
        /// </summary>
        public async Task<long> GetCurrentScnAsync(OracleConnection connection)
        {
            try
            {
                var query = "SELECT CURRENT_SCN FROM V$DATABASE";
                
                await using var command = new OracleCommand(query, connection);
                var scn = Convert.ToInt64(await command.ExecuteScalarAsync());
                
                _logger.Debug("SCN actual: {Scn}", scn);
                return scn;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error obteniendo SCN actual");
                return 0;
            }
        }
    }
}
