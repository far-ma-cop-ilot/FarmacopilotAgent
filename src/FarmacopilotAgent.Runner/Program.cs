using System;
using System.IO;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;
using FarmacopilotAgent.Core.Utils;
using FarmacopilotAgent.Core.Http;
using FarmacopilotAgent.Core.Security;
using FarmacopilotAgent.Detection;
using FarmacopilotAgent.Exporters;
using FarmacopilotAgent.Uploaders;

namespace FarmacopilotAgent.Runner
{
    class Program
    {
        private static readonly string BasePath = @"C:\FarmacopilotAgent";
        private static readonly string LogPath = Path.Combine(BasePath, "logs");

        static async Task<int> Main(string[] args)
        {
            // Configurar Serilog
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.Console()
                .WriteTo.File(
                    Path.Combine(LogPath, "agent.log"),
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
                )
                .CreateLogger();

            try
            {
                Log.Information("=== Farmacopilot Agent v1.0.0 iniciado ===");

                // Cargar configuración
                var configManager = new ConfigManager(BasePath, Log.Logger);
                var config = await configManager.LoadConfigAsync();

                if (config == null)
                {
                    Log.Error("No se pudo cargar la configuración. Ejecute primero la instalación.");
                    return 1;
                }

                // Verificar estado del cliente
                var apiClient = new ApiClient(config.ApiBaseUrl, Log.Logger);
                var clientStatus = await apiClient.GetClientStatusAsync(config.FarmaciaId);

                if (clientStatus == null || !clientStatus.Active)
                {
                    Log.Warning("Cliente inactivo o no se pudo verificar estado. Deshabilitando tarea.");
                    var taskManager = new TaskSchedulerManager(Log.Logger);
                    taskManager.DisableTask("Farmacopilot_Export");
                    
                    await apiClient.ReportAgentActivityAsync(new
                    {
                        farmacia_id = config.FarmaciaId,
                        event_type = "disabled_by_status_check",
                        reason = clientStatus?.Reason ?? "status_check_failed",
                        timestamp = DateTime.UtcNow
                    });

                    return 0;
                }

                // Realizar exportación
                var lastExportManager = new LastExportManager(BasePath, Log.Logger);
                var lastExportTs = await lastExportManager.GetLastExportTimestampAsync();

                var connectionString = configManager.GetDecryptedConnectionString(config);
                var outputPath = Path.Combine(BasePath, "staging");

                IExporter exporter = config.ErpType.ToLower() switch
                {
                    "nixfarma" => new NixfarmaExporter(connectionString, outputPath, config.FarmaciaId, Log.Logger),
                    "farmatic" => throw new NotImplementedException("FarmaticExporter pendiente Sprint 2"),
                    _ => throw new InvalidOperationException($"ERP no soportado: {config.ErpType}")
                };

                var exportResult = await exporter.ExportDataAsync(lastExportTs);

                if (exportResult.Success)
                {
                    Log.Information("Exportación exitosa: {File}", exportResult.FilePath);

                    // ✅ NUEVO: Subir a SharePoint usando Client Credentials
                    var credentialsProvider = new GraphCredentialsProvider(BasePath, Log.Logger);
                    var graphCredentials = credentialsProvider.GetCredentials();

                    var uploader = new SharePointGraphUploader(
                        config.FarmaciaId,
                        graphCredentials,
                        Log.Logger
                    );

                    var uploadSuccess = await uploader.UploadFileAsync(
                        exportResult.FilePath, 
                        $"/Clients/{config.FarmaciaId}/Exports/"
                    );

                    if (uploadSuccess)
                    {
                        Log.Information("Archivo subido correctamente a SharePoint");
                        
                        // Validar integridad
                        await uploader.ValidateUploadAsync(
                            $"/Clients/{config.FarmaciaId}/Exports/{Path.GetFileName(exportResult.FilePath)}",
                            exportResult.Sha256Hash
                        );

                        await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, true);

                        // Reportar al backend
                        await apiClient.ReportAgentActivityAsync(new
                        {
                            farmacia_id = config.FarmaciaId,
                            event_type = "export_success",
                            rows_exported = exportResult.RowsExported,
                            file_hash = exportResult.Sha256Hash,
                            timestamp = exportResult.ExportTimestamp
                        });
                    }
                    else
                    {
                        Log.Error("Error al subir archivo a SharePoint");
                        await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, false, "upload_failed");
                    }
                }
                else
                {
                    Log.Error("Error en exportación: {Error}", exportResult.ErrorDetails);
                    await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, false, exportResult.ErrorDetails);
                }

                Log.Information("=== Farmacopilot Agent finalizado ===");
                return exportResult.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Error crítico en Farmacopilot Agent");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}
