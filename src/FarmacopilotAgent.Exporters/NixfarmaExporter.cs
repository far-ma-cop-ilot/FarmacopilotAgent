using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using FarmacopilotAgent.Core.Interfaces;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Exporters
{
    /// <summary>
    /// Exportador para ERP Nixfarma (SQL Server)
    /// Soporta versiones 10.x y 11.x con detección automática
    /// </summary>
    public class NixfarmaExporter : IExporter
    {
        private readonly string _connectionString;
        private readonly string _outputPath;
        private readonly string _farmaciaId;
        private readonly ILogger _logger;
        private string? _erpVersion;
        private NixfarmaMapping? _mapping;

        public NixfarmaExporter(string connectionString, string outputPath, string farmaciaId, ILogger logger)
        {
            _connectionString = connectionString;
            _outputPath = outputPath;
            _farmaciaId = farmaciaId;
            _logger = logger;
        }

        /// <summary>
        /// Prueba la conexión a la base de datos
        /// </summary>
        public async Task<bool> TestConnectionAsync(string connectionString)
        {
            try
            {
                _logger.Information("Probando conexión a SQL Server...");
                
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                
                _logger.Information("Conexión exitosa a SQL Server");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al conectar a SQL Server");
                return false;
            }
        }

        /// <summary>
        /// Detecta información del ERP Nixfarma
        /// </summary>
        public async Task<ErpInfo> DetectErpInfoAsync()
        {
            try
            {
                _logger.Information("Detectando información de Nixfarma...");

                await using var connection = new SqlConnection(_connectionString);
                await connection.OpenAsync();

                // Intentar detectar versión desde tabla de sistema
                string? version = null;
                
                try
                {
                    var query = @"
                        SELECT TOP 1 valor 
                        FROM configuracion WITH (NOLOCK)
                        WHERE parametro = 'VERSION_SISTEMA'";
                    
                    await using var command = new SqlCommand(query, connection);
                    version = (string?)await command.ExecuteScalarAsync();
                }
                catch
                {
                    _logger.Warning("No se pudo obtener versión desde tabla configuracion");
                }

                // Si no se encuentra, intentar desde tabla alternativa
                if (string.IsNullOrEmpty(version))
                {
                    try
                    {
                        var query = "SELECT @@VERSION";
                        await using var command = new SqlCommand(query, connection);
                        var sqlVersion = (string?)await command.ExecuteScalarAsync();
                        _logger.Information("SQL Server version: {Version}", sqlVersion);
                        
                        // Asumir versión 11.x si no se puede determinar
                        version = "11.0";
                    }
                    catch
                    {
                        version = "11.0";
                    }
                }

                _erpVersion = version;

                return new ErpInfo
                {
                    ErpType = "Nixfarma",
                    Version = version ?? "11.0",
                    DatabaseType = "SQL Server",
                    DetectionMethod = "database_query"
                };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al detectar información de Nixfarma");
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
                _logger.Information("Iniciando exportación {Type} de Nixfarma v{Version}", 
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
                _logger.Error(ex, "Error durante la exportación de Nixfarma");
                
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
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();

            // Construir query incremental
            var query = BuildVentasQuery(lastExportTimestamp);

            await using var command = new SqlCommand(query, connection);
            command.CommandTimeout = 300; // 5 minutos

            if (lastExportTimestamp.HasValue)
            {
                command.Parameters.AddWithValue("@LastExport", lastExportTimestamp.Value);
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
            
            if (_erpVersion!.StartsWith("10."))
            {
                // Query para Nixfarma 10.x
                query = $@"
                    SELECT {columns}
                    FROM VENTAS_LINEAS v WITH (NOLOCK)
                    INNER JOIN ARTICULOS a WITH (NOLOCK) ON v.CODIGO_ARTICULO = a.CODIGO
                    WHERE 1=1";
            }
            else
            {
                // Query para Nixfarma 11.x
                query = $@"
                    SELECT {columns}
                    FROM VentasLineas v WITH (NOLOCK)
                    INNER JOIN Articulos a WITH (NOLOCK) ON v.CodigoArticulo = a.Codigo
                    WHERE 1=1";
            }

            if (lastExportTimestamp.HasValue)
            {
                query += $" AND v.{tableConfig.IncrementalColumn} > @LastExport";
            }

            query += $" ORDER BY v.{tableConfig.IncrementalColumn} ASC";

            return query;
        }

        private NixfarmaMapping LoadMapping(string version)
        {
            var mappingFileName = version.StartsWith("10.") ? "nixfarma_v10.json" : "nixfarma_v11.json";
            var mappingPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mappings", mappingFileName);

            if (!File.Exists(mappingPath))
            {
                _logger.Error("Archivo de mapping no encontrado: {Path}", mappingPath);
                throw new FileNotFoundException($"Mapping file not found: {mappingPath}");
            }

            var json = File.ReadAllText(mappingPath);
            var mapping = JsonSerializer.Deserialize<NixfarmaMapping>(json);

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
        private class NixfarmaMapping
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
