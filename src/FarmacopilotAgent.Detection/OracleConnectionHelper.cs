using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Oracle.ManagedDataAccess.Client;
using Serilog;

namespace FarmacopilotAgent.Detection
{
    /// <summary>
    /// Helper para detección y configuración de conexiones Oracle (Farmatic)
    /// </summary>
    public class OracleConnectionHelper
    {
        private readonly ILogger _logger;

        public OracleConnectionHelper(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Información de conexión Oracle detectada
        /// </summary>
        public class OracleConnectionInfo
        {
            public string Host { get; set; } = "localhost";
            public int Port { get; set; } = 1521;
            public string ServiceName { get; set; } = string.Empty;
            public string TnsName { get; set; } = string.Empty;
            public string? TnsNamesPath { get; set; }
            public string ConnectionString { get; set; } = string.Empty;
            public string DetectionMethod { get; set; } = string.Empty;
        }

        /// <summary>
        /// Detecta configuración Oracle del sistema
        /// </summary>
        public OracleConnectionInfo DetectOracleConnection()
        {
            _logger.Information("Detectando configuración Oracle...");

            // 1. Intentar detectar desde TNSNAMES.ORA
            var tnsInfo = DetectFromTnsNames();
            if (tnsInfo != null)
            {
                _logger.Information("Configuración Oracle detectada desde TNSNAMES.ORA");
                return tnsInfo;
            }

            // 2. Intentar detectar desde registro Windows
            var registryInfo = DetectFromRegistry();
            if (registryInfo != null)
            {
                _logger.Information("Configuración Oracle detectada desde registro Windows");
                return registryInfo;
            }

            // 3. Fallback a configuración por defecto
            _logger.Warning("No se pudo detectar configuración Oracle automáticamente. Usando valores por defecto.");
            return new OracleConnectionInfo
            {
                Host = "localhost",
                Port = 1521,
                ServiceName = "FARMATIC",
                DetectionMethod = "default"
            };
        }

        /// <summary>
        /// Detecta desde archivo TNSNAMES.ORA
        /// </summary>
        private OracleConnectionInfo? DetectFromTnsNames()
        {
            try
            {
                // Buscar archivo TNSNAMES.ORA en ubicaciones comunes
                var tnsLocations = new[]
                {
                    Environment.GetEnvironmentVariable("TNS_ADMIN"),
                    Path.Combine(Environment.GetEnvironmentVariable("ORACLE_HOME") ?? "", "network", "admin"),
                    @"C:\oracle\product\11.2.0\client_1\network\admin",
                    @"C:\oracle\product\12.2.0\client_1\network\admin",
                    @"C:\app\oracle\product\11.2.0\client_1\network\admin",
                    @"C:\app\oracle\product\12.2.0\client_1\network\admin"
                };

                foreach (var location in tnsLocations.Where(l => !string.IsNullOrEmpty(l)))
                {
                    var tnsPath = Path.Combine(location!, "tnsnames.ora");
                    if (File.Exists(tnsPath))
                    {
                        _logger.Information("Encontrado TNSNAMES.ORA en {Path}", tnsPath);
                        return ParseTnsNames(tnsPath);
                    }
                }

                _logger.Warning("TNSNAMES.ORA no encontrado en ubicaciones comunes");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al leer TNSNAMES.ORA");
                return null;
            }
        }

        /// <summary>
        /// Parsea archivo TNSNAMES.ORA
        /// </summary>
        private OracleConnectionInfo? ParseTnsNames(string tnsPath)
        {
            try
            {
                var content = File.ReadAllText(tnsPath);

                // Buscar entradas FARMATIC o FAR
                var tnsPattern = @"(FARMATIC|FAR\w*)\s*=\s*\(DESCRIPTION\s*=.*?HOST\s*=\s*([^\)]+)\).*?PORT\s*=\s*(\d+).*?SERVICE_NAME\s*=\s*([^\)]+)\)";
                var match = Regex.Match(content, tnsPattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);

                if (match.Success)
                {
                    var tnsName = match.Groups[1].Value.Trim();
                    var host = match.Groups[2].Value.Trim();
                    var port = int.Parse(match.Groups[3].Value.Trim());
                    var serviceName = match.Groups[4].Value.Trim();

                    _logger.Information("TNS Entry encontrado: {TnsName} -> {Host}:{Port}/{ServiceName}",
                        tnsName, host, port, serviceName);

                    return new OracleConnectionInfo
                    {
                        TnsName = tnsName,
                        Host = host,
                        Port = port,
                        ServiceName = serviceName,
                        TnsNamesPath = tnsPath,
                        DetectionMethod = "tnsnames"
                    };
                }

                _logger.Warning("No se encontró entrada FARMATIC en TNSNAMES.ORA");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al parsear TNSNAMES.ORA");
                return null;
            }
        }

        /// <summary>
        /// Detecta desde registro Windows
        /// </summary>
        private OracleConnectionInfo? DetectFromRegistry()
        {
            try
            {
                // Buscar ORACLE_HOME en registro
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\ORACLE")
                    ?? Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\ORACLE");

                if (key != null)
                {
                    var oracleHome = key.GetValue("ORACLE_HOME") as string;
                    if (!string.IsNullOrEmpty(oracleHome))
                    {
                        _logger.Information("ORACLE_HOME encontrado en registro: {Path}", oracleHome);
                        
                        // Buscar TNSNAMES.ORA dentro de ORACLE_HOME
                        var tnsPath = Path.Combine(oracleHome, "network", "admin", "tnsnames.ora");
                        if (File.Exists(tnsPath))
                        {
                            return ParseTnsNames(tnsPath);
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al leer registro Oracle");
                return null;
            }
        }

        /// <summary>
        /// Construye connection string desde información detectada
        /// </summary>
        public string BuildConnectionString(OracleConnectionInfo info, string username, string password)
        {
            // Si hay TNS Name, usarlo directamente
            if (!string.IsNullOrEmpty(info.TnsName))
            {
                return $"Data Source={info.TnsName};User Id={username};Password={password};";
            }

            // Si no, construir connection string con host/port/service
            var dataSource = $"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST={info.Host})(PORT={info.Port}))(CONNECT_DATA=(SERVICE_NAME={info.ServiceName})))";
            return $"Data Source={dataSource};User Id={username};Password={password};";
        }

        /// <summary>
        /// Prueba conexión Oracle
        /// </summary>
        public bool TestConnection(string connectionString)
        {
            try
            {
                _logger.Information("Probando conexión Oracle...");
                
                using var connection = new OracleConnection(connectionString);
                connection.Open();
                
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT 1 FROM DUAL";
                command.ExecuteScalar();
                
                _logger.Information("Conexión Oracle exitosa");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al probar conexión Oracle");
                return false;
            }
        }

        /// <summary>
        /// Detecta versión de Oracle Client instalado
        /// </summary>
        public string? DetectOracleClientVersion()
        {
            try
            {
                var oracleHome = Environment.GetEnvironmentVariable("ORACLE_HOME");
                if (string.IsNullOrEmpty(oracleHome))
                {
                    return null;
                }

                // Buscar archivo de versión
                var versionFiles = new[]
                {
                    Path.Combine(oracleHome, "inventory", "ContentsXML", "comps.xml"),
                    Path.Combine(oracleHome, "OPatch", "version.txt")
                };

                foreach (var file in versionFiles)
                {
                    if (File.Exists(file))
                    {
                        var content = File.ReadAllText(file);
                        var versionMatch = Regex.Match(content, @"(\d+\.\d+\.\d+\.\d+)");
                        if (versionMatch.Success)
                        {
                            return versionMatch.Groups[1].Value;
                        }
                    }
                }

                return "Unknown";
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error al detectar versión Oracle Client");
                return null;
            }
        }
    }
}
