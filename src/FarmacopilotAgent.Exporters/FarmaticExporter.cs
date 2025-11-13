using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Oracle.ManagedDataAccess.Client;
using FarmacopilotAgent.Core.Interfaces;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Exporters
{
    /// <summary>
    /// Exportador para ERP Farmatic (Oracle Database)
    /// Soporta versiones 11.x y 12.x con detección automática
    /// </summary>
    public class FarmaticExporter : IExporter
    {
        private readonly string _connectionString;
        private readonly string _outputPath;
        private readonly string _farmaciaId;
        private readonly ILogger _logger;
        private string? _erpVersion;
        private FarmaticMapping? _mapping;

        public FarmaticExporter(string connectionString, string outputPath, string farmaciaId, ILogger logger)
        {
            _connectionString = connectionString;
            _outputPath = outputPath;
            _farmaciaId = farmaciaId;
            _logger = logger;
        }

        /// <summary>
        /// Prueba la conexión a la base de datos Oracle
        /// </summary>
        public async Task<bool> TestConnectionAsync(string connectionString)
        {
            try
            {
                _logger.Information("Probando conexión a Oracle Database...");
                
                await using var connection = new OracleConnection(connectionString);
                await connection.OpenAsync();
                
                // Verificar versión Oracle
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT BANNER FROM V$VERSION WHERE ROWNUM = 1";
                var version = await command.ExecuteScalarAsync();
                
                _logger.Information("Conexión exitosa a Oracle: {Version}", version);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al conectar a Oracle Database");
                return false;
            }
        }

        /// <summary>
        /// Detecta información del ERP Farmatic
        /// </summary>
        public async Task<ErpInfo> DetectErpInfoAsync()
        {
            try
            {
                _logger.Information("Detectando información de Farmatic...");

                await using var connection = new OracleConnection(_connectionString);
                await connection.OpenAsync();

                // Intentar detectar versión desde tabla de configuración
                string? version = null;
                
                try
                {
                    // Farmatic suele tener tabla CONFIGURACION o PARAMETROS
                    var query = @"
                        SELECT VALOR 
                        FROM CONFIGURACION 
                        WHERE PARAMETRO = 'VERSION_SISTEMA'
                        AND ROWNUM = 1";
                    
                    await using var command = new OracleCommand(query, connection);
                    version = (await command.ExecuteScalarAsync())?.ToString();
                }
                catch
                {
                    _logger.Warning("No se pudo obtener versión desde tabla CONFIGURACION");
                }

                // Si no se encuentra, intentar desde tabla alternativa
                if (string.IsNullOrEmpty(version))
                {
                    try
                    {
                        var query = @"
                            SELECT VALOR 
                            FROM PARAMETROS 
                            WHERE NOMBRE = 'VERSION'
                            AND ROWNUM = 1";
                        
                        await using var command = new OracleCommand(query, connection);
                        version = (await command.ExecuteScalarAsync())?.ToString();
                    }
                    catch
                    {
                        _logger.Warning("No se pudo obtener versión desde tabla PARAMETROS");
                    }
                }

                // Si no se puede determinar, asumir versión más común
                if (string.IsNullOrEmpty(version))
                {
                    _logger.Warning("No se pudo determinar versión exacta de Farmatic, asumiendo 12.0");
                    version = "12.0";
                }

                _erpVersion = version;

                return new ErpInfo
                {
                    ErpType = "Farmatic",
                    Version = version,
                    DatabaseType = "Oracle",
                    DetectionMethod = "database_query"
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al detectar información de Farmatic");
                throw;
            }
        }

        /// <summary>
        /// Exporta datos desde la última exportación (incremental) o completo
        /// </summary>
        public async Task<ExportResult> ExportDataAsync(DateTime? lastExportTimestamp = null)
        {
            try
            {
                // Detectar versión si no se ha hecho
                if (string.IsNullOrEmpty(_erpVersion))
                {
                    var erpInfo = await DetectErpInfoAsync();
                    _erpVersion = erpInfo.Version;
                }

                // Cargar mapping correspondiente
                _mapping = LoadMapping(_erpVersion);

                var exportType = lastExportTimestamp.HasValue ? "incremental" : "completa";
                _logger.Information("Iniciando exportación {Type} de Farmatic v{Version}", 
                    exportType, _erpVersion);

                if (lastExportTimestamp.HasValue)
                {
                    _logger.Information("Última exportación: {LastExport}", lastExportTimestamp.Value);
                }

                // Crear directorio de salida si no existe
                Directory.CreateDirectory(_outputPath);

                // Generar timestamp para el archivo
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var fileName = $"ventas_{_farmaciaId}_{timestamp}.csv";
                var filePath = Path.Combine(_outputPath, fileName);

                // Exportar ventas
                var rowsExported = await ExportVentasAsync(filePath, lastExportTimestamp);

                // Calcular SHA256
                var sha256Hash = CalculateSha256(filePath);

                // Guardar archivo SHA256
                var sha256FilePath = filePath + ".sha256";
                await File.WriteAllTextAsync(sha256FilePath, sha256Hash);

                _logger.Information("Exportación completada: {Rows} registros en {File}", 
                    rowsExported, fileName);

                return new ExportResult
                {
                    Success = true,
                    Message = $"Exportación {exportType} completada exitosamente",
                    RowsExported = rowsExported,
                    FilePath = filePath,
                    Sha256Hash = sha256Hash,
                    ExportTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error durante la exportación de Farmatic");
                
                return new ExportResult
                {
                    Success = false,
                    Message = "Error durante la exportación",
                    ErrorDetails = ex.Message,
                    ExportTimestamp = DateTime.UtcNow
                };
            }
        }

        private async Task<int> ExportVentasAsync(string filePath, DateTime? lastExportTimestamp)
        {
            await using var connection = new OracleConnection(_connectionString);
            await connection.OpenAsync();

            // Construir query incremental
            var query = BuildVentasQuery(lastExportTimestamp);

            await using var command = new OracleCommand(query, connection);
            command.CommandTimeout = 300; // 5 minutos

            if (lastExportTimestamp.HasValue)
            {
                command.Parameters.Add("lastExport", OracleDbType.Date, lastExportTimestamp.Value, ParameterDirection.Input);
            }

            await using var reader = await command.ExecuteReaderAsync();
            
            // Escribir CSV
            var rowCount = 0;
            await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

            // Escribir cabecera
            var headers = string.Join(";", _mapping!.Tables["ventas"].Columns.Select(c => c.Target));
            await writer.WriteLineAsync(headers);

            // Escribir datos
            while (await reader.ReadAsync())
            {
                var values = new string[_mapping.Tables["ventas"].Columns.Count];
                
                for (int i = 0; i < _mapping.Tables["ventas"].Columns.Count; i++)
                {
                    var column = _mapping.Tables["ventas"].Columns[i];
                    var value = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString() ?? "";
                    
                    // Escape para CSV
                    if (value.Contains(";") || value.Contains("\"") || value.Contains("\n"))
                    {
                        value = $"\"{value.Replace("\"", "\"\"")}\"";
                    }
                    
                    values[i] = value;
                }

                var line = string.Join(";", values);
                await writer.WriteLineAsync(line);
                rowCount++;
            }

            return rowCount;
        }

        private string BuildVentasQuery(DateTime? lastExportTimestamp)
        {
            var tableConfig = _mapping!.Tables["ventas"];
            var columns = string.Join(", ", tableConfig.Columns.Select(c => $"v.{c.Source}"));

            string query;
            
            if (_erpVersion!.StartsWith("11."))
            {
                // Query para Farmatic 11.x
                query = $@"
                    SELECT {columns}
                    FROM VENTAS_DETALLE v
                    INNER JOIN ARTICULOS a ON v.COD_ARTICULO = a.CODIGO
                    WHERE 1=1";
            }
            else
            {
                // Query para Farmatic 12.x (nomenclatura puede variar)
                query = $@"
                    SELECT {columns}
                    FROM VEN_DETALLE v
                    INNER JOIN ART_MAESTRO a ON v.ARTICULO_ID = a.ID
                    WHERE 1=1";
            }

            if (lastExportTimestamp.HasValue)
            {
                query += $" AND v.{tableConfig.IncrementalColumn} > :lastExport";
            }

            query += $" ORDER BY v.{tableConfig.IncrementalColumn} ASC";

            return query;
        }

        private FarmaticMapping LoadMapping(string version)
        {
            var mappingFileName = version.StartsWith("11.") ? "farmatic_v11.json" : "farmatic_v12.json";
            var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mappings", mappingFileName);

            if (!File.Exists(mappingPath))
            {
                _logger.Error("Archivo de mapping no encontrado: {Path}", mappingPath);
                throw new FileNotFoundException($"Mapping file not found: {mappingPath}");
            }

            var json = File.ReadAllText(mappingPath);
            var mapping = JsonSerializer.Deserialize<FarmaticMapping>(json);

            if (mapping == null)
            {
                throw new InvalidOperationException("Failed to deserialize mapping file");
            }

            _logger.Information("Mapping cargado: {File}", mappingFileName);
            return mapping;
        }

        private string CalculateSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        // Clases auxiliares para deserialización de mapping
        private class FarmaticMapping
        {
            public string Version { get; set; } = string.Empty;
            public string Erp { get; set; } = string.Empty;
            public Dictionary<string, TableMapping> Tables { get; set; } = new();
        }

        private class TableMapping
        {
            public string SourceTable { get; set; } = string.Empty;
            public List<ColumnMapping> Columns { get; set; } = new();
            public string IncrementalColumn { get; set; } = string.Empty;
            public string QueryTemplate { get; set; } = string.Empty;
        }

        private class ColumnMapping
        {
            public string Source { get; set; } = string.Empty;
            public string Target { get; set; } = string.Empty;
            public string Type { get; set; } = string.Empty;
        }
    }
}
