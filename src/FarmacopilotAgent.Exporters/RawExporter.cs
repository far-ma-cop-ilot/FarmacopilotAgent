using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FarmacopilotAgent.Core.Interfaces;
using FarmacopilotAgent.Core.Models;
using FarmacopilotAgent.Core.Utils;
using FarmacopilotAgent.Detection;
using Serilog;

namespace FarmacopilotAgent.Exporters
{
    /// <summary>
    /// Exportador genérico RAW que extrae SELECT * de cualquier tabla
    /// sin transformaciones. Para uso con Fabric lakehouse Bronze.
    /// Soporta Oracle (Nixfarma con esquema APPUL) y SQL Server (Farmatic).
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
            var dbType = _connection.GetType().Name.Contains("Oracle") ? "Oracle" : "SQL Server";
            
            return new ErpInfo
            {
                DatabaseType = dbType,
                DetectionMethod = "connection_type"
            };
        }

        /// <summary>
        /// Exporta datos con toda la pipeline de optimización
        /// </summary>
        public async Task<ExportResult> ExportDataAsync(DateTime? lastExportTimestamp = null)
        {
            var results = new List<ExportResult>();
            var totalRows = 0;
            var failedTables = new List<string>();
            
            var configManager = new ConfigManager(@"C:\FarmacopilotAgent", _logger);
            var config = await configManager.LoadConfigAsync();
            
            if (config == null)
                throw new InvalidOperationException("Config not found");
            
            var tablesToExport = config.TablesToExport
                .Where(t => t.Enabled)
                .OrderBy(t => t.Priority);
            
            foreach (var table in tablesToExport)
            {
                try
                {
                    // Extraer nombre simple para el archivo (sin esquema)
                    var tableNameForFile = GetTableNameWithoutSchema(table.TableName);
                    
                    _logger.Information("Exportando tabla {Table}", table.TableName);
                    
                    var result = await ExportTableRawAsync(
                        table.TableName,
                        table.IncrementalColumn,
                        table.LastExportTimestamp ?? lastExportTimestamp
                    );
                    
                    results.Add(result);
                    totalRows += result.RowsExported;
                    
                    var progress = (results.Count * 100) / tablesToExport.Count();
                    _logger.Information("Progreso: {Progress}% completado", progress);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error exportando tabla {Table}", table.TableName);
                    failedTables.Add(table.TableName);
                }
            }
            
            return new ExportResult
            {
                Success = failedTables.Count == 0,
                Message = $"Exportadas {results.Count} tablas, {failedTables.Count} errores",
                RowsExported = totalRows,
                ExportTimestamp = DateTime.UtcNow,
                TablesFailed = failedTables
            };
        }

        /// <summary>
        /// Extrae el nombre de tabla sin el esquema (APPUL.AH_VENTAS -> AH_VENTAS)
        /// </summary>
        private string GetTableNameWithoutSchema(string fullTableName)
        {
            if (fullTableName.Contains("."))
            {
                return fullTableName.Split('.').Last();
            }
            return fullTableName;
        }

        /// <summary>
        /// Exporta una tabla individual
        /// </summary>
        public async Task<ExportResult> ExportTableRawAsync(
            string tableName,
            string? incrementalColumn,
            DateTime? lastExportTimestamp)
        {
            // Nombre para archivo (sin esquema, sin espacios)
            var tableNameForFile = GetTableNameWithoutSchema(tableName).Replace(" ", "_");
            var fileName = $"{tableNameForFile}_{_farmaciaId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var filePath = Path.Combine(_outputPath, fileName);

            _logger.Information("Exportando {Table} a {File}", tableName, fileName);

            // Detectar columna timestamp si no está especificada
            if (string.IsNullOrEmpty(incrementalColumn))
            {
                var detector = new TimestampColumnDetector(_logger);
                incrementalColumn = await detector.DetectBestTimestampColumnAsync(_connection, tableName);
                
                if (!string.IsNullOrEmpty(incrementalColumn))
                {
                    _logger.Information("Columna incremental detectada automáticamente: {Column}", incrementalColumn);
                }
            }

            // Construir query
            string query = BuildRawQuery(tableName, incrementalColumn, lastExportTimestamp);
            
            _logger.Debug("Query: {Query}", query);

            // Ejecutar exportación
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var rowCount = await ExportToCsvAsync(query, filePath, lastExportTimestamp);
            stopwatch.Stop();

            if (rowCount == 0)
            {
                _logger.Information("No hay datos nuevos para exportar en {Table}", tableName);
                
                if (File.Exists(filePath))
                    File.Delete(filePath);
                
                return new ExportResult
                {
                    Success = true,
                    Message = "No hay datos nuevos",
                    RowsExported = 0,
                    TableName = tableName,
                    ExportTimestamp = DateTime.UtcNow,
                    ExportType = string.IsNullOrEmpty(incrementalColumn) ? "full" : "incremental",
                    DurationMs = stopwatch.ElapsedMilliseconds
                };
            }

            var fileInfo = new FileInfo(filePath);
            var sha256 = CalculateSha256(filePath);
            
            // Generar archivo .sha256
            var sha256FilePath = filePath + ".sha256";
            await File.WriteAllTextAsync(sha256FilePath, sha256);

            _logger.Information("Exportación completada: {Rows:N0} filas, {Size:N0} KB, {Duration:N1}s",
                rowCount, fileInfo.Length / 1024, stopwatch.Elapsed.TotalSeconds);

            return new ExportResult
            {
                Success = true,
                Message = $"Exportados {rowCount:N0} registros",
                RowsExported = rowCount,
                FilePath = filePath,
                Sha256Hash = sha256,
                ExportTimestamp = DateTime.UtcNow,
                TableName = tableName,
                FileSizeBytes = fileInfo.Length,
                DurationMs = stopwatch.ElapsedMilliseconds,
                ExportType = string.IsNullOrEmpty(incrementalColumn) ? "full" : "incremental",
                ColumnCount = 0
            };
        }

        /// <summary>
        /// Exportar con compresión y particionado automático
        /// </summary>
        public async Task<List<ExportResult>> ExportTableRawWithCompressionAsync(
            string tableName,
            string? incrementalColumn,
            DateTime? lastExportTimestamp)
        {
            var baseResult = await ExportTableRawAsync(tableName, incrementalColumn, lastExportTimestamp);
            
            if (!baseResult.Success || baseResult.RowsExported == 0)
            {
                return new List<ExportResult> { baseResult };
            }
            
            var results = new List<ExportResult>();
            var compressor = new FileCompressor(_logger);
            
            // Particionar si es necesario (archivos >100MB)
            var partitions = await compressor.PartitionIfNeededAsync(baseResult.FilePath);
            
            if (partitions.Count > 1)
            {
                _logger.Information("Tabla {Table} dividida en {Count} partición(es)", 
                    tableName, partitions.Count);
            }
            
            // Comprimir cada partición
            foreach (var partitionPath in partitions)
            {
                var compressedPath = await compressor.CompressIfNeededAsync(partitionPath);
                
                var partResult = new ExportResult
                {
                    Success = true,
                    FilePath = compressedPath,
                    TableName = tableName,
                    ExportTimestamp = baseResult.ExportTimestamp,
                    RowsExported = baseResult.RowsExported / partitions.Count,
                    FileSizeBytes = new FileInfo(compressedPath).Length,
                    Sha256Hash = CalculateSha256(compressedPath),
                    ExportType = baseResult.ExportType,
                    DurationMs = baseResult.DurationMs
                };
                
                // Generar .sha256 para archivo comprimido
                var sha256FilePath = compressedPath + ".sha256";
                await File.WriteAllTextAsync(sha256FilePath, partResult.Sha256Hash);
                
                results.Add(partResult);
            }
            
            return results;
        }

        /// <summary>
        /// Construye la query SELECT respetando el formato de cada BD
        /// </summary>
        private string BuildRawQuery(
            string tableName, 
            string? incrementalColumn, 
            DateTime? lastExport)
        {
            bool isOracle = _connection.GetType().Name.Contains("Oracle");
            string escapedTable;
            
            if (isOracle)
            {
                // Oracle: APPUL.AH_VENTAS o "APPUL"."AH_VENTAS"
                // Si ya tiene esquema (contiene .), usarlo directamente
                if (tableName.Contains("."))
                {
                    var parts = tableName.Split('.');
                    escapedTable = $"{parts[0]}.{parts[1]}"; // APPUL.AH_VENTAS
                }
                else
                {
                    escapedTable = tableName;
                }
            }
            else
            {
                // SQL Server: [tabla] - permite espacios
                escapedTable = $"[{tableName}]";
            }

            var query = $"SELECT * FROM {escapedTable}";

            if (!string.IsNullOrEmpty(incrementalColumn) && lastExport.HasValue)
            {
                if (isOracle)
                {
                    // Oracle usa :paramName y TO_DATE
                    query += $" WHERE {incrementalColumn} > :lastExport";
                    query += $" ORDER BY {incrementalColumn} ASC";
                }
                else
                {
                    // SQL Server usa @paramName
                    var escapedColumn = $"[{incrementalColumn}]";
                    query += $" WHERE {escapedColumn} > @lastExport";
                    query += $" ORDER BY {escapedColumn} ASC";
                }
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
                bool isOracle = _connection.GetType().Name.Contains("Oracle");
                
                param.ParameterName = isOracle ? "lastExport" : "@lastExport";
                param.Value = lastExport.Value;
                param.DbType = DbType.DateTime;
                command.Parameters.Add(param);
            }

            await using var reader = await command.ExecuteReaderAsync();
            
            var rowCount = 0;
            await using var writer = new StreamWriter(filePath, false, Encoding.UTF8);

            // Escribir cabecera con nombres de columnas
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
                        typeName.Contains("IMAGE") ||
                        typeName.Contains("RAW") ||
                        typeName.Contains("LONG RAW"))
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
                    if (value.Contains(";") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                    {
                        value = $"\"{value.Replace("\"", "\"\"")}\"";
                    }
                    
                    values[i] = value;
                }

                await writer.WriteLineAsync(string.Join(";", values));
                rowCount++;
                
                // Log progreso cada 100k registros
                if (rowCount % 100000 == 0)
                {
                    _logger.Information("  Procesados {Rows:N0} registros...", rowCount);
                }
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

        // Métodos auxiliares para CDC (simplificados - sin uso por ahora)
        private async Task<long> LoadLastScnAsync(string tableName) => 0;
        private async Task SaveLastScnAsync(string tableName, long scn) { }
        private async Task<long> LoadLastChangeVersionAsync(string tableName) => 0;
        private async Task SaveLastChangeVersionAsync(string tableName, long version) { }
    }
}