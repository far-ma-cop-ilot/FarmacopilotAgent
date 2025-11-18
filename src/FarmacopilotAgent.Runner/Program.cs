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
using FarmacopilotAgent.Core.Database;

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
                    Log.Error("No se pudo cargar la configuración");
                    return 1;
                }

                Log.Information("Configuración cargada: FarmaciaID={FarmaciaId}, ERP={ErpType} v{Version}",
                    config.FarmaciaId, config.ErpType, config.ErpVersion);
            
                // ✅ Verificar estado del cliente desde PostgreSQL
                var postgresConnection = configManager.GetDecryptedPostgresConnection(config);
                var statusChecker = new PostgresStatusChecker(postgresConnection, Log.Logger);
                var clientStatus = await statusChecker.GetClientStatusAsync(config.FarmaciaId);
            
                if (clientStatus == null)
                {
                    Log.Error("No se pudo verificar estado del cliente. Abortando ejecución.");
                    return 1;
                }
            
                if (!clientStatus.Active)
                {
                    Log.Warning("Cliente inactivo. Motivo: {Reason}. Deshabilitando tarea.", clientStatus.Reason);
                    
                    var taskManager = new TaskSchedulerManager(Log.Logger);
                    taskManager.DisableTask("Farmacopilot_Export");
                    
                    return 0;
                }
            
                Log.Information("Cliente activo. Procediendo con exportación...");
            
                // Realizar exportación
                var lastExportManager = new LastExportManager(BasePath, Log.Logger);
                var lastExportTs = await lastExportManager.GetLastExportTimestampAsync();
            
                var connectionString = configManager.GetDecryptedConnectionString(config);
                var outputPath = Path.Combine(BasePath, "staging");
            
                // Crear conexión según tipo de BD (corregido: Nixfarma=Oracle, Farmatic=SQLServer)
                DbConnection connection;
                
                if (config.ErpType.ToLower() == "nixfarma")
                {
                    Log.Information("Inicializando conexión Oracle (Nixfarma)...");
                    connection = new OracleConnection(connectionString);
                }
                else if (config.ErpType.ToLower() == "farmatic")
                {
                    Log.Information("Inicializando conexión SQL Server (Farmatic)...");
                    connection = new SqlConnection(connectionString);
                }
                else
                {
                    Log.Error("ERP no soportado: {ErpType}", config.ErpType);
                    throw new InvalidOperationException($"ERP no soportado: {config.ErpType}");
                }
                
                await connection.OpenAsync();
                
                // Crear exportador RAW genérico
                var exporter = new RawExporter(
                    connection, 
                    outputPath, 
                    config.FarmaciaId, 
                    Log.Logger
                );
                
                // Probar conexión
                Log.Information("Verificando conexión a base de datos...");
                var connectionOk = await exporter.TestConnectionAsync(connectionString);
                
                if (!connectionOk)
                {
                    Log.Error("No se pudo conectar a la base de datos");
                    await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, false, "connection_failed");
                    return 1;
                }
                
                Log.Information("Conexión verificada correctamente");
                
                // Cargar credenciales Graph
                var credentialsProvider = new GraphCredentialsProvider(BasePath, Log.Logger);
                var graphCredentials = credentialsProvider.GetCredentials();
                
                var uploader = new SharePointGraphUploader(
                    config.FarmaciaId,
                    graphCredentials,
                    Log.Logger
                );
                
                // Exportar cada tabla configurada
                var allSuccess = true;
                foreach (var tableConfig in config.TablesToExport.Where(t => t.Enabled))
                {
                    Log.Information("Exportando tabla: {Table}", tableConfig.TableName);
                
                    var exportResult = await exporter.ExportTableRawAsync(
                        tableConfig.TableName,
                        tableConfig.IncrementalColumn,
                        tableConfig.LastExportTimestamp
                    );
                
                    if (exportResult.Success)
                    {
                        Log.Information("Exportación exitosa: {File}, {Rows} registros",
                            Path.GetFileName(exportResult.FilePath), exportResult.RowsExported);
                
                        // Subir a SharePoint
                        var uploadSuccess = await uploader.UploadFileAsync(
                            exportResult.FilePath,
                            $"/Clients/{config.FarmaciaId}/Raw/"
                        );
                
                        if (uploadSuccess)
                        {
                            Log.Information("Archivo subido correctamente a SharePoint");
                
                            await uploader.ValidateUploadAsync(
                                $"/Clients/{config.FarmaciaId}/Raw/{Path.GetFileName(exportResult.FilePath)}",
                                exportResult.Sha256Hash
                            );
                
                            // Actualizar timestamp de última exportación para esta tabla
                            tableConfig.LastExportTimestamp = DateTime.UtcNow;
                        }
                        else
                        {
                            Log.Error("Error al subir archivo de tabla {Table}", tableConfig.TableName);
                            allSuccess = false;
                        }
                    }
                    else
                    {
                        Log.Error("Error en exportación de tabla {Table}: {Error}",
                            tableConfig.TableName, exportResult.ErrorDetails);
                        allSuccess = false;
                    }
                }
                
                // Guardar config actualizado con nuevos timestamps
                await configManager.SaveConfigAsync(config);
                
                // Actualizar estado global
                if (allSuccess)
                {
                    await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, true);
                    await statusChecker.UpdateLastActivityAsync(config.FarmaciaId);
                    Log.Information("Todas las exportaciones completadas exitosamente");
                }
                else
                {
                    await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, false, "partial_export_failed");
                    Log.Warning("Algunas exportaciones fallaron");
                }

                // Probar conexión antes de exportar
                Log.Information("Verificando conexión a base de datos...");
                var connectionOk = await exporter.TestConnectionAsync(connectionString);
                
                if (!connectionOk)
                {
                    Log.Error("No se pudo conectar a la base de datos");
                    await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, false, "connection_failed");
                    return 1;
                }

                Log.Information("Conexión a base de datos verificada correctamente");

                // Realizar exportación
                var exportResult = await exporter.ExportDataAsync(lastExportTs);
            
                if (exportResult.Success)
                {
                    Log.Information("Exportación exitosa: {File}, {Rows} registros", 
                        Path.GetFileName(exportResult.FilePath), exportResult.RowsExported);
            
                    // Subir a SharePoint
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
                        
                        await uploader.ValidateUploadAsync(
                            $"/Clients/{config.FarmaciaId}/Exports/{Path.GetFileName(exportResult.FilePath)}",
                            exportResult.Sha256Hash
                        );
            
                        await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, true);
            
                        // Actualizar última actividad en PostgreSQL
                        await statusChecker.UpdateLastActivityAsync(config.FarmaciaId);

                        Log.Information("Proceso completado exitosamente");
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
