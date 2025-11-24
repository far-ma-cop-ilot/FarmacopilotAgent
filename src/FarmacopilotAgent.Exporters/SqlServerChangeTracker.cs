using System;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Serilog;

namespace FarmacopilotAgent.Exporters
{
    /// <summary>
    /// Gestiona Change Tracking en SQL Server
    /// </summary>
    public class SqlServerChangeTracker
    {
        private readonly ILogger _logger;

        public SqlServerChangeTracker(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Verifica si Change Tracking está habilitado en la base de datos
        /// </summary>
        public async Task<bool> IsChangeTrackingEnabledAsync(SqlConnection connection)
        {
            try
            {
                var query = @"
                    SELECT is_change_tracking_on 
                    FROM sys.databases 
                    WHERE name = DB_NAME()";
                
                await using var command = new SqlCommand(query, connection);
                var result = await command.ExecuteScalarAsync();
                
                var enabled = result != null && Convert.ToBoolean(result);
                
                _logger.Information("Change Tracking en base de datos: {Enabled}", 
                    enabled ? "habilitado" : "deshabilitado");
                
                return enabled;
            }
            catch (Exception ex)
            {
                _logger.Warning("Error verificando Change Tracking: {Error}", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Verifica si Change Tracking está habilitado en una tabla específica
        /// </summary>
        public async Task<bool> IsTableTrackedAsync(SqlConnection connection, string tableName)
        {
            try
            {
                var query = @"
                    SELECT COUNT(*) 
                    FROM sys.change_tracking_tables ctt
                    INNER JOIN sys.tables t ON ctt.object_id = t.object_id
                    WHERE t.name = @tableName";
                
                await using var command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@tableName", tableName);
                
                var count = Convert.ToInt32(await command.ExecuteScalarAsync());
                
                _logger.Information("Change Tracking en {Table}: {Enabled}", 
                    tableName, count > 0 ? "habilitado" : "deshabilitado");
                
                return count > 0;
            }
            catch (Exception ex)
            {
                _logger.Warning("Error verificando tracking en tabla {Table}: {Error}", 
                    tableName, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Obtiene la versión de Change Tracking actual
        /// </summary>
        public async Task<long> GetCurrentVersionAsync(SqlConnection connection)
        {
            try
            {
                var query = "SELECT CHANGE_TRACKING_CURRENT_VERSION()";
                
                await using var command = new SqlCommand(query, connection);
                var version = await command.ExecuteScalarAsync();
                
                if (version == null || version == DBNull.Value)
                {
                    _logger.Warning("Change Tracking no disponible");
                    return 0;
                }
                
                var currentVersion = Convert.ToInt64(version);
                _logger.Debug("Versión Change Tracking actual: {Version}", currentVersion);
                
                return currentVersion;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error obteniendo versión Change Tracking");
                return 0;
            }
        }

        /// <summary>
        /// Construye query para obtener cambios desde última versión
        /// </summary>
        public string BuildChangeTrackingQuery(string tableName, long lastVersion)
        {
            // Escapar nombre de tabla (puede tener espacios en Farmatic)
            var escapedTable = $"[{tableName}]";
            
            return $@"
                SELECT 
                    ct.SYS_CHANGE_VERSION,
                    ct.SYS_CHANGE_OPERATION,
                    t.*
                FROM CHANGETABLE(CHANGES {escapedTable}, {lastVersion}) AS ct
                LEFT JOIN {escapedTable} AS t ON t.id = ct.id
                WHERE ct.SYS_CHANGE_VERSION > {lastVersion}
                ORDER BY ct.SYS_CHANGE_VERSION";
        }
    }
}
