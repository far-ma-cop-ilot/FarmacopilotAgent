using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using FarmacopilotAgent.Core.Models;
using FarmacopilotAgent.Core.Utils;
using FarmacopilotAgent.Detection;

namespace SetupWizard
{
    public partial class MainWindow : Window
    {
        private string farmaciaId = string.Empty;
        private string detectedErp = string.Empty;
        private string detectedVersion = string.Empty;
        private string dbConnectionString = string.Empty;
        private string dbUsername = string.Empty;
        private string dbPassword = string.Empty;

        // ← INSTANCIA DEL SERVICIO (esto es lo que faltaba)
        private readonly InstallerService _service = new InstallerService();

        public MainWindow()
        {
            InitializeComponent();
            ShowScreen("Welcome");
        }

        private void ShowScreen(string screen)
        {
            WelcomePanel.Visibility = Visibility.Collapsed;
            ErpDetectionPanel.Visibility = Visibility.Collapsed;
            DbCredsPanel.Visibility = Visibility.Collapsed;
            ExportConfigPanel.Visibility = Visibility.Collapsed;
            InstallPanel.Visibility = Visibility.Collapsed;

            switch (screen)
            {
                case "Welcome":
                    WelcomePanel.Visibility = Visibility.Visible;
                    LoadFarmaciaId();
                    PerformWelcomeChecks();
                    break;
                case "ErpDetection":
                    ErpDetectionPanel.Visibility = Visibility.Visible;
                    DetectErp();
                    break;
                case "DbCreds":
                    DbCredsPanel.Visibility = Visibility.Visible;
                    break;
                case "ExportConfig":
                    ExportConfigPanel.Visibility = Visibility.Visible;
                    break;
                case "Install":
                    InstallPanel.Visibility = Visibility.Visible;
                    InstallStatusLabel.Text = "Iniciando instalación...";
                    InstallAsync();
                    break;
            }
        }

        private void LoadFarmaciaId()
        {
            var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (File.Exists(configPath))
            {
                var config = JsonConvert.DeserializeObject<dynamic>(File.ReadAllText(configPath));
                farmaciaId = config.farmacia_id ?? string.Empty;
                FarmaciaIdTextBox.Text = farmaciaId;
            }
            else
            {
                var template = new { farmacia_id = "farmacia_001" };
                File.WriteAllText(configPath, JsonConvert.SerializeObject(template, Formatting.Indented));
                farmaciaId = "farmacia_001";
                FarmaciaIdTextBox.Text = farmaciaId;
            }
        }

        private void PerformWelcomeChecks()
        {
            var errors = new StringBuilder();

            // Windows 10 o superior
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    var version = obj["Version"]?.ToString();
                    if (!version.StartsWith("10.") && !version.StartsWith("11."))
                        errors.AppendLine("• Requiere Windows 10 o Windows 11");
                }
            }

            // Administrador
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                errors.AppendLine("• Debe ejecutarse como Administrador");

            // .NET 8 Runtime
            var netPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"dotnet\shared\Microsoft.NETCore.App");
            if (!Directory.Exists(netPath) || !Directory.GetDirectories(netPath).Any(d => Path.GetFileName(d).StartsWith("8.")))
                errors.AppendLine("• Requiere .NET 8.0 Desktop Runtime instalado");

            if (errors.Length > 0)
            {
                MessageBox.Show($"Se han detectado los siguientes problemas:\n\n{errors}\n\nEl instalador se cerrará.", 
                    "Requisitos no cumplidos", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
            else
            {
                StatusLabel.Text = $"Todo correcto · Farmacia ID: {farmaciaId}";
                StatusLabel.Foreground = System.Windows.Media.Brushes.Green;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            switch (GetCurrentScreen())
            {
                case "Welcome":
                    if (string.IsNullOrWhiteSpace(FarmaciaIdTextBox.Text))
                    {
                        MessageBox.Show("El ID de farmacia es obligatorio.", "Datos incompletos", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                    farmaciaId = FarmaciaIdTextBox.Text.Trim();
                    ShowScreen("ErpDetection");
                    break;

                case "ErpDetection":
                    if (string.IsNullOrEmpty(detectedErp))
                    {
                        MessageBox.Show("No se detectó ningún ERP compatible (Nixfarma o Farmatic).\n\nAsegúrese de que el programa esté instalado.", 
                            "ERP no encontrado", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    ShowScreen("DbCreds");
                    break;

                case "DbCreds":
                    dbUsername = UsernameTextBox.Text.Trim();
                    dbPassword = PasswordBox.Password;

                    if (string.IsNullOrEmpty(dbUsername) || string.IsNullOrEmpty(dbPassword))
                    {
                        MessageBox.Show("Usuario y contraseña son obligatorios.", "Credenciales incompletas", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    dbConnectionString = DetectConnectionString(detectedErp);
                    InstallStatusLabel.Text = "Probando conexión a la base de datos...";

                    if (_service.TestDbConnection(dbConnectionString, dbUsername, dbPassword, detectedErp))
                    {
                        ShowScreen("ExportConfig");
                    }
                    else
                    {
                        MessageBox.Show("No se pudo conectar a la base de datos.\n\nRevise usuario/contraseña o contacte con soporte.", 
                            "Error de conexión", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    break;

                case "ExportConfig":
                    ShowScreen("Install");
                    break;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        private string GetCurrentScreen()
        {
            if (WelcomePanel.Visibility == Visibility.Visible) return "Welcome";
            if (ErpDetectionPanel.Visibility == Visibility.Visible) return "ErpDetection";
            if (DbCredsPanel.Visibility == Visibility.Visible) return "DbCreds";
            if (ExportConfigPanel.Visibility == Visibility.Visible) return "ExportConfig";
            return "Install";
        }

        private void DetectErp()
        {
            var logger = Serilog.Log.Logger;
            var erpDetector = new ErpDetector(logger);
            
            try
            {
                var erpInfo = erpDetector.DetectErp();
                detectedErp = erpInfo.ErpType;
                detectedVersion = erpInfo.Version;
                ErpStatusLabel.Text = $"{erpInfo.ErpType} detectado (v{erpInfo.Version})";
                ErpStatusLabel.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                ErpStatusLabel.Text = "ERP no detectado";
                ErpStatusLabel.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private string DetectConnectionString(string erp)
        {
            if (erp == "Nixfarma")
            {
                var tnsPath = @"C:\Oracle\product\11.2.0\client_1\NETWORK\ADMIN\tnsnames.ora";
                if (File.Exists(tnsPath))
                {
                    var content = File.ReadAllText(tnsPath);
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"(\w+)\s*=\s*\(.*?(HOST\s*=\s*[^\)]+).*(PORT\s*=\s*\d+).*(SERVICE_NAME\s*=\s*[\w\.]+)", 
                        System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        return $"Data Source={match.Groups[1].Value};User Id={dbUsername};Password={dbPassword};";
                    }
                }
                // Fallback genérico
                return $"Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=XE)));User Id={dbUsername};Password={dbPassword};";
            }
            else // Farmatic
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Consoft\Farmatic"))
                {
                    var server = key?.GetValue("Server")?.ToString() ?? "localhost";
                    return $"Server={server};Database=CGCOF;User Id={dbUsername};Password={dbPassword};TrustServerCertificate=true;";
                }
            }
        }

        private async void InstallAsync()
        {
            try
            {
                InstallStatusLabel.Text = "Creando carpeta de instalación...";
                var installPath = @"C:\FarmacopilotAgent";
                Directory.CreateDirectory(installPath);

                InstallStatusLabel.Text = "Copiando archivos del agente...";
                var sourceDir = AppDomain.CurrentDomain.BaseDirectory;
                CopyDirectory(sourceDir, installPath);

                InstallStatusLabel.Text = "Guardando credenciales cifradas...";
                _service.EncryptAndSaveCreds(installPath, dbUsername, dbPassword, dbConnectionString, farmaciaId, detectedErp);

                InstallStatusLabel.Text = "Creando tarea programada diaria...";
                await _service.CreateScheduledTask(installPath);

                InstallStatusLabel.Text = "Ejecutando primera exportación de prueba...";
                _service.RunExportTest(installPath);

                InstallStatusLabel.Text = "Verificando subida a SharePoint...";
                if (!_service.VerifyUpload(farmaciaId, detectedErp))
                {
                    MessageBox.Show("Advertencia: No se pudo verificar la subida. Revise logs.", "Subida no verificada", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                InstallStatusLabel.Text = "Notificando instalación completa...";

                // Notificar al backend
                var apiSuccess = await CallCompletionApi(farmaciaId, "completed");
                
                if (apiSuccess)
                {
                    InstallStatusLabel.Text = "¡Instalación completada y registrada!";
                    InstallStatusLabel.Foreground = System.Windows.Media.Brushes.Green;
                    
                    // Trigger Power Automate webhook
                    await TriggerPowerAutomateWebhook(farmaciaId);
                }
                else
                {
                    InstallStatusLabel.Text = "¡Instalación completada! (sin conexión al servidor)";
                    InstallStatusLabel.Foreground = System.Windows.Media.Brushes.Orange;
                }
                
                MessageBox.Show(
                    $"¡Todo listo!\n\nEl agente se ejecutará automáticamente todas las noches a las 03:00.\n\nRecibirás un email de confirmación con tu primer reporte en las próximas horas.\n\nTus datos ya están sincronizándose con Farmacopilot.",
                    "Instalación completada", MessageBoxButton.OK, MessageBoxImage.Information);
                
                Close();
            }
            catch (Exception ex)
            {
                InstallStatusLabel.Text = "Error en instalación";
                InstallStatusLabel.Foreground = System.Windows.Media.Brushes.Red;
                MessageBox.Show($"Error crítico:\n{ex.Message}\n\nContacte con soporte@farmacopilot.com", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CopyDirectory(string source, string target)
        {
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dir.Replace(source, target));

            foreach (var file in Directory.GetFiles(source, "*.*", SearchOption.AllDirectories))
            {
                var dest = file.Replace(source, target);
                File.Copy(file, dest, true);
            }
        }

        private async Task<bool> CallCompletionApi(string id, string status)
        {
            try
            {
                using var client = new HttpClient();
                client.Timeout = TimeSpan.FromSeconds(30);
                
                var payload = new
                {
                    farmacia_id = id,
                    status = status,
                    timestamp = DateTime.UtcNow,
                    agent_version = "1.0.0",
                    erp_type = detectedErp,
                    erp_version = detectedVersion,
                    hostname = Environment.MachineName
                };
                
                var content = new StringContent(
                    JsonConvert.SerializeObject(payload), 
                    Encoding.UTF8, 
                    "application/json"
                );
                
                var response = await client.PostAsync(
                    $"https://api.farmacopilot.com/api/installations/{id}/complete", 
                    content
                );
                
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger?.Error(ex, "Error notificando instalación completa");
                return false;
            }
        }
        private async Task TriggerPowerAutomateWebhook(string farmaciaId)
        {
            try
            {
                using var client = new HttpClient();
                var webhookUrl = "https://prod-XX.westeurope.logic.azure.com/workflows/XXXXX/triggers/manual/paths/invoke?api-version=2016-06-01&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=XXXXX";
                
                var payload = new
                {
                    farmacia_id = farmaciaId,
                    event_type = "installation_completed",
                    timestamp = DateTime.UtcNow.ToString("o"),
                    erp_type = detectedErp
                };
                
                await client.PostAsync(
                    webhookUrl,
                    new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json")
                );
            }
            catch
            {
                // No crítico - continuar sin error
            }
        }
    }
}
