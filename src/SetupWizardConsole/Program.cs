using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;

namespace SetupWizardConsole
{
    class Program
    {
        private static readonly string InstallPath = @"C:\FarmacopilotAgent";
        
        static void Main(string[] args)
        {
            Console.Title = "Farmacopilot Agent - Instalador";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════╗
║                                                               ║
║        FARMACOPILOT AGENT - ASISTENTE DE INSTALACIÓN         ║
║                         v1.0.0                                ║
║                                                               ║
╚═══════════════════════════════════════════════════════════════╝
");
            Console.ResetColor();

            // Verificar administrador
            if (!IsRunningAsAdmin())
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] Este instalador debe ejecutarse como Administrador.");
                Console.WriteLine("        Haz clic derecho -> Ejecutar como administrador");
                Console.ResetColor();
                WaitForExit();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[✓] Ejecutando como Administrador");
            Console.ResetColor();
            Console.WriteLine();

            // Paso 1: Detectar ERP
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  PASO 1: DETECCIÓN DE ERP");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            var erpInfo = DetectErp();
            
            if (erpInfo == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] No se detectó ningún ERP compatible (Nixfarma o Farmatic)");
                Console.WriteLine("        Asegúrese de que el ERP esté instalado en este equipo.");
                Console.ResetColor();
                WaitForExit();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[✓] ERP detectado: {erpInfo.ErpType}");
            Console.WriteLine($"    Versión: {erpInfo.Version}");
            Console.WriteLine($"    Base de datos: {erpInfo.DbType}");
            if (!string.IsNullOrEmpty(erpInfo.InstallPath))
                Console.WriteLine($"    Ruta: {erpInfo.InstallPath}");
            Console.ResetColor();
            Console.WriteLine();

            // Paso 2: Configurar conexión BD
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  PASO 2: CONEXIÓN A BASE DE DATOS");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            string connectionString = BuildConnectionString(erpInfo);
            bool connectionOk = false;

            // Primero intentar sin credenciales (Windows Auth para SQL Server)
            if (erpInfo.DbType == "sqlserver")
            {
                Console.WriteLine("Probando conexión con autenticación de Windows...");
                connectionOk = TestSqlServerConnection(connectionString);
            }

            // Si no funciona, pedir credenciales
            if (!connectionOk)
            {
                Console.WriteLine();
                Console.WriteLine("Se requieren credenciales de base de datos:");
                Console.Write("  Usuario: ");
                string username = Console.ReadLine() ?? "";
                Console.Write("  Contraseña: ");
                string password = ReadPassword();
                Console.WriteLine();

                connectionString = BuildConnectionString(erpInfo, username, password);
                
                if (erpInfo.DbType == "sqlserver")
                    connectionOk = TestSqlServerConnection(connectionString);
                else
                    connectionOk = TestOracleConnection(connectionString);
            }

            if (!connectionOk)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] No se pudo conectar a la base de datos.");
                Console.WriteLine("        Verifique las credenciales e intente de nuevo.");
                Console.ResetColor();
                WaitForExit();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[✓] Conexión a base de datos exitosa");
            Console.ResetColor();
            Console.WriteLine();

            // Paso 3: Pedir ID de farmacia
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  PASO 3: IDENTIFICACIÓN DE FARMACIA");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();
            Console.Write("Ingrese el ID de farmacia (ej: FAR001): ");
            string farmaciaId = Console.ReadLine() ?? "FARMACIA_001";
            if (string.IsNullOrWhiteSpace(farmaciaId))
                farmaciaId = "FARMACIA_001";
            Console.WriteLine();

            // Paso 4: Crear configuración
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  PASO 4: INSTALACIÓN");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            try
            {
                // Crear carpetas
                Console.WriteLine("Creando carpetas...");
                Directory.CreateDirectory(InstallPath);
                Directory.CreateDirectory(Path.Combine(InstallPath, "logs"));
                Directory.CreateDirectory(Path.Combine(InstallPath, "staging"));
                Console.WriteLine($"  [✓] {InstallPath}");
                Console.WriteLine($"  [✓] {InstallPath}\\logs");
                Console.WriteLine($"  [✓] {InstallPath}\\staging");

                // Crear config.json
                Console.WriteLine();
                Console.WriteLine("Creando configuración...");
                var config = CreateConfig(farmaciaId, erpInfo, connectionString);
                var configJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(Path.Combine(InstallPath, "config.json"), configJson);
                Console.WriteLine($"  [✓] config.json creado");

                // Copiar Runner si existe en la misma carpeta
                string currentDir = AppDomain.CurrentDomain.BaseDirectory;
                string runnerSource = Path.Combine(currentDir, "FarmacopilotAgent.Runner.exe");
                string runnerDest = Path.Combine(InstallPath, "FarmacopilotAgent.Runner.exe");
                
                if (File.Exists(runnerSource))
                {
                    File.Copy(runnerSource, runnerDest, true);
                    Console.WriteLine($"  [✓] FarmacopilotAgent.Runner.exe copiado");
                }

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("╔═══════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║                                                               ║");
                Console.WriteLine("║              ¡INSTALACIÓN COMPLETADA!                        ║");
                Console.WriteLine("║                                                               ║");
                Console.WriteLine("╚═══════════════════════════════════════════════════════════════╝");
                Console.ResetColor();
                Console.WriteLine();
                Console.WriteLine($"Configuración guardada en: {InstallPath}\\config.json");
                Console.WriteLine();
                Console.WriteLine("Próximo paso: Ejecutar FarmacopilotAgent.Runner.exe para");
                Console.WriteLine("              exportar los datos a SharePoint.");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {ex.Message}");
                Console.ResetColor();
            }

            WaitForExit();
        }

        static ErpInfo? DetectErp()
        {
            Console.WriteLine("Buscando ERPs instalados...");
            Console.WriteLine();

            // === DETECTAR FARMATIC ===
            Console.WriteLine("  Buscando Farmatic...");
            
            // Registry paths para Farmatic
            string[] farmaticPaths = {
                @"SOFTWARE\Consoft\Farmatic",
                @"SOFTWARE\WOW6432Node\Consoft\Farmatic",
                @"SOFTWARE\Farmatic",
                @"SOFTWARE\WOW6432Node\Farmatic"
            };

            foreach (var path in farmaticPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key != null)
                    {
                        Console.WriteLine($"    [✓] Registry: {path}");
                        return new ErpInfo
                        {
                            ErpType = "Farmatic",
                            Version = key.GetValue("Version")?.ToString() ?? "16.00",
                            DbType = "sqlserver",
                            InstallPath = key.GetValue("InstallPath")?.ToString() ?? "",
                            DetectionMethod = "registry"
                        };
                    }
                }
                catch { }
            }

            // Buscar SQL Server con base de datos CGCOF
            Console.WriteLine("    Buscando SQL Server con BD Farmatic...");
            var sqlInstances = GetSqlServerInstances();
            foreach (var instance in sqlInstances)
            {
                try
                {
                    var connStr = $"Server={instance};Database=CGCOF;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=5;";
                    using var conn = new SqlConnection(connStr);
                    conn.Open();
                    
                    // Verificar tabla característica de Farmatic
                    using var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ventas'", conn);
                    var count = (int)cmd.ExecuteScalar();
                    if (count > 0)
                    {
                        Console.WriteLine($"    [✓] SQL Server: {instance}/CGCOF");
                        return new ErpInfo
                        {
                            ErpType = "Farmatic",
                            Version = "16.00",
                            DbType = "sqlserver",
                            DbInstance = instance,
                            DbName = "CGCOF",
                            DetectionMethod = "database"
                        };
                    }
                }
                catch { }
            }

            // === DETECTAR NIXFARMA ===
            Console.WriteLine("  Buscando Nixfarma...");
            
            string[] nixfarmaPaths = {
                @"SOFTWARE\Pulso Informatica\Nixfarma",
                @"SOFTWARE\WOW6432Node\Pulso Informatica\Nixfarma",
                @"SOFTWARE\Nixfarma",
                @"SOFTWARE\WOW6432Node\Nixfarma"
            };

            foreach (var path in nixfarmaPaths)
            {
                try
                {
                    using var key = Registry.LocalMachine.OpenSubKey(path);
                    if (key != null)
                    {
                        Console.WriteLine($"    [✓] Registry: {path}");
                        return new ErpInfo
                        {
                            ErpType = "Nixfarma",
                            Version = key.GetValue("Version")?.ToString() ?? "9.1",
                            DbType = "oracle",
                            InstallPath = key.GetValue("InstallPath")?.ToString() ?? "",
                            DetectionMethod = "registry"
                        };
                    }
                }
                catch { }
            }

            // Buscar Oracle con tablas de Nixfarma
            Console.WriteLine("    Buscando Oracle con BD Nixfarma...");
            try
            {
                var oracleConnStr = "Data Source=NIXFARMA;User Id=/;";
                using var conn = new OracleConnection(oracleConnStr);
                conn.Open();
                
                using var cmd = new OracleCommand("SELECT COUNT(*) FROM USER_TABLES WHERE TABLE_NAME = 'AH_VENTAS'", conn);
                var count = Convert.ToInt32(cmd.ExecuteScalar());
                if (count > 0)
                {
                    Console.WriteLine($"    [✓] Oracle: NIXFARMA");
                    return new ErpInfo
                    {
                        ErpType = "Nixfarma",
                        Version = "9.1",
                        DbType = "oracle",
                        DetectionMethod = "database"
                    };
                }
            }
            catch { }

            // Buscar archivos en rutas conocidas
            Console.WriteLine("  Buscando en rutas comunes...");
            string[] farmaticFilePaths = {
                @"C:\Program Files\Consoft\Farmatic",
                @"C:\Program Files (x86)\Consoft\Farmatic",
                @"C:\Farmatic"
            };
            foreach (var p in farmaticFilePaths)
            {
                if (Directory.Exists(p))
                {
                    Console.WriteLine($"    [✓] Carpeta: {p}");
                    return new ErpInfo
                    {
                        ErpType = "Farmatic",
                        Version = "16.00",
                        DbType = "sqlserver",
                        InstallPath = p,
                        DetectionMethod = "filesystem"
                    };
                }
            }

            string[] nixfarmaFilePaths = {
                @"C:\Program Files\Pulso Informatica\Nixfarma",
                @"C:\Program Files (x86)\Pulso Informatica\Nixfarma",
                @"C:\Nixfarma"
            };
            foreach (var p in nixfarmaFilePaths)
            {
                if (Directory.Exists(p))
                {
                    Console.WriteLine($"    [✓] Carpeta: {p}");
                    return new ErpInfo
                    {
                        ErpType = "Nixfarma",
                        Version = "9.1",
                        DbType = "oracle",
                        InstallPath = p,
                        DetectionMethod = "filesystem"
                    };
                }
            }

            Console.WriteLine("    [✗] No se encontró ningún ERP");
            return null;
        }

        static List<string> GetSqlServerInstances()
        {
            var instances = new List<string> { "localhost", ".", "(local)", "localhost\\SQLEXPRESS", ".\\SQLEXPRESS" };
            
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        if (name.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase))
                            instances.Insert(0, "localhost");
                        else
                            instances.Insert(0, $"localhost\\{name}");
                    }
                }
            }
            catch { }

            return instances.Distinct().ToList();
        }

        static string BuildConnectionString(ErpInfo erp, string? username = null, string? password = null)
        {
            if (erp.DbType == "sqlserver")
            {
                var server = erp.DbInstance ?? "localhost";
                var database = erp.DbName ?? "CGCOF";
                
                if (string.IsNullOrEmpty(username))
                    return $"Server={server};Database={database};Integrated Security=true;TrustServerCertificate=true;";
                else
                    return $"Server={server};Database={database};User Id={username};Password={password};TrustServerCertificate=true;";
            }
            else // Oracle
            {
                var dataSource = erp.DbName ?? "NIXFARMA";
                if (string.IsNullOrEmpty(username))
                    return $"Data Source={dataSource};User Id=/;";
                else
                    return $"Data Source={dataSource};User Id={username};Password={password};";
            }
        }

        static bool TestSqlServerConnection(string connectionString)
        {
            try
            {
                using var conn = new SqlConnection(connectionString + ";Connection Timeout=5;");
                conn.Open();
                using var cmd = new SqlCommand("SELECT 1", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    [✗] Error: {ex.Message}");
                return false;
            }
        }

        static bool TestOracleConnection(string connectionString)
        {
            try
            {
                using var conn = new OracleConnection(connectionString);
                conn.Open();
                using var cmd = new OracleCommand("SELECT 1 FROM DUAL", conn);
                cmd.ExecuteScalar();
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    [✗] Error: {ex.Message}");
                return false;
            }
        }

        static Dictionary<string, object> CreateConfig(string farmaciaId, ErpInfo erp, string connectionString)
        {
            var tables = erp.ErpType == "Farmatic" 
                ? new List<object>
                {
                    new { TableName = "ventas", IncrementalColumn = "fecha", Enabled = true, Priority = 10 },
                    new { TableName = "linea venta", IncrementalColumn = "fecha", Enabled = true, Priority = 10 },
                    new { TableName = "articu", IncrementalColumn = (string?)null, Enabled = true, Priority = 50 },
                    new { TableName = "proveedor", IncrementalColumn = (string?)null, Enabled = true, Priority = 60 },
                    new { TableName = "recep", IncrementalColumn = (string?)null, Enabled = true, Priority = 40 },
                    new { TableName = "vendedor", IncrementalColumn = (string?)null, Enabled = true, Priority = 70 },
                    new { TableName = "albaran dev", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 },
                    new { TableName = "coste venta", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 },
                    new { TableName = "linea albaran", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 },
                    new { TableName = "venta aux", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 },
                    new { TableName = "vlab", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 },
                    new { TableName = "esarti", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 }
                }
                : new List<object>
                {
                    new { TableName = "AH_VENTAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 10 },
                    new { TableName = "AH_VENTA_LINEAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 10 },
                    new { TableName = "AB_ARTICULOS_FICHA_E", IncrementalColumn = (string?)null, Enabled = true, Priority = 50 },
                    new { TableName = "AB_LABORATORIOS", IncrementalColumn = (string?)null, Enabled = true, Priority = 60 },
                    new { TableName = "AD_PROVEEDORES", IncrementalColumn = (string?)null, Enabled = true, Priority = 60 },
                    new { TableName = "AB_FAMILIAS", IncrementalColumn = (string?)null, Enabled = true, Priority = 70 },
                    new { TableName = "AD_PED_DEV", IncrementalColumn = "FECHA", Enabled = true, Priority = 70 },
                    new { TableName = "AE_ENTIDADES", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 },
                    new { TableName = "AE_TIPOS", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 },
                    new { TableName = "AB_DOM_VALOR_BD", IncrementalColumn = (string?)null, Enabled = true, Priority = 80 }
                };

            return new Dictionary<string, object>
            {
                ["FarmaciaId"] = farmaciaId,
                ["ErpType"] = erp.ErpType.ToLower(),
                ["ErpVersion"] = erp.Version,
                ["DbType"] = erp.DbType,
                ["DbConnectionEncrypted"] = connectionString,
                ["PostgresConnectionEncrypted"] = "",
                ["SharePointSiteId"] = "d14f0b31-c267-4493-82ea-02447a8cc665",
                ["TablesToExport"] = tables,
                ["ExportSchedule"] = "03:00",
                ["LastInstallTs"] = DateTime.UtcNow.ToString("o"),
                ["AgentVersion"] = "1.0.0"
            };
        }

        static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        static string ReadPassword()
        {
            var password = "";
            ConsoleKeyInfo key;
            do
            {
                key = Console.ReadKey(true);
                if (key.Key != ConsoleKey.Enter && key.Key != ConsoleKey.Backspace)
                {
                    password += key.KeyChar;
                    Console.Write("*");
                }
                else if (key.Key == ConsoleKey.Backspace && password.Length > 0)
                {
                    password = password[..^1];
                    Console.Write("\b \b");
                }
            } while (key.Key != ConsoleKey.Enter);
            return password;
        }

        static void WaitForExit()
        {
            Console.WriteLine();
            Console.WriteLine("Presione cualquier tecla para salir...");
            Console.ReadKey();
        }
    }

    class ErpInfo
    {
        public string ErpType { get; set; } = "";
        public string Version { get; set; } = "";
        public string DbType { get; set; } = "";
        public string InstallPath { get; set; } = "";
        public string DetectionMethod { get; set; } = "";
        public string? DbInstance { get; set; }
        public string? DbName { get; set; }
    }
}