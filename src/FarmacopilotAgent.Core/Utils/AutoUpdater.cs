using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Core.Utils
{
    public class AutoUpdater
    {
        private readonly string _basePath;
        private readonly ILogger _logger;
        private readonly string _currentVersion = "1.0.0";
        private readonly string _updateCheckUrl = "https://api.farmacopilot.com/api/agent/version";

        public AutoUpdater(string basePath, ILogger logger)
        {
            _basePath = basePath;
            _logger = logger;
        }

        public async Task<bool> CheckAndUpdateAsync()
        {
            try
            {
                _logger.Information("Verificando actualizaciones...");
                
                var latestVersion = await GetLatestVersionInfoAsync();
                if (latestVersion == null) return false;

                if (!IsNewerVersion(latestVersion.Version)) 
                {
                    _logger.Information("Ya tienes la última versión");
                    return false;
                }

                _logger.Information("Nueva versión disponible: {Version}", latestVersion.Version);
                
                if (latestVersion.IsMandatory)
                {
                    _logger.Warning("Esta actualización es obligatoria");
                }

                // Descargar nueva versión
                var updatePath = await DownloadUpdateAsync(latestVersion);
                if (string.IsNullOrEmpty(updatePath)) return false;

                // Verificar integridad
                if (!VerifyFileIntegrity(updatePath, latestVersion.Sha256Hash))
                {
                    _logger.Error("Verificación SHA256 falló. Abortando actualización.");
                    File.Delete(updatePath);
                    return false;
                }

                // Crear backup actual
                var backupPath = CreateBackup();

                // Aplicar actualización
                var success = await ApplyUpdateAsync(updatePath, backupPath);
                
                if (!success && !string.IsNullOrEmpty(backupPath))
                {
                    // Rollback si falla
                    await RollbackAsync(backupPath);
                }

                return success;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error durante auto-actualización");
                return false;
            }
        }

        private async Task<VersionInfo?> GetLatestVersionInfoAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                var response = await client.GetAsync(_updateCheckUrl);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<VersionInfo>(json);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudo verificar versión desde servidor");
                return null;
            }
        }

        private bool IsNewerVersion(string remoteVersion)
        {
            try
            {
                var current = Version.Parse(_currentVersion);
                var remote = Version.Parse(remoteVersion);
                return remote > current;
            }
            catch
            {
                return false;
            }
        }

        private async Task<string> DownloadUpdateAsync(VersionInfo version)
        {
            try
            {
                var updateDir = Path.Combine(_basePath, "updates");
                Directory.CreateDirectory(updateDir);
                
                var fileName = $"FarmacopilotAgent_{version.Version}.exe";
                var filePath = Path.Combine(updateDir, fileName);

                _logger.Information("Descargando actualización: {Size:N0} KB", version.FileSizeBytes / 1024);

                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromMinutes(10);

                using var response = await client.GetAsync(version.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                
                await stream.CopyToAsync(fileStream);
                
                _logger.Information("Descarga completada: {File}", fileName);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error descargando actualización");
                return string.Empty;
            }
        }

        private bool VerifyFileIntegrity(string filePath, string expectedHash)
        {
            try
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = sha256.ComputeHash(stream);
                var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                
                return hashString.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private string CreateBackup()
        {
            try
            {
                var backupDir = Path.Combine(_basePath, "backup");
                Directory.CreateDirectory(backupDir);
                
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
                var backupPath = Path.Combine(backupDir, $"backup_{timestamp}");
                Directory.CreateDirectory(backupPath);

                // Copiar ejecutables actuales
                var filesToBackup = new[] 
                { 
                    "FarmacopilotAgent.Runner.exe",
                    "config.json",
                    "secrets.enc"
                };

                foreach (var file in filesToBackup)
                {
                    var source = Path.Combine(_basePath, file);
                    if (File.Exists(source))
                    {
                        var dest = Path.Combine(backupPath, file);
                        File.Copy(source, dest, true);
                    }
                }

                _logger.Information("Backup creado en {Path}", backupPath);
                return backupPath;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "No se pudo crear backup");
                return string.Empty;
            }
        }

        private async Task<bool> ApplyUpdateAsync(string updatePath, string backupPath)
        {
            try
            {
                _logger.Information("Aplicando actualización...");

                // Crear script PowerShell para actualización
                var scriptPath = Path.Combine(_basePath, "apply_update.ps1");
                var script = $@"
                    Start-Sleep -Seconds 5
                    Stop-Process -Name 'FarmacopilotAgent.Runner' -Force -ErrorAction SilentlyContinue
                    Start-Sleep -Seconds 2
                    Copy-Item '{updatePath}' 'C:\FarmacopilotAgent\FarmacopilotAgent.Runner.exe' -Force
                    Start-Process 'C:\FarmacopilotAgent\FarmacopilotAgent.Runner.exe' -ArgumentList '--verify-update'
                    Remove-Item '{scriptPath}' -Force
                ";

                await File.WriteAllTextAsync(scriptPath, script);

                // Ejecutar script en proceso separado
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                Process.Start(psi);
                
                _logger.Information("Script de actualización iniciado. El agente se reiniciará...");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error aplicando actualización");
                return false;
            }
        }

        private async Task RollbackAsync(string backupPath)
        {
            try
            {
                _logger.Warning("Ejecutando rollback desde {Path}", backupPath);

                var files = Directory.GetFiles(backupPath);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileName(file);
                    var destPath = Path.Combine(_basePath, fileName);
                    File.Copy(file, destPath, true);
                }

                _logger.Information("Rollback completado");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error durante rollback. Intervención manual requerida.");
            }
        }
    }
}
