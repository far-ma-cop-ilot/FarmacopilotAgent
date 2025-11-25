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
                    _logger.Warning("Archivo de credenciales no encontrado, usando valores embebidos");
                    return new GraphCredentials
                    {
                        TenantId = "d543ed3a-c274-41c8-ad2e-393a36c2d1fc",
                        ClientId = "27f13cc9-95e5-4b8e-b7a3-dff7e3b9ec26",
                        ClientSecret = "IGU8Q~ODfxHsw_LSjAEAHep3C55fjvkyhiddScrx",
                        SharePointSiteId = "d14f0b31-c267-4493-82ea-02447a8cc665"
                    };
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
