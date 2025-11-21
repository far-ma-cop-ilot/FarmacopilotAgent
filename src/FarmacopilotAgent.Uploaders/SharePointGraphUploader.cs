using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Identity.Client;
using FarmacopilotAgent.Core.Interfaces;
using FarmacopilotAgent.Core.Security;
using Serilog;

namespace FarmacopilotAgent.Uploaders
{
    public class SharePointGraphUploader : IUploader
    {
        private readonly string _farmaciaId;
        private readonly GraphCredentialsProvider.GraphCredentials _credentials;
        private readonly ILogger _logger;
        private readonly HttpClient _httpClient;

        public SharePointGraphUploader(
            string farmaciaId, 
            GraphCredentialsProvider.GraphCredentials credentials,
            ILogger logger)
        {
            _farmaciaId = farmaciaId;
            _credentials = credentials;
            _logger = logger;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// Obtiene access token usando Client Credentials Flow (sin interacción usuario)
        /// </summary>
        private async Task<string> GetAccessTokenAsync()
        {
            try
            {
                var app = ConfidentialClientApplicationBuilder
                    .Create(_credentials.ClientId)
                    .WithClientSecret(_credentials.ClientSecret)
                    .WithAuthority($"https://login.microsoftonline.com/{_credentials.TenantId}")
                    .Build();

                var scopes = new[] { "https://graph.microsoft.com/.default" };
                var result = await app.AcquireTokenForClient(scopes).ExecuteAsync();

                _logger.Information("Access token obtenido correctamente");
                return result.AccessToken;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al obtener access token de Graph API");
                throw;
            }
        }

        public async Task<bool> UploadFileAsync(string localFilePath, string remotePath)
        {
            try
            {
                if (!File.Exists(localFilePath))
                {
                    _logger.Error("Archivo local no encontrado: {Path}", localFilePath);
                    return false;
                }

                _logger.Information("Iniciando subida de {File} a SharePoint", Path.GetFileName(localFilePath));

                var accessToken = await GetAccessTokenAsync();
                var fileName = Path.GetFileName(localFilePath);
                
                // Construir ruta en SharePoint: /Clients/FAR{ID}/Exports/{fileName}
                var sharePointPath = $"/Clients/{_farmaciaId}/Exports/{fileName}";
                
                // URL de Graph API para subir archivo
                var uploadUrl = $"https://graph.microsoft.com/v1.0/sites/{_credentials.SharePointSiteId}/drive/root:{sharePointPath}:/content";

                using var fileStream = File.OpenRead(localFilePath);
                using var content = new StreamContent(fileStream);
                content.Headers.ContentType = new MediaTypeHeaderValue("text/csv");

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Implementar política de retry para uploads
                var retryPolicy = Policy
                    .HandleResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode && 
                        (r.StatusCode == System.Net.HttpStatusCode.TooManyRequests || 
                         r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable ||
                         r.StatusCode == System.Net.HttpStatusCode.GatewayTimeout))
                    .WaitAndRetryAsync(
                        3,
                        retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                        onRetry: (outcome, timespan, retry, context) =>
                        {
                            _logger.Warning("Reintento {Retry} de upload después de {Delay}s", 
                                retry, timespan.TotalSeconds);
                        });
                
                var response = await retryPolicy.ExecuteAsync(async () => 
                    await _httpClient.PutAsync(uploadUrl, content));
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.Information("Archivo subido exitosamente a SharePoint: {Path}", sharePointPath);
                    
                    // Subir también el archivo SHA256
                    await UploadSha256FileAsync(localFilePath, sharePointPath, accessToken);
                    
                    return true;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.Error("Error al subir archivo. Status: {Status}, Error: {Error}", 
                        response.StatusCode, error);
                    
                    // Guardar en queue para reintento diferido si es error temporal
                    if ((int)response.StatusCode >= 500)
                    {
                        await QueueForRetryAsync(localFilePath, sharePointPath);
                    }
                    
                    return false;
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.Error("Error al subir archivo. Status: {Status}, Error: {Error}", 
                        response.StatusCode, error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Excepción al subir archivo a SharePoint");
                return false;
            }
        }

        private async Task UploadSha256FileAsync(string originalFilePath, string sharePointPath, string accessToken)
        {
            try
            {
                var sha256Hash = CalculateSha256(originalFilePath);
                var sha256FileName = Path.GetFileName(originalFilePath) + ".sha256";
                var sha256Path = sharePointPath + ".sha256";

                var uploadUrl = $"https://graph.microsoft.com/v1.0/sites/{_credentials.SharePointSiteId}/drive/root:{sha256Path}:/content";

                using var content = new StringContent(sha256Hash, Encoding.UTF8, "text/plain");
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.PutAsync(uploadUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    _logger.Information("Archivo SHA256 subido: {File}", sha256FileName);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al subir archivo SHA256 (no crítico)");
            }
        }

        public async Task<bool> ValidateUploadAsync(string remotePath, string expectedSha256)
        {
            try
            {
                _logger.Information("Validando integridad del archivo subido");

                var accessToken = await GetAccessTokenAsync();
                var sha256Path = remotePath + ".sha256";
                var downloadUrl = $"https://graph.microsoft.com/v1.0/sites/{_credentials.SharePointSiteId}/drive/root:{sha256Path}:/content";

                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                var response = await _httpClient.GetAsync(downloadUrl);

                if (response.IsSuccessStatusCode)
                {
                    var remoteSha256 = await response.Content.ReadAsStringAsync();
                    var isValid = remoteSha256.Trim().Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);

                    if (isValid)
                    {
                        _logger.Information("Validación SHA256 exitosa");
                    }
                    else
                    {
                        _logger.Error("Validación SHA256 falló. Esperado: {Expected}, Obtenido: {Actual}", 
                            expectedSha256, remoteSha256);
                    }

                    return isValid;
                }
                else
                {
                    _logger.Warning("No se pudo descargar archivo SHA256 para validación");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error durante validación de integridad");
                return false;
            }
        }

        private string CalculateSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        private async Task QueueForRetryAsync(string localPath, string remotePath)
        {
            var queuePath = Path.Combine(@"C:\FarmacopilotAgent", "upload_queue");
            Directory.CreateDirectory(queuePath);
            
            var queueItem = new
            {
                LocalPath = localPath,
                RemotePath = remotePath,
                Timestamp = DateTime.UtcNow,
                FarmaciaId = _farmaciaId
            };
            
            var fileName = $"pending_{DateTime.UtcNow.Ticks}.json";
            var json = System.Text.Json.JsonSerializer.Serialize(queueItem);
            await File.WriteAllTextAsync(Path.Combine(queuePath, fileName), json);
            
            _logger.Information("Archivo encolado para reintento: {File}", Path.GetFileName(localPath));
        }
    }
}
