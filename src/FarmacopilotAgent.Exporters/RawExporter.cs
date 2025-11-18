using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FarmacopilotAgent.Core.Interfaces;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Exporters
{
    /// <summary>
    /// Exportador genérico RAW que extrae SELECT * de cualquier tabla
    /// sin transformaciones. Para uso con Fabric lakehouse Bronze.
    /// </summary>
    public class RawExporter : IExporter
    {
        private readonly DbConnection _connection;
        private readonly string _outputPath;
        private readonly string _farmaciaId;
        private readonly ILogger _logger;

        public RawExporter(
            DbConnection connection, 
            string outputPath, 
            string farmaciaId, 
            ILogger logger)
        {
            _connection = connection;
            _outputPath = outputPath;
            _farmaciaId = farmaciaId;
            _logger = logger;
        }

        public async Task<bool> TestConnectionAsync(string connectionString)
        {
            try
            {
                _logger.Information("Probando conexión a base de datos...");
                
                if (_connection.State != ConnectionState.Open)
                {
                    await _connection.OpenAsync();
                }
                
                _logger.Information("Conexión exitosa");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al conectar a base de datos");
                return false;
            }
        }

        public async Task<ErpInfo> DetectErpInfoAsync()
        {
            // Implementar detección básica según tipo de conexión
            var dbType = _connection.GetType().Name.Contains("Oracle") ? "Oracle" : "SQL Server";
            
            return new ErpInfo
            {
                DatabaseType = dbType,
                DetectionMethod = "connection_type"
            };
        }

        /// <summary>
        /// Exporta una tabla completa sin transformaciones (SELECT *)
        /// </summary>
        public async Task<ExportResult> ExportTableRawAsync(
            string tableName, 
            string? incrementalColumn = null,
            DateTime? lastExportTimestamp = null)
        {
            try
            {
                var exportType = lastExportTimestamp.HasValue ? "incremental" : "completa";
                _logger.Information("Iniciando exportación {Type} de tabla {Table}", 
                    exportType, tableName);

                // Crear directorio de salida
                Directory.CreateDirectory(_outputPath);

                // Generar nombre de archivo
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{tableName}_raw_{_farmaciaId}_{timestamp}.csv";
                var filePath = Path.Combine(_outputPath, fileName);

                // Construir query
                var query = BuildRawQuery(tableName, incrementalColumn, lastExportTimestamp);

                // Ejecutar export
                var rowsExported = await ExportToCsvAsync(query, filePath, lastExportTimestamp);

                // Calcular SHA256
                var sha256Hash = CalculateSha256(filePath);
                var sha256FilePath = filePath + ".sha256";
                await File.WriteAllTextAsync(sha256FilePath, sha256Hash);

                _logger.Information("Exportación completada: {Rows} registros en {File}", 
                    rowsExported, fileName);

                return new ExportResult
                {
                    Success = true,
                    Message = $"Exportación {exportType} completada",
                    RowsExported = rowsExported,
                    FilePath = filePath,
                    Sha256Hash = sha256Hash,
                    ExportTimestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error durante exportación de tabla {Table}", tableName);
                
                return new ExportResult
                {
                    Success = false,
                    Message = "Error durante exportación",
                    ErrorDetails = ex.Message,
                    ExportTimestamp = DateTime.UtcNow
                };
            }
        }

        private string BuildRawQuery(
            string tableName, 
            string? incrementalColumn, 
            DateTime? lastExport)
        {
            // Escapar nombre de tabla según BD
            var escapedTable = _connection.GetType().Name.Contains("Oracle")
                ? $"\"{tableName}\""  // Oracle: "TABLA"
                : $"[{tableName}]";   // SQL Server: [tabla]

            var query = $"SELECT * FROM {escapedTable}";

            if (!string.IsNullOrEmpty(incrementalColumn) && lastExport.HasValue)
            {
                var paramName = _connection.GetType().Name.Contains("Oracle") 
                    ? ":lastExport"  // Oracle
                    : "@lastExport"; // SQL Server

                var escapedColumn = _connection.GetType().Name.Contains("Oracle")
                    ? $"\"{incrementalColumn}\""
                    : $"[{incrementalColumn}]";

                query += $" WHERE {escapedColumn} > {paramName}";
                query += $" ORDER BY {escapedColumn} ASC";
            }

            return query;
        }

        private async Task<int> ExportToCsvAsync(
            string query, 
            string filePath, 
            DateTime? lastExport)
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = 600; // 10 minutos

            if (lastExport.HasValue)
            {
                var param = command.CreateParameter();
                param.ParameterName = _connection.GetType().Name.Contains("Oracle") 
                    ? "lastExport" 
                    : "@lastExport";
                param.Value = lastExport.Value;
                command.Parameters.Add(param);
            }

            await using var reader = await command.ExecuteReaderAsync();
            
            var rowCount = 0;
            await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

            // Escribir cabecera con nombres de columnas EXACTOS de la BD
            var columnNames = new List<string>();
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columnNames.Add(reader.GetName(i));
            }
            await writer.WriteLineAsync(string.Join(";", columnNames));

            // Escribir datos
            while (await reader.ReadAsync())
            {
                var values = new string[reader.FieldCount];
                
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    // Excluir tipos binarios
                    var typeName = reader.GetDataTypeName(i).ToUpper();
                    if (typeName.Contains("BINARY") || 
                        typeName.Contains("BLOB") || 
                        typeName.Contains("IMAGE"))
                    {
                        values[i] = "[BINARY_EXCLUDED]";
                        continue;
                    }

                    var value = reader.IsDBNull(i) ? "" : reader.GetValue(i).ToString() ?? "";
                    
                    // Truncar valores muy largos (>5000 chars)
                    if (value.Length > 5000)
                    {
                        value = value.Substring(0, 5000) + "...[TRUNCATED]";
                    }
                    
                    // Escape para CSV
                    if (value.Contains(";") || value.Contains("\"") || value.Contains("\n"))
                    {
                        value = $"\"{value.Replace("\"", "\"\"")}\"";
                    }
                    
                    values[i] = value;
                }

                await writer.WriteLineAsync(string.Join(";", values));
                rowCount++;
            }

            return rowCount;
        }

        private string CalculateSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public Task<ExportResult> ExportDataAsync(DateTime? lastExportTimestamp = null)
        {
            throw new NotImplementedException(
                "Use ExportTableRawAsync para exportar tablas específicas");
        }
    }
}
