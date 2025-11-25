using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using Oracle.ManagedDataAccess.Client;
using FarmacopilotAgent.Core.Interfaces;
using FarmacopilotAgent.Core.Models;
using Serilog;

namespace FarmacopilotAgent.Detection
{
    public class ErpDetector : IErpDetector
    {
        private readonly ILogger _logger;

        private static readonly string[] NixfarmaRegistryPaths = new[]
        {
            @"SOFTWARE\Pulso Informatica\Nixfarma",
            @"SOFTWARE\WOW6432Node\Pulso Informatica\Nixfarma",
            @"SOFTWARE\Pulso\Nixfarma",
            @"SOFTWARE\WOW6432Node\Pulso\Nixfarma",
            @"SOFTWARE\Nixfarma",
            @"SOFTWARE\WOW6432Node\Nixfarma"
        };

        private static readonly string[] FarmaticRegistryPaths = new[]
        {
            @"SOFTWARE\Consoft\Farmatic",
            @"SOFTWARE\WOW6432Node\Consoft\Farmatic",
            @"SOFTWARE\Consoft\Farmatic\16.0",
            @"SOFTWARE\WOW6432Node\Consoft\Farmatic\16.0",
            @"SOFTWARE\Farmatic",
            @"SOFTWARE\WOW6432Node\Farmatic"
        };

        private static readonly string[] NixfarmaInstallPaths = new[]
        {
            @"C:\Program Files\Pulso Informatica\Nixfarma",
            @"C:\Program Files (x86)\Pulso Informatica\Nixfarma",
            @"C:\Nixfarma",
            @"C:\Program Files\Nixfarma",
            @"C:\Program Files (x86)\Nixfarma",
            @"D:\Nixfarma",
            @"D:\Program Files\Pulso Informatica\Nixfarma"
        };

        private static readonly string[] FarmaticInstallPaths = new[]
        {
            @"C:\Program Files\Consoft\Farmatic",
            @"C:\Program Files (x86)\Consoft\Farmatic",
            @"C:\Farmatic",
            @"C:\Program Files\Farmatic",
            @"C:\Program Files (x86)\Farmatic",
            @"D:\Farmatic",
            @"D:\Program Files\Consoft\Farmatic"
        };

        private static readonly string[] NixfarmaTables = new[]
        {
            "AH_VENTAS", "AH_VENTA_LINEAS", "AB_ARTICULOS_FICHA_E", 
            "AB_LABORATORIOS", "AD_PROVEEDORES"
        };

        private static readonly string[] FarmaticTables = new[]
        {
            "ventas", "linea venta", "articu", "proveedor", "recep"
        };

        public ErpDetector(ILogger logger)
        {
            _logger = logger;
        }

        public ErpInfo DetectErp()
        {
            _logger.Information("═══════════════════════════════════════════════════════════");
            _logger.Information("   DETECCIÓN DE ERP - SISTEMA MULTICAPA");
            _logger.Information("═══════════════════════════════════════════════════════════");

            // CAPA 1: Registry
            _logger.Information("CAPA 1: Buscando en Registry...");
            var erpInfo = DetectFromRegistry();
            if (erpInfo != null)
            {
                _logger.Information("✓ ERP detectado via Registry: {Erp}", erpInfo.ErpType);
                return erpInfo;
            }

            // CAPA 2: Servicios Windows
            _logger.Information("CAPA 2: Buscando servicios de base de datos...");
            erpInfo = DetectFromServices();
            if (erpInfo != null)
            {
                _logger.Information("✓ ERP detectado via Servicios: {Erp}", erpInfo.ErpType);
                return erpInfo;
            }

            // CAPA 3: Archivos en rutas conocidas
            _logger.Information("CAPA 3: Buscando archivos en rutas conocidas...");
            erpInfo = DetectFromFileSystem();
            if (erpInfo != null)
            {
                _logger.Information("✓ ERP detectado via FileSystem: {Erp}", erpInfo.ErpType);
                return erpInfo;
            }

            // CAPA 4: Procesos en ejecución
            _logger.Information("CAPA 4: Buscando procesos en ejecución...");
            erpInfo = DetectFromRunningProcesses();
            if (erpInfo != null)
            {
                _logger.Information("✓ ERP detectado via Procesos: {Erp}", erpInfo.ErpType);
                return erpInfo;
            }

            // CAPA 5: Conexión directa a base de datos
            _logger.Information("CAPA 5: Intentando conexión directa a bases de datos...");
            erpInfo = DetectFromDatabase();
            if (erpInfo != null)
            {
                _logger.Information("✓ ERP detectado via Base de Datos: {Erp}", erpInfo.ErpType);
                return erpInfo;
            }

            _logger.Error("✗ No se detectó ningún ERP compatible");
            return new ErpInfo
            {
                ErpType = "unknown",
                Version = "unknown",
                DetectionMethod = "failed",
                IsCompatible = false,
                RequiresManualConfiguration = true,
                IncompatibilityReasons = new List<string> { "No se detectó Nixfarma ni Farmatic instalado" }
            };
        }

        public bool ValidateErpInstallation(ErpInfo erpInfo)
        {
            if (erpInfo == null || erpInfo.ErpType == "unknown")
                return false;

            if (erpInfo.RequiresManualConfiguration)
            {
                _logger.Warning("ERP requiere configuración manual");
                return false;
            }

            _logger.Information("Instalación de ERP validada: {Erp} v{Version}", 
                erpInfo.ErpType, erpInfo.Version);
            return true;
        }

        private ErpInfo? DetectFromRegistry()
        {
            // Detectar Nixfarma
            foreach (var path in NixfarmaRegistryPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key != null)
                    {
                        _logger.Information("  Registry encontrado: {Path}", path);
                        var installPath = key.GetValue("InstallPath") as string 
                            ?? key.GetValue("Path") as string 
                            ?? key.GetValue("InstallDir") as string 
                            ?? "";
                        var version = key.GetValue("Version") as string 
                            ?? key.GetValue("CurrentVersion") as string 
                            ?? "unknown";

                        return new ErpInfo
                        {
                            ErpType = "Nixfarma",
                            Version = version,
                            InstallPath = installPath,
                            DatabaseType = "Oracle",
                            DetectionMethod = "registry",
                            ConfidenceLevel = 90
                        };
                    }
                }
                catch { }
            }

            // Detectar Farmatic
            foreach (var path in FarmaticRegistryPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key != null)
                    {
                        _logger.Information("  Registry encontrado: {Path}", path);
                        var installPath = key.GetValue("InstallPath") as string 
                            ?? key.GetValue("Path") as string 
                            ?? key.GetValue("InstallDir") as string 
                            ?? "";
                        var version = key.GetValue("Version") as string 
                            ?? key.GetValue("CurrentVersion") as string 
                            ?? "unknown";

                        return new ErpInfo
                        {
                            ErpType = "Farmatic",
                            Version = version,
                            InstallPath = installPath,
                            DatabaseType = "SQL Server",
                            DetectionMethod = "registry",
                            ConfidenceLevel = 90
                        };
                    }
                }
                catch { }
            }

            _logger.Information("  No se encontró ERP en Registry");
            return null;
        }

        private ErpInfo? DetectFromServices()
        {
            try
            {
                var services = ServiceController.GetServices();

                // Buscar Oracle (indica Nixfarma)
                var oracleServices = services.Where(s => 
                    s.ServiceName.Contains("Oracle", StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains("OracleService", StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains("TNSListener", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (oracleServices.Any(s => s.Status == ServiceControllerStatus.Running))
                {
                    _logger.Information("  Servicio Oracle detectado y corriendo");
                    return new ErpInfo
                    {
                        ErpType = "Nixfarma",
                        Version = "unknown",
                        DatabaseType = "Oracle",
                        DetectionMethod = "service",
                        ConfidenceLevel = 70,
                        AdditionalInfo = new Dictionary<string, string>
                        {
                            { "OracleService", oracleServices.First(s => s.Status == ServiceControllerStatus.Running).ServiceName }
                        }
                    };
                }

                // Buscar SQL Server (indica Farmatic)
                var sqlServices = services.Where(s => 
                    s.ServiceName.Contains("MSSQL", StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Contains("SQLServer", StringComparison.OrdinalIgnoreCase) ||
                    s.ServiceName.Equals("SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (sqlServices.Any(s => s.Status == ServiceControllerStatus.Running))
                {
                    _logger.Information("  Servicio SQL Server detectado y corriendo");
                    return new ErpInfo
                    {
                        ErpType = "Farmatic",
                        Version = "unknown",
                        DatabaseType = "SQL Server",
                        DetectionMethod = "service",
                        ConfidenceLevel = 70,
                        AdditionalInfo = new Dictionary<string, string>
                        {
                            { "SqlService", sqlServices.First(s => s.Status == ServiceControllerStatus.Running).ServiceName }
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "  Error al buscar servicios");
            }

            _logger.Information("  No se encontraron servicios de BD relevantes");
            return null;
        }

        private ErpInfo? DetectFromFileSystem()
        {
            // Buscar Nixfarma
            foreach (var path in NixfarmaInstallPaths)
            {
                if (Directory.Exists(path))
                {
                    var exePath = Path.Combine(path, "Nixfarma.exe");
                    var altExePath = Path.Combine(path, "nix.exe");
                    
                    if (File.Exists(exePath) || File.Exists(altExePath))
                    {
                        _logger.Information("  Nixfarma encontrado en: {Path}", path);
                        var version = GetVersionFromExe(File.Exists(exePath) ? exePath : altExePath);
                        
                        return new ErpInfo
                        {
                            ErpType = "Nixfarma",
                            Version = version,
                            InstallPath = path,
                            DatabaseType = "Oracle",
                            DetectionMethod = "filesystem",
                            ConfidenceLevel = 85
                        };
                    }
                }
            }

            // Buscar Farmatic
            foreach (var path in FarmaticInstallPaths)
            {
                if (Directory.Exists(path))
                {
                    var exePath = Path.Combine(path, "Farmatic.exe");
                    var altExePath = Path.Combine(path, "far.exe");
                    
                    if (File.Exists(exePath) || File.Exists(altExePath))
                    {
                        _logger.Information("  Farmatic encontrado en: {Path}", path);
                        var version = GetVersionFromExe(File.Exists(exePath) ? exePath : altExePath);
                        
                        return new ErpInfo
                        {
                            ErpType = "Farmatic",
                            Version = version,
                            InstallPath = path,
                            DatabaseType = "SQL Server",
                            DetectionMethod = "filesystem",
                            ConfidenceLevel = 85
                        };
                    }
                }
            }

            _logger.Information("  No se encontró ERP en rutas conocidas");
            return null;
        }

        private ErpInfo? DetectFromRunningProcesses()
        {
            try
            {
                var processes = Process.GetProcesses();

                // Buscar proceso Nixfarma
                var nixProcess = processes.FirstOrDefault(p => 
                    p.ProcessName.Contains("Nixfarma", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Contains("nix", StringComparison.OrdinalIgnoreCase));

                if (nixProcess != null)
                {
                    _logger.Information("  Proceso Nixfarma detectado: {Process}", nixProcess.ProcessName);
                    var installPath = Path.GetDirectoryName(nixProcess.MainModule?.FileName) ?? "";
                    
                    return new ErpInfo
                    {
                        ErpType = "Nixfarma",
                        Version = nixProcess.MainModule?.FileVersionInfo.FileVersion ?? "unknown",
                        InstallPath = installPath,
                        DatabaseType = "Oracle",
                        DetectionMethod = "process",
                        ConfidenceLevel = 95
                    };
                }

                // Buscar proceso Farmatic
                var farProcess = processes.FirstOrDefault(p => 
                    p.ProcessName.Contains("Farmatic", StringComparison.OrdinalIgnoreCase) ||
                    p.ProcessName.Contains("far", StringComparison.OrdinalIgnoreCase));

                if (farProcess != null)
                {
                    _logger.Information("  Proceso Farmatic detectado: {Process}", farProcess.ProcessName);
                    var installPath = Path.GetDirectoryName(farProcess.MainModule?.FileName) ?? "";
                    
                    return new ErpInfo
                    {
                        ErpType = "Farmatic",
                        Version = farProcess.MainModule?.FileVersionInfo.FileVersion ?? "unknown",
                        InstallPath = installPath,
                        DatabaseType = "SQL Server",
                        DetectionMethod = "process",
                        ConfidenceLevel = 95
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "  Error al buscar procesos");
            }

            _logger.Information("  No se encontraron procesos ERP en ejecución");
            return null;
        }

        private ErpInfo? DetectFromDatabase()
        {
            // Intentar SQL Server primero (más común y rápido)
            var farmaticInfo = TryDetectFarmaticFromSqlServer();
            if (farmaticInfo != null)
                return farmaticInfo;

            // Intentar Oracle
            var nixfarmaInfo = TryDetectNixfarmaFromOracle();
            if (nixfarmaInfo != null)
                return nixfarmaInfo;

            return null;
        }

        private ErpInfo? TryDetectFarmaticFromSqlServer()
        {
            var connectionStrings = new[]
            {
                @"Server=localhost;Database=CGCOF;Integrated Security=true;TrustServerCertificate=true;",
                @"Server=localhost\SQLEXPRESS;Database=CGCOF;Integrated Security=true;TrustServerCertificate=true;",
                @"Server=.\SQLEXPRESS;Database=CGCOF;Integrated Security=true;TrustServerCertificate=true;",
                @"Server=(local);Database=CGCOF;Integrated Security=true;TrustServerCertificate=true;",
                @"Server=127.0.0.1;Database=CGCOF;Integrated Security=true;TrustServerCertificate=true;"
            };

            foreach (var connStr in connectionStrings)
            {
                try
                {
                    using var connection = new SqlConnection(connStr);
                    connection.Open();

                    // Verificar tablas características de Farmatic
                    var tablesFound = 0;
                    foreach (var table in FarmaticTables)
                    {
                        var query = $"SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @table";
                        using var cmd = new SqlCommand(query, connection);
                        cmd.Parameters.AddWithValue("@table", table);
                        var count = (int)cmd.ExecuteScalar();
                        if (count > 0) tablesFound++;
                    }

                    if (tablesFound >= 3)
                    {
                        _logger.Information("  Farmatic detectado via SQL Server ({Tables} tablas encontradas)", tablesFound);
                        
                        // Obtener versión de SQL Server
                        using var versionCmd = new SqlCommand("SELECT @@VERSION", connection);
                        var sqlVersion = versionCmd.ExecuteScalar()?.ToString() ?? "";

                        return new ErpInfo
                        {
                            ErpType = "Farmatic",
                            Version = "detected",
                            DatabaseType = "SQL Server",
                            DatabaseVersion = sqlVersion.Split('\n').FirstOrDefault(),
                            DatabaseName = "CGCOF",
                            DetectionMethod = "database",
                            DetectedTables = FarmaticTables.ToList(),
                            ConfidenceLevel = 100
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("  SQL Server {ConnStr}: {Error}", connStr.Substring(0, 30), ex.Message);
                }
            }

            return null;
        }

        private ErpInfo? TryDetectNixfarmaFromOracle()
        {
            var connectionStrings = new[]
            {
                "Data Source=localhost:1521/XE;User Id=system;Password=oracle;",
                "Data Source=localhost:1521/NIXFARMA;User Id=system;Password=oracle;",
                "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=XE)));User Id=system;Password=oracle;",
                "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=127.0.0.1)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=NIXFARMA)));User Id=system;Password=oracle;"
            };

            foreach (var connStr in connectionStrings)
            {
                try
                {
                    using var connection = new OracleConnection(connStr);
                    connection.Open();

                    // Verificar tablas características de Nixfarma
                    var tablesFound = 0;
                    foreach (var table in NixfarmaTables)
                    {
                        var query = $"SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :tableName";
                        using var cmd = new OracleCommand(query, connection);
                        cmd.Parameters.Add(new OracleParameter("tableName", table));
                        var count = Convert.ToInt32(cmd.ExecuteScalar());
                        if (count > 0) tablesFound++;
                    }

                    if (tablesFound >= 3)
                    {
                        _logger.Information("  Nixfarma detectado via Oracle ({Tables} tablas encontradas)", tablesFound);
                        
                        // Obtener versión de Oracle
                        using var versionCmd = new OracleCommand("SELECT * FROM V$VERSION WHERE ROWNUM = 1", connection);
                        var oracleVersion = versionCmd.ExecuteScalar()?.ToString() ?? "";

                        return new ErpInfo
                        {
                            ErpType = "Nixfarma",
                            Version = "detected",
                            DatabaseType = "Oracle",
                            DatabaseVersion = oracleVersion,
                            DatabaseName = "NIXFARMA",
                            DetectionMethod = "database",
                            DetectedTables = NixfarmaTables.ToList(),
                            ConfidenceLevel = 100
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("  Oracle {ConnStr}: {Error}", connStr.Substring(0, 30), ex.Message);
                }
            }

            return null;
        }

        private string GetVersionFromExe(string exePath)
        {
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(exePath);
                return versionInfo.FileVersion ?? versionInfo.ProductVersion ?? "unknown";
            }
            catch
            {
                return "unknown";
            }
        }
    }
}
