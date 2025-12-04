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
            if (!string.IsNullOrEmpty(erpInfo.DbSchema))
                Console.WriteLine($"    Esquema BD: {erpInfo.DbSchema}");
            Console.ResetColor();
            Console.WriteLine();

            // Paso 2: Configurar conexión BD
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine("  PASO 2: CONEXIÓN A BASE DE DATOS");
            Console.WriteLine("═══════════════════════════════════════════════════════════════");
            Console.WriteLine();

            string connectionString = "";
            bool connectionOk = false;

            if (erpInfo.DbType == "sqlserver")
            {
                // === SQL SERVER (FARMATIC) ===
                string[] possibleDbNames = { "Farmatic", "FARMATIC", "CGCOF", "cgcof" };
                
                // Primero intentar Windows Auth
                Console.WriteLine("Probando conexión con autenticación de Windows...");
                foreach (var dbName in possibleDbNames)
                {
                    try
                    {
                        var testConn = $"Server={erpInfo.DbInstance ?? "localhost"};Database={dbName};Integrated Security=true;TrustServerCertificate=true;Connection Timeout=3;";
                        using var conn = new SqlConnection(testConn);
                        conn.Open();
                        erpInfo.DbName = dbName;
                        connectionString = testConn;
                        connectionOk = true;
                        Console.WriteLine($"  [✓] Conectado a: {dbName}");
                        break;
                    }
                    catch { }
                }
                
                // Si no funciona, pedir credenciales
                if (!connectionOk)
                {
                    Console.WriteLine("  [!] Windows Auth no disponible");
                    Console.WriteLine();
                    Console.WriteLine("Se requieren credenciales de base de datos:");
                    Console.Write("  Usuario: ");
                    string username = Console.ReadLine() ?? "";
                    Console.Write("  Contraseña: ");
                    string password = ReadPassword();
                    Console.WriteLine();

                    Console.WriteLine("Buscando base de datos...");
                    foreach (var dbName in possibleDbNames)
                    {
                        try
                        {
                            var testConn = $"Server={erpInfo.DbInstance ?? "localhost"};Database={dbName};User Id={username};Password={password};TrustServerCertificate=true;Connection Timeout=3;";
                            connectionOk = TestSqlServerConnection(testConn);
                            if (connectionOk)
                            {
                                erpInfo.DbName = dbName;
                                connectionString = testConn;
                                Console.WriteLine($"  [✓] Base de datos encontrada: {dbName}");
                                break;
                            }
                        }
                        catch { }
                    }
                }
            }
            else if (erpInfo.DbType == "oracle")
            {
                // === ORACLE (NIXFARMA) ===
                Console.WriteLine("Configurando conexión Oracle...");
                Console.WriteLine();
                
                // Intentar primero con credenciales conocidas de Nixfarma
                string[] defaultUsers = { "consu", "nixfarma", "nix", "system" };
                string[] defaultPasswords = { "consu", "nixfarma", "nix", "manager" };
                string[] tnsNames = { "NIXFARMA", "NIX", "XE", "ORCL" };
                
                // Si ya detectamos el TNS, usarlo primero
                if (!string.IsNullOrEmpty(erpInfo.TnsName))
                {
                    tnsNames = new[] { erpInfo.TnsName }.Concat(tnsNames.Where(t => t != erpInfo.TnsName)).ToArray();
                }
                
                Console.WriteLine("Probando conexiones conocidas de Nixfarma...");
                
                foreach (var tns in tnsNames)
                {
                    foreach (var user in defaultUsers)
                    {
                        var pass = defaultPasswords[Array.IndexOf(defaultUsers, user)];
                        try
                        {
                            var testConn = $"Data Source={tns};User Id={user};Password={pass};";
                            Console.Write($"  Probando {user}@{tns}... ");
                            
                            if (TestOracleConnectionAndSchema(testConn, out string? detectedSchema))
                            {
                                connectionString = testConn;
                                connectionOk = true;
                                erpInfo.DbSchema = detectedSchema ?? "APPUL";
                                erpInfo.TnsName = tns;
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"[✓] Conectado! Esquema: {erpInfo.DbSchema}");
                                Console.ResetColor();
                                break;
                            }
                            else
                            {
                                Console.WriteLine("[✗]");
                            }
                        }
                        catch
                        {
                            Console.WriteLine("[✗]");
                        }
                    }
                    if (connectionOk) break;
                }
                
                // Si no funciona, pedir credenciales manualmente
                if (!connectionOk)
                {
                    Console.WriteLine();
                    Console.WriteLine("  [!] No se pudo conectar automáticamente");
                    Console.WriteLine();
                    Console.WriteLine("Se requieren credenciales de Oracle:");
                    Console.Write("  TNS Name (ej: NIXFARMA): ");
                    string tnsName = Console.ReadLine() ?? "NIXFARMA";
                    Console.Write("  Usuario: ");
                    string username = Console.ReadLine() ?? "";
                    Console.Write("  Contraseña: ");
                    string password = ReadPassword();
                    Console.WriteLine();

                    connectionString = $"Data Source={tnsName};User Id={username};Password={password};";
                    
                    if (TestOracleConnectionAndSchema(connectionString, out string? schema))
                    {
                        connectionOk = true;
                        erpInfo.DbSchema = schema ?? "APPUL";
                        erpInfo.TnsName = tnsName;
                        Console.WriteLine($"  [✓] Conectado! Esquema: {erpInfo.DbSchema}");
                    }
                }
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
                Console.WriteLine($"ERP: {erpInfo.ErpType} ({erpInfo.DbType})");
                if (erpInfo.DbType == "oracle")
                    Console.WriteLine($"Esquema: {erpInfo.DbSchema}");
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

            // === DETECTAR NIXFARMA PRIMERO (Oracle) ===
            Console.WriteLine("  Buscando Nixfarma (Oracle)...");
            
            // Registry paths para Nixfarma
            string[] nixfarmaPaths = {
                @"SOFTWARE\Pulso Informatica\Nixfarma",
                @"SOFTWARE\WOW6432Node\Pulso Informatica\Nixfarma",
                @"SOFTWARE\Pulso\Nixfarma",
                @"SOFTWARE\WOW6432Node\Pulso\Nixfarma",
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
                            DetectionMethod = "registry",
                            DbSchema = "APPUL"
                        };
                    }
                }
                catch { }
            }

            // Buscar carpetas de Nixfarma
            string[] nixfarmaFilePaths = {
                @"C:\Nixfarma",
                @"C:\Program Files\Nixfarma",
                @"C:\Program Files (x86)\Nixfarma",
                @"C:\Program Files\Pulso Informatica\Nixfarma",
                @"C:\Program Files (x86)\Pulso Informatica\Nixfarma",
                @"D:\Nixfarma"
            };
            
            foreach (var p in nixfarmaFilePaths)
            {
                if (Directory.Exists(p))
                {
                    Console.WriteLine($"    [✓] Carpeta encontrada: {p}");
                    return new ErpInfo
                    {
                        ErpType = "Nixfarma",
                        Version = "9.1",
                        DbType = "oracle",
                        InstallPath = p,
                        DetectionMethod = "filesystem",
                        DbSchema = "APPUL"
                    };
                }
            }

            // Buscar tnsnames.ora para Oracle
            Console.WriteLine("    Buscando configuración Oracle (tnsnames.ora)...");
            var tnsLocations = GetTnsNamesLocations();
            foreach (var tnsPath in tnsLocations)
            {
                if (File.Exists(tnsPath))
                {
                    Console.WriteLine($"    [✓] tnsnames.ora encontrado: {tnsPath}");
                    var tnsName = ParseTnsNamesForNixfarma(tnsPath);
                    if (!string.IsNullOrEmpty(tnsName))
                    {
                        Console.WriteLine($"    [✓] TNS Entry NIXFARMA encontrado: {tnsName}");
                        return new ErpInfo
                        {
                            ErpType = "Nixfarma",
                            Version = "9.1",
                            DbType = "oracle",
                            DetectionMethod = "tnsnames",
                            TnsName = tnsName,
                            DbSchema = "APPUL"
                        };
                    }
                }
            }

            // Intentar conexión directa a Oracle NIXFARMA
            Console.WriteLine("    Probando conexión directa a Oracle NIXFARMA...");
            try
            {
                var testConn = "Data Source=NIXFARMA;User Id=consu;Password=consu;";
                if (TestOracleConnectionAndSchema(testConn, out string? schema))
                {
                    Console.WriteLine($"    [✓] Oracle NIXFARMA accesible (esquema: {schema})");
                    return new ErpInfo
                    {
                        ErpType = "Nixfarma",
                        Version = "9.1",
                        DbType = "oracle",
                        DetectionMethod = "database",
                        TnsName = "NIXFARMA",
                        DbSchema = schema ?? "APPUL"
                    };
                }
            }
            catch { }

            // === DETECTAR FARMATIC (SQL Server) ===
            Console.WriteLine("  Buscando Farmatic (SQL Server)...");
            
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

            // Buscar SQL Server con base de datos Farmatic
            Console.WriteLine("    Buscando SQL Server con BD Farmatic...");
            var sqlInstances = GetSqlServerInstances();
            string[] possibleDbNames = { "Farmatic", "FARMATIC", "CGCOF", "cgcof" };
            
            foreach (var instance in sqlInstances)
            {
                foreach (var dbName in possibleDbNames)
                {
                    try
                    {
                        var connStr = $"Server={instance};Database={dbName};Integrated Security=true;TrustServerCertificate=true;Connection Timeout=3;";
                        using var conn = new SqlConnection(connStr);
                        conn.Open();
                        
                        using var cmd = new SqlCommand("SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN ('Venta', 'ventas', 'Articu', 'articu')", conn);
                        var count = (int)cmd.ExecuteScalar();
                        if (count > 0)
                        {
                            Console.WriteLine($"    [✓] SQL Server: {instance}/{dbName}");
                            return new ErpInfo
                            {
                                ErpType = "Farmatic",
                                Version = "16.00",
                                DbType = "sqlserver",
                                DbInstance = instance,
                                DbName = dbName,
                                DetectionMethod = "database"
                            };
                        }
                    }
                    catch { }
                }
            }

            // Buscar carpetas de Farmatic
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

            Console.WriteLine("    [✗] No se encontró ningún ERP");
            return null;
        }

        static List<string> GetTnsNamesLocations()
        {
            var locations = new List<string>();

            // Variable de entorno TNS_ADMIN
            var tnsAdmin = Environment.GetEnvironmentVariable("TNS_ADMIN");
            if (!string.IsNullOrEmpty(tnsAdmin))
                locations.Add(Path.Combine(tnsAdmin, "tnsnames.ora"));

            // Variable ORACLE_HOME
            var oracleHome = Environment.GetEnvironmentVariable("ORACLE_HOME");
            if (!string.IsNullOrEmpty(oracleHome))
                locations.Add(Path.Combine(oracleHome, "network", "admin", "tnsnames.ora"));

            // Rutas comunes de Oracle
            string[] commonPaths = {
                @"C:\Oracle\Ora11\NETWORK\ADMIN\tnsnames.ora",
                @"C:\Oracle\Ora11Cli32\NETWORK\ADMIN\tnsnames.ora",
                @"C:\orant\NET80\ADMIN\tnsnames.ora",
                @"C:\oracle\product\11.2.0\client_1\network\admin\tnsnames.ora",
                @"C:\oracle\product\11.2.0\dbhome_1\network\admin\tnsnames.ora",
                @"C:\oracle\product\12.2.0\client_1\network\admin\tnsnames.ora",
                @"C:\app\oracle\product\11.2.0\client_1\network\admin\tnsnames.ora",
                @"C:\oraclexe\app\oracle\product\11.2.0\server\network\admin\tnsnames.ora"
            };
            locations.AddRange(commonPaths);

            return locations.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToList();
        }

        static string? ParseTnsNamesForNixfarma(string tnsPath)
        {
            try
            {
                var content = File.ReadAllText(tnsPath).ToUpper();
                
                // Buscar entradas relacionadas con Nixfarma
                string[] searchTerms = { "NIXFARMA", "NIX", "FARMA" };
                
                foreach (var term in searchTerms)
                {
                    if (content.Contains(term))
                    {
                        // Buscar el nombre del TNS entry
                        var lines = content.Split('\n');
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (trimmed.StartsWith(term) && trimmed.Contains("="))
                            {
                                return term;
                            }
                        }
                        // Si no encontramos la línea exacta pero existe el término
                        return term == "NIXFARMA" ? "NIXFARMA" : null;
                    }
                }
            }
            catch { }
            
            return null;
        }

        static bool TestOracleConnectionAndSchema(string connectionString, out string? detectedSchema)
        {
            detectedSchema = null;
            try
            {
                using var conn = new OracleConnection(connectionString);
                conn.Open();
                
                // Buscar el esquema que contiene tablas de Nixfarma
                using var cmd = new OracleCommand(@"
                    SELECT DISTINCT OWNER 
                    FROM ALL_TABLES 
                    WHERE TABLE_NAME IN ('AH_VENTAS', 'AH_VENTA_LINEAS', 'AB_ARTICULOS')
                    AND OWNER NOT IN ('SYS', 'SYSTEM')
                    AND ROWNUM = 1", conn);
                
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    detectedSchema = result.ToString();
                    return true;
                }
                
                // Si no encuentra las tablas, al menos la conexión funciona
                // Asumir APPUL como esquema por defecto
                detectedSchema = "APPUL";
                return true;
            }
            catch
            {
                return false;
            }
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
            catch
            {
                return false;
            }
        }

        static Dictionary<string, object> CreateConfig(string farmaciaId, ErpInfo erp, string connectionString)
        {
            List<object> tables;
            
            if (erp.ErpType == "Nixfarma")
            {
                // Tablas Nixfarma con esquema APPUL
                var schema = erp.DbSchema ?? "APPUL";
                tables = new List<object>
                {
                    new { TableName = $"{schema}.AH_VENTAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 10, Notes = "Cabecera de ventas" },
                    new { TableName = $"{schema}.AH_VENTA_LINEAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 10, Notes = "Detalle de ventas" },
                    new { TableName = $"{schema}.AB_ARTICULOS", IncrementalColumn = (string?)null, Enabled = true, Priority = 50, Notes = "Maestro de artículos" },
                    new { TableName = $"{schema}.AB_LABORATORIOS", IncrementalColumn = (string?)null, Enabled = true, Priority = 60, Notes = "Maestro de laboratorios" },
                    new { TableName = $"{schema}.AD_PROVEEDORES", IncrementalColumn = (string?)null, Enabled = true, Priority = 60, Notes = "Maestro de proveedores" },
                    new { TableName = $"{schema}.AB_FAMILIAS", IncrementalColumn = (string?)null, Enabled = true, Priority = 70, Notes = "Familias de artículos" },
                    new { TableName = $"{schema}.AE_RECETAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 30, Notes = "Recetas" },
                    new { TableName = $"{schema}.AE_RECE_LINEAS", IncrementalColumn = (string?)null, Enabled = true, Priority = 30, Notes = "Lineas de recetas" },
                    new { TableName = $"{schema}.AC_EXISTENCIAS", IncrementalColumn = (string?)null, Enabled = true, Priority = 40, Notes = "Stock/Existencias" },
                    new { TableName = $"{schema}.AD_ALBARANES", IncrementalColumn = "FECHA", Enabled = true, Priority = 50, Notes = "Albaranes de compra" },
                    new { TableName = $"{schema}.AD_LINALBARAN", IncrementalColumn = (string?)null, Enabled = true, Priority = 50, Notes = "Lineas de albaran" },
                    new { TableName = $"{schema}.AG_CLIENTES", IncrementalColumn = (string?)null, Enabled = true, Priority = 70, Notes = "Maestro de clientes" },
                    new { TableName = $"{schema}.AB_SUBFAMILIAS", IncrementalColumn = (string?)null, Enabled = true, Priority = 70, Notes = "Subfamilias de artículos" },
                    new { TableName = $"{schema}.AH_FACTURAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 40, Notes = "Facturas" },
                    new { TableName = $"{schema}.AH_FACTURA_LINEAS", IncrementalColumn = (string?)null, Enabled = true, Priority = 40, Notes = "Lineas de factura" }
                };
            }
            else
            {
                // Tablas Farmatic (SQL Server)
                tables = new List<object>
                {
                    new { TableName = "Venta", IncrementalColumn = (string?)null, Enabled = true, Priority = 10, Notes = "Cabecera de ventas" },
                    new { TableName = "LineaVenta", IncrementalColumn = (string?)null, Enabled = true, Priority = 10, Notes = "Detalle de ventas" },
                    new { TableName = "Articu", IncrementalColumn = (string?)null, Enabled = true, Priority = 50, Notes = "Maestro de artículos" },
                    new { TableName = "Proveedor", IncrementalColumn = (string?)null, Enabled = true, Priority = 60, Notes = "Maestro de proveedores" },
                    new { TableName = "Recep", IncrementalColumn = (string?)null, Enabled = true, Priority = 40, Notes = "Recepciones de mercancía" },
                    new { TableName = "LineaRecep", IncrementalColumn = (string?)null, Enabled = true, Priority = 40, Notes = "Detalle de recepciones" },
                    new { TableName = "Vendedor", IncrementalColumn = (string?)null, Enabled = true, Priority = 70, Notes = "Maestro de vendedores" },
                    new { TableName = "AlbaranDevol", IncrementalColumn = (string?)null, Enabled = true, Priority = 80, Notes = "Albaranes de devolución" },
                    new { TableName = "CosteVenta", IncrementalColumn = (string?)null, Enabled = true, Priority = 80, Notes = "Costes de venta" },
                    new { TableName = "LineaAlbaran", IncrementalColumn = (string?)null, Enabled = true, Priority = 80, Notes = "Líneas de albarán" },
                    new { TableName = "VentaAux", IncrementalColumn = (string?)null, Enabled = true, Priority = 80, Notes = "Ventas auxiliares" },
                    new { TableName = "Laboratorio", IncrementalColumn = (string?)null, Enabled = true, Priority = 80, Notes = "Maestro de laboratorios" },
                    new { TableName = "Estarti", IncrementalColumn = (string?)null, Enabled = true, Priority = 80, Notes = "Estadísticas de artículos" },
                    new { TableName = "Familia", IncrementalColumn = (string?)null, Enabled = true, Priority = 70, Notes = "Familias de artículos" },
                    new { TableName = "Lote", IncrementalColumn = (string?)null, Enabled = true, Priority = 80, Notes = "Lotes de artículos" }
                };
            }

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
                ["AgentVersion"] = "1.0.0",
                ["ErpInstallPath"] = erp.InstallPath ?? "",
                ["DetectionMethod"] = erp.DetectionMethod,
                ["DatabaseName"] = erp.DbType == "oracle" ? (erp.TnsName ?? "NIXFARMA") : (erp.DbName ?? "Farmatic"),
                ["DbSchema"] = erp.DbSchema ?? ""
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
        public string? TnsName { get; set; }
        public string? DbSchema { get; set; }
    }
}