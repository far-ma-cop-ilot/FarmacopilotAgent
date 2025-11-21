using System;
using System.IO;
using Microsoft.Win32;
using FarmacopilotAgent.Core.Interfaces;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Detection
{
    /// <summary>
    /// Detecta ERP instalado en el sistema mediante registro y archivos de configuración
    /// </summary>
    public class ErpDetector : IErpDetector
    {
        private readonly ILogger _logger;

        public ErpDetector(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Detecta el ERP instalado en el sistema
        /// </summary>
        public ErpInfo DetectErp()
        {
            _logger.Information("Iniciando detección de ERP...");

            // 1. Intentar detectar Nixfarma
            var nixfarmaInfo = DetectNixfarma();
            if (nixfarmaInfo != null)
            {
                _logger.Information("Nixfarma detectado: versión {Version}", nixfarmaInfo.Version);
                return nixfarmaInfo;
            }

            // 2. Intentar detectar Farmatic
            var farmaticInfo = DetectFarmatic();
            if (farmaticInfo != null)
            {
                _logger.Information("Farmatic detectado: versión {Version}", farmaticInfo.Version);
                return farmaticInfo;
            }

            _logger.Error("No se detectó ningún ERP compatible instalado");
            throw new InvalidOperationException("No se detectó ningún ERP compatible (Nixfarma o Farmatic)");
        }

        /// <summary>
        /// Valida que la instalación del ERP sea correcta
        /// </summary>
        public bool ValidateErpInstallation(ErpInfo erpInfo)
        {
            if (erpInfo == null)
                return false;

            // Validar que exista la ruta de instalación
            if (string.IsNullOrEmpty(erpInfo.InstallPath))
            {
                _logger.Warning("Ruta de instalación no encontrada");
                return false;
            }

            if (!Directory.Exists(erpInfo.InstallPath))
            {
                _logger.Warning("Ruta de instalación no existe: {Path}", erpInfo.InstallPath);
                return false;
            }

            // Validar versión soportada
            if (erpInfo.ErpType.Equals("Nixfarma", StringComparison.OrdinalIgnoreCase))
            {
                if (!erpInfo.Version.StartsWith("10.") && !erpInfo.Version.StartsWith("11."))
                {
                    _logger.Warning("Versión de Nixfarma no soportada: {Version}", erpInfo.Version);
                    return false;
                }
            }
            else if (erpInfo.ErpType.Equals("Farmatic", StringComparison.OrdinalIgnoreCase))
            {
                if (!erpInfo.Version.StartsWith("11.") && !erpInfo.Version.StartsWith("12."))
                {
                    _logger.Warning("Versión de Farmatic no soportada: {Version}", erpInfo.Version);
                    return false;
                }
            }

            _logger.Information("Instalación de ERP validada correctamente");
            return true;
        }

        private ErpInfo? DetectNixfarma()
        {
            try
            {
                _logger.Information("Buscando Nixfarma en registro...");

                // Intentar 64-bit primero
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Pulso Informatica\Nixfarma")
                    ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Pulso Informatica\Nixfarma");

                if (key == null)
                {
                    _logger.Information("Nixfarma no encontrado en registro");
                    return null;
                }

                var installPath = key.GetValue("InstallPath") as string ?? "";
                var version = key.GetValue("Version") as string ?? "unknown";

                // Si no está la versión en registro, intentar detectarla desde archivos
                if (version == "unknown" && !string.IsNullOrEmpty(installPath))
                {
                    version = DetectNixfarmaVersionFromFiles(installPath);
                }

                return new ErpInfo
                {
                    ErpType = "Nixfarma",
                    Version = version,
                    InstallPath = installPath,
                    DatabaseType = "Oracle",
                    DetectionMethod = "registry"
                };
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al detectar Nixfarma desde registro");
                return null;
            }
        }

        private ErpInfo? DetectFarmatic()
        {
            try
            {
                _logger.Information("Buscando Farmatic en registro...");

                // Intentar 64-bit primero
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Consoft\Farmatic")
                    ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Consoft\Farmatic");

                if (key == null)
                {
                    _logger.Information("Farmatic no encontrado en registro");
                    return null;
                }

                var installPath = key.GetValue("InstallPath") as string ?? "";
                var version = key.GetValue("Version") as string ?? "unknown";

                // Si no está la versión en registro, intentar detectarla desde archivos
                if (version == "unknown" && !string.IsNullOrEmpty(installPath))
                {
                    version = DetectFarmaticVersionFromFiles(installPath);
                }

                return new ErpInfo
                {
                    ErpType = "Farmatic",
                    Version = version,
                    InstallPath = installPath,
                    DatabaseType = "SQL Server",
                    DetectionMethod = "registry"
                };
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al detectar Farmatic desde registro");
                return null;
            }
        }

        private string DetectNixfarmaVersionFromFiles(string installPath)
        {
            try
            {
                // Buscar archivo version.txt o similar
                var versionFile = Path.Combine(installPath, "version.txt");
                if (File.Exists(versionFile))
                {
                    var content = File.ReadAllText(versionFile).Trim();
                    if (!string.IsNullOrEmpty(content))
                        return content;
                }

                // Buscar en archivos .exe principales
                var mainExe = Path.Combine(installPath, "Nixfarma.exe");
                if (File.Exists(mainExe))
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(mainExe);
                    if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                        return versionInfo.FileVersion;
                }

                _logger.Warning("No se pudo determinar versión de Nixfarma desde archivos");
                return "unknown";
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al detectar versión de Nixfarma desde archivos");
                return "unknown";
            }
        }

        private string DetectFarmaticVersionFromFiles(string installPath)
        {
            try
            {
                // Buscar archivo version.txt o similar
                var versionFile = Path.Combine(installPath, "version.txt");
                if (File.Exists(versionFile))
                {
                    var content = File.ReadAllText(versionFile).Trim();
                    if (!string.IsNullOrEmpty(content))
                        return content;
                }

                // Buscar en archivos .exe principales
                var mainExe = Path.Combine(installPath, "Farmatic.exe");
                if (File.Exists(mainExe))
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(mainExe);
                    if (!string.IsNullOrEmpty(versionInfo.FileVersion))
                        return versionInfo.FileVersion;
                }

                _logger.Warning("No se pudo determinar versión de Farmatic desde archivos");
                return "unknown";
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al detectar versión de Farmatic desde archivos");
                return "unknown";
            }
        }
    }
}
