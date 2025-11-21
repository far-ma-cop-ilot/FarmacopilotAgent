using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient; // NuGet: Microsoft.Data.SqlClient
using Oracle.ManagedDataAccess.Client; // NuGet: Oracle.ManagedDataAccess
using System.Diagnostics;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SetupWizard
{
    public class InstallerService
    {
        public bool TestDbConnection(string connectionString, string username, string password, string erp)
        {
            try
            {
                if (string.IsNullOrEmpty(connectionString)) return false;

                if (erp == "Nixfarma")
                {
                    
                    var oracleConnStr = connectionString.Replace("{USER}", username).Replace("{PASS}", password);
                    using var oracleConn = new OracleConnection(oracleConnStr);
                    oracleConn.Open();
                    // Verificar tablas clave (spec Nixfarma)
                    var tablesToCheck = new[] { "AH_VENTAS", "AB_ARTICULOS_FICHA_E", "AD_PED_DEV" };
                    foreach (var table in tablesToCheck)
                    {
                        using var cmd = new OracleCommand($"SELECT COUNT(*) FROM {table}", oracleConn);
                        cmd.ExecuteScalar();
                    }
                    return true;
                }
                else if (erp == "Farmatic")
                {
                    
                    var sqlConnStr = connectionString.Replace("{USER}", username).Replace("{PASS}", password);
                    using var sqlConn = new SqlConnection(sqlConnStr);
                    sqlConn.Open();
                    // Verificar tablas clave (spec Farmatic)
                    var tablesToCheck = new[] { "ventas", "articu", "proveedor" };
                    foreach (var table in tablesToCheck)
                    {
                        using var cmd = new SqlCommand($"SELECT COUNT(*) FROM [{table}]", sqlConn);
                        cmd.ExecuteScalar();
                    }
                    return true;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return false;
        }

        public void EncryptAndSaveCreds(string installPath, string username, string password, string connectionString, string farmaciaId, string erp)
        {
            // Usar DPAPIHelper del proyecto Core
            var encryptedConnection = DPAPIHelper.Encrypt($"{connectionString.Replace("{USER}", username).Replace("{PASS}", password)}");
            
            // Crear AgentConfig compatible con ConfigManager
            var config = new AgentConfig
            {
                FarmaciaId = farmaciaId,
                ErpType = erp.ToLower(),
                ErpVersion = detectedVersion, // Necesitas pasar esto como parámetro
                DbType = erp == "Nixfarma" ? "oracle" : "sqlserver",
                DbConnectionEncrypted = encryptedConnection,
                PostgresConnectionEncrypted = "", // Se llenará desde secrets.enc
                SharePointSiteId = "", // Se llenará desde secrets.enc
                ExportSchedule = "03:00",
                TablesToExport = GetDefaultTables(erp),
                LastInstallTs = DateTime.UtcNow,
                AgentVersion = "1.0.0"
            };
        
            var configPath = Path.Combine(installPath, "config.json");
            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(configPath, json);
        }
        
        private List<TableConfig> GetDefaultTables(string erp)
        {
            if (erp == "Nixfarma")
            {
                return new List<TableConfig>
                {
                    new() { TableName = "AH_VENTAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 10 },
                    new() { TableName = "AH_VENTA_LINEAS", IncrementalColumn = "FECHA", Enabled = true, Priority = 10 },
                    new() { TableName = "AB_ARTICULOS_FICHA_E", Enabled = true, Priority = 50 },
                    new() { TableName = "AB_LABORATORIOS", Enabled = true, Priority = 60 },
                    new() { TableName = "AD_PROVEEDORES", Enabled = true, Priority = 60 }
                };
            }
            else
            {
                return new List<TableConfig>
                {
                    new() { TableName = "ventas", IncrementalColumn = "fecha", Enabled = true, Priority = 10 },
                    new() { TableName = "linea venta", IncrementalColumn = "fecha", Enabled = true, Priority = 10 },
                    new() { TableName = "articu", Enabled = true, Priority = 50 },
                    new() { TableName = "proveedor", Enabled = true, Priority = 60 },
                    new() { TableName = "recep", IncrementalColumn = "fecha_recep", Enabled = true, Priority = 40 }
                };
            }
        }

        public async Task CreateScheduledTask(string installPath)
        {
            var taskName = "Farmacopilot_Export";
            var scriptPath = Path.Combine(installPath, "scripts", "export.ps1");

            // Eliminar si existe
            var deleteProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/delete /tn {taskName} /f",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            deleteProc.Start();
            deleteProc.WaitForExit();

            // Crear nueva
            var createProc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "schtasks",
                    Arguments = $"/create /tn {taskName} /tr \"powershell.exe -ExecutionPolicy Bypass -File \\\"{scriptPath}\\\"\" /sc daily /st 03:00 /ru SYSTEM /rl HIGHEST",
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            createProc.Start();
            await createProc.WaitForExitAsync(); // Async para no bloquear UI
        }

        // Método para primera export prueba (llamar en InstallAsync)
        public void RunExportTest(string installPath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{Path.Combine(installPath, "scripts", "export.ps1")}\" -TestMode",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(60000); // Timeout 1min
            if (proc?.ExitCode != 0)
                throw new Exception("Export de prueba falló: " + proc?.StandardError.ReadToEnd());
        }

        // Verificar subida a SharePoint (placeholder con Graph API - implementa con MSAL si tienes app reg)
        public bool VerifyUpload(string farmaciaId, string erp)
        {
            // TODO: HttpClient POST a Graph API: https://graph.microsoft.com/v1.0/sites/{site-id}/drives/{drive-id}/root:/{erp}/{farmaciaId}:/children
            // Check if files exist
            return true; // Simular OK
        }
    }
}
