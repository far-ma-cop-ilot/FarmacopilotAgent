using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FarmacopilotAgent.Core.Models;
using FarmacopilotAgent.Core.Security;
using Serilog;

namespace FarmacopilotAgent.Core.Utils
{
    public class ConfigManager
    {
        private readonly string _configFilePath;
        private readonly ILogger _logger;

        public ConfigManager(string basePath, ILogger logger)
        {
            _configFilePath = Path.Combine(basePath, "config.json");
            _logger = logger;
        }

        public async Task<AgentConfig?> LoadConfigAsync()
        {
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    _logger.Warning("Archivo config.json no encontrado en {Path}", _configFilePath);
                    return null;
                }

                var json = await File.ReadAllTextAsync(_configFilePath);
                var config = JsonSerializer.Deserialize<AgentConfig>(json);

                if (config != null)
                {
                    _logger.Information("Configuración cargada correctamente para {FarmaciaId}", config.FarmaciaId);
                }

                return config;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al cargar configuración desde {Path}", _configFilePath);
                return null;
            }
        }

        public async Task SaveConfigAsync(AgentConfig config)
        {
            try
            {
                // Asegurar que las cadenas de conexión estén cifradas antes de guardar
                if (!string.IsNullOrEmpty(config.DbConnectionEncrypted) && 
                    !DPAPIHelper.IsEncrypted(config.DbConnectionEncrypted))
                {
                    config.DbConnectionEncrypted = DPAPIHelper.Encrypt(config.DbConnectionEncrypted);
                }

                if (!string.IsNullOrEmpty(config.PostgresConnectionEncrypted) && 
                    !DPAPIHelper.IsEncrypted(config.PostgresConnectionEncrypted))
                {
                    config.PostgresConnectionEncrypted = DPAPIHelper.Encrypt(config.PostgresConnectionEncrypted);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(config, options);
                await File.WriteAllTextAsync(_configFilePath, json);

                _logger.Information("Configuración guardada correctamente en {Path}", _configFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al guardar configuración en {Path}", _configFilePath);
                throw;
            }
        }

        /// <summary>
        /// Obtiene la cadena de conexión del ERP descifrada
        /// </summary>
        public string GetDecryptedConnectionString(AgentConfig config)
        {
            if (string.IsNullOrEmpty(config.DbConnectionEncrypted))
            {
                _logger.Error("Cadena de conexión ERP no configurada");
                throw new InvalidOperationException("ERP connection string not configured");
            }

            try
            {
                if (DPAPIHelper.IsEncrypted(config.DbConnectionEncrypted))
                {
                    return DPAPIHelper.Decrypt(config.DbConnectionEncrypted);
                }
                return config.DbConnectionEncrypted;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al descifrar cadena de conexión ERP");
                throw;
            }
        }

        /// <summary>
        /// Obtiene la cadena de conexión PostgreSQL descifrada
        /// </summary>
        public string GetDecryptedPostgresConnection(AgentConfig config)
        {
            if (string.IsNullOrEmpty(config.PostgresConnectionEncrypted))
            {
                _logger.Error("Conexión PostgreSQL no configurada");
                throw new InvalidOperationException("PostgreSQL connection not configured");
            }

            try
            {
                if (DPAPIHelper.IsEncrypted(config.PostgresConnectionEncrypted))
                {
                    return DPAPIHelper.Decrypt(config.PostgresConnectionEncrypted);
                }
                return config.PostgresConnectionEncrypted;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al descifrar cadena de conexión PostgreSQL");
                throw;
            }
        }
    }
}
