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
    /// Incluye: detección automática de columnas timestamp, CDC/Change Tracking, compresión y particionado.
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
        /// Exporta datos con toda la pipeline de optimización (timestamp detection, CDC/CT, compresión, particionado)
        /// </summary>
        public async Task<ExportResult> ExportDataAsync(DateTime? lastExportTimestamp = null)
        {
            var results = new List<ExportResult>();
            var totalRows = 0;
            var failedTables = new List<string>();
            
            // Obtener lista de tablas desde config
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
                    // Manejar nombres con espacios (Farmatic)
                    var sanitizedTableName = table.TableName.Replace(" ", "_");
                    
                    _logger.Information("Exportando tabla {Table} ({Sanitized})", 
                        table.TableName, sanitizedTableName);
                    
                    var result = await ExportTableRawAsync(
                        table.TableName,
                        table.IncrementalColumn,
                        table.LastExportTimestamp ?? lastExportTimestamp
                    );
                    
                    results.Add(result);
                    totalRows += result.RowsExported;
                    
                    // Progress reporting
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
        /// Exporta una tabla con optimizaciones de Fase 4 (timestamp detection, CDC/CT)
        /// </summary>
        public async Task<ExportResult> ExportTableRawAsync(
            string tableName,
            string? incrementalColumn,
            DateTime? lastExportTimestamp)
        {
            var sanitizedTableName = tableName.Replace(" ", "_");
            var fileName = $"{sanitizedTableName}_{_farmaciaId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var filePath = Path.Combine(_outputPath, fileName);

            _logger.Information("Exportando {Table} a {File}", tableName, fileName);

            // ✅ FASE 4.1: Detectar automáticamente columna timestamp si no está especificada
            if (string.IsNullOrEmpty(incrementalColumn))
            {
                var detector = new TimestampColumnDetector(_logger);
                incrementalColumn = await detector.DetectBestTimestampColumnAsync(_connection, tableName);
                
                if (!string.IsNullOrEmpty(incrementalColumn))
                {
                    _logger.Information("Columna incremental detectada automáticamente: {Column}", 
                        incrementalColumn);
                }
                else
                {
                    _logger.Information("No se detectó columna timestamp. Exportación completa (SELECT *)");
                }
            }

            // ✅ FASE 4.1: Usar CDC/Change Tracking si está disponible
            string query;
            bool usedAdvancedTracking = false;
            
            if (_connection.GetType().Name.Contains("Oracle"))
            {
                // Oracle: intentar usar CDC (Change Data Capture)
                var cdcDetector = new OracleCdcDetector(_logger);
                var cdcEnabled = await cdcDetector.IsCdcEnabledAsync(
                    (Oracle.ManagedDataAccess.Client.OracleConnection)_connection, 
                    tableName);
                
                if (cdcEnabled)
                {
                    _logger.Information("✓ Usando Oracle CDC para extracción incremental");
                    var lastScn = await LoadLastScnAsync(tableName);
                    query = await cdcDetector.GetChangesSinceScnAsync(
                        (Oracle.ManagedDataAccess.Client.OracleConnection)_connection, 
                        tableName, 
                        lastScn);
                    
                    var currentScn = await cdcDetector.GetCurrentScnAsync(
                        (Oracle.ManagedDataAccess.Client.OracleConnection)_connection);
                    await SaveLastScnAsync(tableName, currentScn);
                    usedAdvancedTracking = true;
                }
                else
                {
                    query = BuildRawQuery(tableName, incrementalColumn, lastExportTimestamp);
                }
            }
            else
            {
                // SQL Server: intentar usar Change Tracking
                var changeTracker = new SqlServerChangeTracker(_logger);
                var trackingEnabled = await changeTracker.IsTableTrackedAsync(
                    (Microsoft.Data.SqlClient.SqlConnection)_connection, 
                    tableName);
                
                if (trackingEnabled)
                {
                    _logger.Information("✓ Usando SQL Server Change Tracking para extracción incremental");
                    var lastVersion = await LoadLastChangeVersionAsync(tableName);
                    query = changeTracker.BuildChangeTrackingQuery(tableName, lastVersion);
                    
                    var currentVersion = await changeTracker.GetCurrentVersionAsync(
                        (Microsoft.Data.SqlClient.SqlConnection)_connection);
                    await SaveLastChangeVersionAsync(tableName, currentVersion);
                    usedAdvancedTracking = true;
                }
                else
                {
                    query = BuildRawQuery(tableName, incrementalColumn, lastExportTimestamp);
                }
            }
            
            if (!usedAdvancedTracking && !string.IsNullOrEmpty(incrementalColumn))
            {
                _logger.Information("Usando extracción incremental tradicional (columna: {Column})", 
                    incrementalColumn);
            }
            else if (!usedAdvancedTracking)
            {
                _logger.Information("Usando extracción completa (SELECT * sin filtros)");
            }

            // Ejecutar exportación
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var rowCount = await ExportToCsvAsync(query, filePath, lastExportTimestamp);
            stopwatch.Stop();

            if (rowCount == 0)
            {
                _logger.Information("No hay datos nuevos para exportar");
                
                // Eliminar archivo vacío
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
                ColumnCount = 0 // Se actualiza en ExportToCsvAsync si es necesario
            };
        }

        /// <summary>
        /// ✅ FASE 4.2: Exportar con compresión y particionado automático
        /// </summary>
        public async Task<List<ExportResult>> ExportTableRawWithCompressionAsync(
            string tableName,
            string? incrementalColumn,
            DateTime? lastExportTimestamp)
        {
            // Exportar normalmente primero
            var baseResult = await ExportTableRawAsync(tableName, incrementalColumn, lastExportTimestamp);
            
            if (!baseResult.Success || baseResult.RowsExported == 0)
            {
                return new List<ExportResult> { baseResult };
            }
            
            var results = new List<ExportResult>();
            var compressor = new FileCompressor(_logger);
            
            // ✅ FASE 4.2: Particionar si es necesario (archivos >100MB)
            var partitions = await compressor.PartitionIfNeededAsync(baseResult.FilePath);
            
            if (partitions.Count > 1)
            {
                _logger.Information("Tabla {Table} dividida en {Count} partición(es)", 
                    tableName, partitions.Count);
            }
            
            // ✅ FASE 4.2: Comprimir cada partición
            foreach (var partitionPath in partitions)
            {
                var compressedPath = await compressor.CompressIfNeededAsync(partitionPath);
                
                var partResult = new ExportResult
                {
                    Success = true,
                    FilePath = compressedPath,
                    TableName = tableName,
                    ExportTimestamp = baseResult.ExportTimestamp,
                    RowsExported = baseResult.RowsExported / partitions.Count, // Estimado
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

        private string BuildRawQuery(
            string tableName, 
            string? incrementalColumn, 
            DateTime? lastExport)
        {
            // Escapar nombre de tabla según BD - manejar espacios y caracteres especiales
            string escapedTable;
            if (_connection.GetType().Name.Contains("Oracle"))
            {
                // Oracle: "TABLA" - mayúsculas, sin espacios
                escapedTable = $"\"{tableName.ToUpper()}\"";
            }
            else
            {
                // SQL Server: [tabla] - permite espacios
                escapedTable = $"[{tableName}]";
            }

            var query = $"SELECT * FROM {escapedTable}";

            if (!string.IsNullOrEmpty(incrementalColumn) && lastExport.HasValue)
            {
                var paramName = _connection.GetType().Name.Contains("Oracle") 
                    ? ":lastExport"  // Oracle
                    : "@lastExport"; // SQL Server

                var escapedColumn = _connection.GetType().Name.Contains("Oracle")
                    ? $"\"{incrementalColumn.ToUpper()}\""
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

        // ✅ FASE 4.1: Métodos auxiliares para tracking de versiones CDC/Change Tracking
        
        private async Task<long> LoadLastScnAsync(string tableName)
        {
            var trackingFile = Path.Combine(@"C:\FarmacopilotAgent", $"scn_{tableName.Replace(" ", "_")}.txt");
            if (File.Exists(trackingFile))
            {
                var content = await File.ReadAllTextAsync(trackingFile);
                if (long.TryParse(content, out var scn))
                {
                    _logger.Debug("SCN anterior cargado para {Table}: {Scn}", tableName, scn);
                    return scn;
                }
            }
            
            _logger.Debug("No hay SCN anterior para {Table}, iniciando desde 0", tableName);
            return 0;
        }

        private async Task SaveLastScnAsync(string tableName, long scn)
        {
            var trackingFile = Path.Combine(@"C:\FarmacopilotAgent", $"scn_{tableName.Replace(" ", "_")}.txt");
            await File.WriteAllTextAsync(trackingFile, scn.ToString());
            _logger.Debug("SCN guardado para {Table}: {Scn}", tableName, scn);
        }

        private async Task<long> LoadLastChangeVersionAsync(string tableName)
        {
            var trackingFile = Path.Combine(@"C:\FarmacopilotAgent", $"ctversion_{tableName.Replace(" ", "_")}.txt");
            if (File.Exists(trackingFile))
            {
                var content = await File.ReadAllTextAsync(trackingFile);
                if (long.TryParse(content, out var version))
                {
                    _logger.Debug("Change Tracking version anterior cargada para {Table}: {Version}", 
                        tableName, version);
                    return version;
                }
            }
            
            _logger.Debug("No hay Change Tracking version anterior para {Table}, iniciando desde 0", 
                tableName);
            return 0;
        }

        private async Task SaveLastChangeVersionAsync(string tableName, long version)
        {
            var trackingFile = Path.Combine(@"C:\FarmacopilotAgent", $"ctversion_{tableName.Replace(" ", "_")}.txt");
            await File.WriteAllTextAsync(trackingFile, version.ToString());
            _logger.Debug("Change Tracking version guardada para {Table}: {Version}", 
                tableName, version);
        }
    }
}
