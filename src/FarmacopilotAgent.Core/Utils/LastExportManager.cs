using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Core.Utils
{
    public class LastExportManager
    {
        private readonly string _lastExportFilePath;
        private readonly ILogger _logger;

        public LastExportManager(string basePath, ILogger logger)
        {
            _lastExportFilePath = Path.Combine(basePath, "last_export.json");
            _logger = logger;
        }

        /// <summary>
        /// Obtiene el timestamp de la última exportación exitosa
        /// </summary>
        public async Task<DateTime?> GetLastExportTimestampAsync()
        {
            try
            {
                if (!File.Exists(_lastExportFilePath))
                {
                    _logger.Information("Archivo last_export.json no existe. Primera extracción (full export)");
                    return null;
                }

                var json = await File.ReadAllTextAsync(_lastExportFilePath);
                var lastExport = JsonSerializer.Deserialize<LastExport>(json);

                if (lastExport != null && lastExport.LastSuccess)
                {
                    _logger.Information("Última exportación exitosa: {Timestamp}", lastExport.LastExportTimestamp);
                    return lastExport.LastExportTimestamp;
                }
                else
                {
                    _logger.Warning("Última exportación falló. Realizando exportación completa.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al leer last_export.json. Realizando exportación completa.");
                return null;
            }
        }

        /// <summary>
        /// Actualiza el archivo last_export.json con el resultado de la última exportación
        /// </summary>
        public async Task UpdateLastExportAsync(string farmaciaId, bool success, string error = "")
        {
            try
            {
                var lastExport = new LastExport
                {
                    FarmaciaId = farmaciaId,
                    LastExportTimestamp = DateTime.UtcNow,
                    LastSuccess = success,
                    LastError = error
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(lastExport, options);
                await File.WriteAllTextAsync(_lastExportFilePath, json);

                _logger.Information("Actualizado last_export.json - Success: {Success}", success);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al actualizar last_export.json");
            }
        }
    }
}
