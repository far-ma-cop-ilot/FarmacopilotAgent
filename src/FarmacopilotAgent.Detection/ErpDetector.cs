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
            // Detectar instancias SQL Server dinámicamente desde Registry
            var instances = GetSqlServerInstances();
            var databases = new[] { "CGCOF", "Farmatic", "FARMATIC", "farmatic" };
            
            foreach (var instance in instances)
            {
                foreach (var database in databases)
                {
                    var connStr = $@"Server={instance};Database={database};Integrated Security=true;TrustServerCertificate=true;Connection Timeout=5;";
                    
                    try
                    {
                        using var connection = new SqlConnection(connStr);
                        connection.Open();

                        var tablesFound = 0;
                        foreach (var table in FarmaticTables)
                        {
                            var query = "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = @table";
                            using var cmd = new SqlCommand(query, connection);
                            cmd.Parameters.AddWithValue("@table", table);
                            var count = (int)cmd.ExecuteScalar();
                            if (count > 0) tablesFound++;
                        }

                        if (tablesFound >= 3)
                        {
                            _logger.Information("  Farmatic detectado via SQL Server ({Tables} tablas encontradas)", tablesFound);
                            _logger.Information("  Instancia: {Instance}, Base de datos: {Database}", instance, database);
                            
                            using var versionCmd = new SqlCommand("SELECT @@VERSION", connection);
                            var sqlVersion = versionCmd.ExecuteScalar()?.ToString() ?? "";

                            return new ErpInfo
                            {
                                ErpType = "Farmatic",
                                Version = "detected",
                                DatabaseType = "SQL Server",
                                DatabaseVersion = sqlVersion.Split('\n').FirstOrDefault(),
                                DatabaseName = database,
                                DatabaseInstance = instance,
                                DetectionMethod = "database",
                                DetectedTables = FarmaticTables.ToList(),
                                ConfidenceLevel = 100,
                                AdditionalInfo = new Dictionary<string, string>
                                {
                                    { "ConnectionString", connStr.Replace("Integrated Security=true", "***") }
                                }
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug("  SQL Server {Instance}/{Db}: {Error}", instance, database, ex.Message);
                    }
                }
            }

            return null;
        }

        private List<string> GetSqlServerInstances()
        {
            var instances = new List<string>
            {
                "localhost",
                ".",
                "(local)",
                "127.0.0.1"
            };

            try
            {
                // Detectar instancias desde Registry
                var registryPaths = new[]
                {
                    @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL",
                    @"SOFTWARE\WOW6432Node\Microsoft\Microsoft SQL Server\Instance Names\SQL"
                };

                foreach (var path in registryPaths)
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key != null)
                    {
                        foreach (var instanceName in key.GetValueNames())
                        {
                            if (instanceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                            {
                                instances.Add("localhost");
                            }
                            else
                            {
                                instances.Add($@"localhost\{instanceName}");
                                instances.Add($@".\{instanceName}");
                            }
                            _logger.Debug("  Instancia SQL Server detectada: {Instance}", instanceName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Debug("  Error leyendo instancias SQL Server desde Registry: {Error}", ex.Message);
            }

            // Agregar instancias comunes conocidas
            var commonInstances = new[] { "SQLEXPRESS", "FARMATIC", "CGCOF", "SQL2019", "SQL2017" };
            foreach (var inst in commonInstances)
            {
                instances.Add($@"localhost\{inst}");
                instances.Add($@".\{inst}");
            }

            return instances.Distinct().ToList();
        }

        private ErpInfo? TryDetectNixfarmaFromOracle()
        {
            // Detectar configuración Oracle dinámicamente desde tnsnames.ora
            var connectionStrings = GetOracleConnectionStrings();

            foreach (var connInfo in connectionStrings)
            {
                try
                {
                    using var connection = new OracleConnection(connInfo.ConnectionString);
                    connection.Open();

                    var tablesFound = 0;
                    foreach (var table in NixfarmaTables)
                    {
                        var query = "SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = :tableName";
                        using var cmd = new OracleCommand(query, connection);
                        cmd.Parameters.Add(new OracleParameter("tableName", table));
                        var count = Convert.ToInt32(cmd.ExecuteScalar());
                        if (count > 0) tablesFound++;
                    }

                    if (tablesFound >= 3)
                    {
                        _logger.Information("  Nixfarma detectado via Oracle ({Tables} tablas encontradas)", tablesFound);
                        _logger.Information("  TNS: {TnsName}, Host: {Host}:{Port}", connInfo.TnsName, connInfo.Host, connInfo.Port);
                        
                        string oracleVersion = "";
                        try
                        {
                            using var versionCmd = new OracleCommand("SELECT BANNER FROM V$VERSION WHERE ROWNUM = 1", connection);
                            oracleVersion = versionCmd.ExecuteScalar()?.ToString() ?? "";
                        }
                        catch
                        {
                            oracleVersion = "Oracle (version query failed)";
                        }

                        return new ErpInfo
                        {
                            ErpType = "Nixfarma",
                            Version = "detected",
                            DatabaseType = "Oracle",
                            DatabaseVersion = oracleVersion,
                            DatabaseName = connInfo.ServiceName,
                            DatabaseInstance = connInfo.TnsName,
                            DetectionMethod = "database",
                            DetectedTables = NixfarmaTables.ToList(),
                            ConfidenceLevel = 100,
                            AdditionalInfo = new Dictionary<string, string>
                            {
                                { "Host", connInfo.Host },
                                { "Port", connInfo.Port.ToString() },
                                { "ServiceName", connInfo.ServiceName },
                                { "TnsName", connInfo.TnsName }
                            }
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug("  Oracle {TnsName}: {Error}", connInfo.TnsName, ex.Message);
                }
            }

            return null;
        }

        private class OracleConnectionInfo
        {
            public string ConnectionString { get; set; } = "";
            public string TnsName { get; set; } = "";
            public string Host { get; set; } = "localhost";
            public int Port { get; set; } = 1521;
            public string ServiceName { get; set; } = "";
        }

        private List<OracleConnectionInfo> GetOracleConnectionStrings()
        {
            var connections = new List<OracleConnectionInfo>();

            // 1. Buscar tnsnames.ora en ubicaciones conocidas
            var tnsLocations = GetTnsNamesLocations();
            
            foreach (var tnsPath in tnsLocations)
            {
                if (File.Exists(tnsPath))
                {
                    _logger.Debug("  Encontrado tnsnames.ora: {Path}", tnsPath);
                    var tnsEntries = ParseTnsNames(tnsPath);
                    connections.AddRange(tnsEntries);
                }
            }

            // 2. Agregar conexiones por defecto si no se encontró tnsnames.ora
            if (connections.Count == 0)
            {
                _logger.Debug("  No se encontró tnsnames.ora, usando conexiones por defecto");
                
                var defaultServices = new[] { "XE", "ORCL", "NIXFARMA", "NIX", "FARMA" };
                var defaultPorts = new[] { 1521, 1522, 1526 };
                var defaultHosts = new[] { "localhost", "127.0.0.1" };

                foreach (var host in defaultHosts)
                {
                    foreach (var port in defaultPorts)
                    {
                        foreach (var service in defaultServices)
                        {
                            connections.Add(new OracleConnectionInfo
                            {
                                TnsName = $"{service}_{host}_{port}",
                                Host = host,
                                Port = port,
                                ServiceName = service,
                                ConnectionString = $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={host})(PORT={port}))(CONNECT_DATA=(SERVICE_NAME={service})));User Id=/;DBA Privilege=SYSDBA;"
                            });
                        }
                    }
                }
            }

            // 3. Agregar variantes con credenciales comunes para cada conexión
            var connectionsWithCreds = new List<OracleConnectionInfo>();
            var credentials = new[]
            {
                ("", "", true),  // OS Authentication
                ("system", "oracle", false),
                ("system", "manager", false),
                ("nixfarma", "nixfarma", false),
                ("nix", "nix", false),
                ("farma", "farma", false)
            };

            foreach (var conn in connections)
            {
                foreach (var (user, pass, osAuth) in credentials)
                {
                    var dataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={conn.Host})(PORT={conn.Port}))(CONNECT_DATA=(SERVICE_NAME={conn.ServiceName})))";
                    
                    string connStr;
                    if (osAuth)
                    {
                        connStr = $"Data Source={dataSource};User Id=/;";
                    }
                    else
                    {
                        connStr = $"Data Source={dataSource};User Id={user};Password={pass};";
                    }

                    connectionsWithCreds.Add(new OracleConnectionInfo
                    {
                        TnsName = conn.TnsName,
                        Host = conn.Host,
                        Port = conn.Port,
                        ServiceName = conn.ServiceName,
                        ConnectionString = connStr
                    });
                }
            }

            return connectionsWithCreds;
        }

        private List<string> GetTnsNamesLocations()
        {
            var locations = new List<string>();

            // Variable de entorno TNS_ADMIN
            var tnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN");
            if (!string.IsNullOrEmpty(tnsAdmin))
            {
                locations.Add(Path.Combine(tnsAdmin, "tnsnames.ora"));
            }

            // Variable de entorno ORACLE_HOME
            var oracleHome = Environment.GetEnvironmentVariable("ORACLE_HOME");
            if (!string.IsNullOrEmpty(oracleHome))
            {
                locations.Add(Path.Combine(oracleHome, "network", "admin", "tnsnames.ora"));
            }

            // Buscar ORACLE_HOME desde Registry
            var registryPaths = new[]
            {
                @"SOFTWARE\Oracle\KEY_OraClient11g_home1",
                @"SOFTWARE\WOW6432Node\Oracle\KEY_OraClient11g_home1",
                @"SOFTWARE\Oracle\KEY_OraDb11g_home1",
                @"SOFTWARE\WOW6432Node\Oracle\KEY_OraDb11g_home1",
                @"SOFTWARE\Oracle",
                @"SOFTWARE\WOW6432Node\Oracle"
            };

            foreach (var regPath in registryPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(regPath);
                    if (key != null)
                    {
                        var home = key.GetValue("ORACLE_HOME") as string;
                        if (!string.IsNullOrEmpty(home))
                        {
                            locations.Add(Path.Combine(home, "network", "admin", "tnsnames.ora"));
                        }
                    }
                }
                catch { }
            }

            // Rutas comunes hardcodeadas
            var commonPaths = new[]
            {
                @"C:\oracle\product\11.2.0\client_1\network\admin\tnsnames.ora",
                @"C:\oracle\product\11.2.0\dbhome_1\network\admin\tnsnames.ora",
                @"C:\oracle\product\12.2.0\client_1\network\admin\tnsnames.ora",
                @"C:\oracle\product\12.2.0\dbhome_1\network\admin\tnsnames.ora",
                @"C:\app\oracle\product\11.2.0\client_1\network\admin\tnsnames.ora",
                @"C:\app\oracle\product\12.2.0\client_1\network\admin\tnsnames.ora",
                @"C:\oraclexe\app\oracle\product\11.2.0\server\network\admin\tnsnames.ora",
                @"C:\Program Files\Oracle\network\admin\tnsnames.ora",
                @"C:\Program Files (x86)\Oracle\network\admin\tnsnames.ora",
                @"D:\oracle\product\11.2.0\client_1\network\admin\tnsnames.ora"
            };

            locations.AddRange(commonPaths);

            return locations.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        }

        private List<OracleConnectionInfo> ParseTnsNames(string tnsPath)
        {
            var entries = new List<OracleConnectionInfo>();

            try
            {
                var content = File.ReadAllText(tnsPath);
                
                // Regex para parsear entradas TNS
                var pattern = @"(\w+)\s*=\s*\(DESCRIPTION\s*=.*?HOST\s*=\s*([^\)]+)\).*?PORT\s*=\s*(\d+).*?SERVICE_NAME\s*=\s*([^\)]+)\)";
                var matches = System.Text.RegularExpressions.Regex.Matches(
                    content, 
                    pattern, 
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);

                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    var tnsName = match.Groups[1].Value.Trim();
                    var host = match.Groups[2].Value.Trim();
                    var port = int.Parse(match.Groups[3].Value.Trim());
                    var serviceName = match.Groups[4].Value.Trim();

                    _logger.Debug("  TNS Entry: {TnsName} -> {Host}:{Port}/{ServiceName}", tnsName, host, port, serviceName);

                    entries.Add(new OracleConnectionInfo
                    {
                        TnsName = tnsName,
                        Host = host,
                        Port = port,
                        ServiceName = serviceName,
                        ConnectionString = $"Data Source={tnsName};"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("  Error parseando tnsnames.ora: {Error}", ex.Message);
            }

            return entries;
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
