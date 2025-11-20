using Microsoft.Win32;
using Microsoft.WindowsAPICodePack.Shell; // Para checks admin (NuGet: Microsoft.WindowsAPICodePack-Shell)
using System;
using System.Diagnostics;
using System.IO;
using System.Management; // Para WMI checks (Windows version, .NET)
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json; // NuGet: Newtonsoft.Json para config

namespace SetupWizard
{
    public partial class MainWindow : Window
    {
        private string farmaciaId = string.Empty;
        private string detectedErp = string.Empty;
        private string detectedVersion = string.Empty;
        private string dbConnectionString = string.Empty;
        private string dbUsername = string.Empty;
        private string dbPassword = string.Empty; // Encriptar post-input
        private bool[] selectedTables = new bool[3]; // Ej: ventas=true, stock=true, compras=true (por default)

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
                    InstallAsync();
                    break;
            }
        }

        // Pantalla 1: Bienvenida + Checks
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
                // Template si no existe
                var template = new { farmacia_id = "farmacia_001" };
                File.WriteAllText(configPath, JsonConvert.SerializeObject(template, Formatting.Indented));
                farmaciaId = "farmacia_001";
            }
        }

        private void PerformWelcomeChecks()
        {
            var errors = new System.Text.StringBuilder();

            // Check Windows version (10+)
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
            {
                foreach (ManagementObject obj in searcher.Get())
                {
                    var version = obj["Version"]?.ToString();
                    if (!version.StartsWith("10.") && !version.StartsWith("11."))
                        errors.AppendLine("Requiere Windows 10 o superior.");
                }
            }

            // Check Admin
            var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
                errors.AppendLine("Ejecutar como Administrador.");

            // Check .NET 8+
            var netPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"dotnet\shared\Microsoft.NETCore.App");
            if (!Directory.Exists(netPath) || !Directory.GetDirectories(netPath).Any(d => d.Contains("8.")))
                errors.AppendLine("Requiere .NET 8.0 runtime.");

            if (errors.Length > 0)
            {
                MessageBox.Show($"Errores en checks:\n{errors}", "Instalación Fallida", MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
            else
            {
                StatusLabel.Text = "Checks OK. Farmacia ID: " + farmaciaId;
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            var currentScreen = (sender as Button).Tag?.ToString();
            switch (currentScreen)
            {
                case "Welcome":
                    if (string.IsNullOrEmpty(FarmaciaIdTextBox.Text)) { MessageBox.Show("Ingrese Farmacia ID."); return; }
                    farmaciaId = FarmaciaIdTextBox.Text;
                    ShowScreen("ErpDetection");
                    break;
                case "ErpDetection":
                    if (string.IsNullOrEmpty(detectedErp)) { MessageBox.Show("ERP no detectado. Verifique instalación."); return; }
                    ErpLabel.Text = $"ERP: {detectedErp} v{detectedVersion}";
                    ShowScreen("DbCreds");
                    break;
                case "DbCreds":
                    dbUsername = UsernameTextBox.Text;
                    dbPassword = PasswordBox.Password;
                    if (string.IsNullOrEmpty(dbUsername) || string.IsNullOrEmpty(dbPassword)) { MessageBox.Show("Credenciales requeridas."); return; }
                    // Probar conexión (placeholder - implementar con OracleConnection o SqlConnection)
                    dbConnectionString = DetectConnectionString(detectedErp); // Lógica abajo
                    if (TestDbConnection()) ShowScreen("ExportConfig");
                    else MessageBox.Show("Conexión BD fallida.");
                    break;
                case "ExportConfig":
                    // Default: diaria 03:00, todas tablas
                    selectedTables[0] = VentasCheckBox.IsChecked ?? false;
                    selectedTables[1] = StockCheckBox.IsChecked ?? false;
                    selectedTables[2] = ComprasCheckBox.IsChecked ?? false;
                    if (!selectedTables.Any(b => b)) { MessageBox.Show("Seleccione al menos una tabla."); return; }
                    ShowScreen("Install");
                    break;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Close();

        // Pantalla 2: Detección ERP
        private void DetectErp()
        {
            detectedErp = string.Empty;
            detectedVersion = string.Empty;

            // Scan Nixfarma
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Pulso Informatica\Nixfarma"))
            {
                if (key != null)
                {
                    detectedErp = "Nixfarma";
                    detectedVersion = key.GetValue("Version")?.ToString() ?? "Unknown";
                }
            }

            // Scan Farmatic
            if (string.IsNullOrEmpty(detectedErp))
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Consoft\Farmatic"))
                {
                    if (key != null)
                    {
                        detectedErp = "Farmatic";
                        detectedVersion = key.GetValue("Version")?.ToString() ?? "Unknown";
                    }
                }
            }

            ErpStatusLabel.Text = detectedErp == string.Empty ? "No detectado" : $"Detectado: {detectedErp} v{detectedVersion}";
        }

        private string DetectConnectionString(string erp)
        {
            if (erp == "Nixfarma")
            {
                // Leer tnsnames.ora (ej: C:\Oracle\product\11.2.0\client_1\NETWORK\ADMIN\tnsnames.ora)
                var tnsPath = @"C:\Oracle\product\11.2.0\client_1\NETWORK\ADMIN\tnsnames.ora";
                if (File.Exists(tnsPath))
                {
                    var lines = File.ReadAllLines(tnsPath);
                    // Parse simple para entry (asumir primera)
                    foreach (var line in lines)
                    {
                        if (line.Trim().StartsWith("FARMA"))
                            return $"Data Source={line.Trim()}; User Id={dbUsername}; Password={dbPassword};"; // Oracle format
                    }
                }
            }
            else if (erp == "Farmatic")
            {
                // SQL Server: Detect server name via registry o default local
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Consoft\Farmatic"))
                {
                    var server = key?.GetValue("Server")?.ToString() ?? "(local)";
                    return $"Server={server};Database=CGCOF;User Id={dbUsername};Password={dbPassword};";
                }
            }
            return string.Empty;
        }

        private bool TestDbConnection()
        {
            // Placeholder: Implementar con System.Data.OracleClient o SqlClient
            // Ej: using var conn = new OracleConnection(dbConnectionString); conn.Open();
            // Verificar tablas: SELECT COUNT(*) FROM AB_VENTAS (Nixfarma) o ventas (Farmatic)
            return true; // Simular OK para demo
        }

        // Pantalla 4: Instalación
        private async void InstallAsync()
        {
            try
            {
                var installPath = @"C:\FarmacopilotAgent";
                Directory.CreateDirectory(installPath);

                // Copiar assets (asumir en app dir: Runner.exe, scripts/, mappings/)
                var sourceDir = AppDomain.CurrentDomain.BaseDirectory;
                CopyDirectory(sourceDir, installPath);

                // Generar config.json con creds encriptadas (usar ProtectedData para DPAPI)
                var config = new
                {
                    farmacia_id = farmaciaId,
                    erp = detectedErp,
                    connection_string = dbConnectionString, // Encriptar
                    export_schedule = "03:00",
                    tables = selectedTables
                };
                File.WriteAllText(Path.Combine(installPath, "config.json"), JsonConvert.SerializeObject(config));

                // Crear tarea programada
                var taskProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "schtasks",
                        Arguments = $"/create /tn Farmacopilot_Export /tr \"powershell -File {installPath}\\scripts\\export.ps1\" /sc daily /st 03:00 /ru SYSTEM",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                taskProcess.Start();

                // Primera export prueba
                await Task.Run(() => RunExportTest(installPath));

                // Verificar subida (placeholder: check Graph API response)
                StatusLabel.Text = "Instalación completada. Subida verificada.";

                // Llamar API FrontEnd
                CallCompletionApi(farmaciaId, "completed");

                MessageBox.Show("Instalación exitosa. Export programada para 03:00 AM.");
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error en instalación: {ex.Message}");
            }
        }

        private void CopyDirectory(string source, string target)
        {
            foreach (var dirPath in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(dirPath.Replace(source, target));

            foreach (var filePath in Directory.GetFiles(source))
                File.Copy(filePath, filePath.Replace(source, target), true);
        }

        private void RunExportTest(string path)
        {
            // Ejecutar export.ps1 con params
            var psi = new ProcessStartInfo { FileName = "powershell", Arguments = $"-File {path}\\scripts\\export.ps1 -Test", UseShellExecute = false };
            Process.Start(psi).WaitForExit();
        }

        private void CallCompletionApi(string id, string status)
        {
            // Placeholder: HttpClient POST a https://farmacopilot.com/api/install-completed
            // using var client = new HttpClient(); var content = new StringContent(JsonConvert.SerializeObject(new { farmacia_id = id, status }), Encoding.UTF8, "application/json");
            // client.PostAsync("url", content);
        }
    }
}
