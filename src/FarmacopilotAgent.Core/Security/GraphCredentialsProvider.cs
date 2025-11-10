using System;
using System.IO;
using System.Text.Json;
using Serilog;

namespace FarmacopilotAgent.Core.Security
{
    /// <summary>
    /// Proporciona credenciales de Graph API pre-configuradas desde el instalador
    /// </summary>
    public class GraphCredentialsProvider
    {
        private readonly ILogger _logger;
        private readonly string _secretsFilePath;

        public GraphCredentialsProvider(string basePath, ILogger logger)
        {
            _logger = logger;
            _secretsFilePath = Path.Combine(basePath, "secrets.enc");
        }

        public class GraphCredentials
        {
            public string TenantId { get; set; } = string.Empty;
            public string ClientId { get; set; } = string.Empty;
            public string ClientSecret { get; set; } = string.Empty;
            public string SharePointSiteId { get; set; } = string.Empty;
        }

        /// <summary>
        /// Lee las credenciales cifradas embebidas en el instalador
        /// </summary>
        public GraphCredentials GetCredentials()
        {
            try
            {
                if (!File.Exists(_secretsFilePath))
                {
                    _logger.Error("Archivo de credenciales no encontrado: {Path}", _secretsFilePath);
                    throw new FileNotFoundException("Credenciales de Graph API no encontradas");
                }

                var encryptedContent = File.ReadAllText(_secretsFilePath);
                var decryptedJson = DPAPIHelper.Decrypt(encryptedContent);
                var credentials = JsonSerializer.Deserialize<GraphCredentials>(decryptedJson);

                if (credentials == null || string.IsNullOrEmpty(credentials.ClientId))
                {
                    throw new InvalidOperationException("Credenciales inválidas");
                }

                _logger.Information("Credenciales de Graph API cargadas correctamente");
                return credentials;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al cargar credenciales de Graph API");
                throw;
            }
        }

        /// <summary>
        /// Método usado durante la instalación para crear el archivo de credenciales
        /// </summary>
        public static void CreateSecretsFile(string basePath, GraphCredentials credentials)
        {
            var json = JsonSerializer.Serialize(credentials);
            var encrypted = DPAPIHelper.Encrypt(json);
            var secretsPath = Path.Combine(basePath, "secrets.enc");
            File.WriteAllText(secretsPath, encrypted);
        }
    }
}
