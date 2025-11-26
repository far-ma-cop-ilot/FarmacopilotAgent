using System;
using System.Data.Common;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Oracle.ManagedDataAccess.Client;
using Serilog;
using Serilog.Events;
using FarmacopilotAgent.Core.Models;
using FarmacopilotAgent.Core.Utils;
using FarmacopilotAgent.Core.Security;
using FarmacopilotAgent.Core.Database;
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

            var stopwatch = Stopwatch.StartNew();

            // Procesar exportaciones fallidas pendientes
            var failedQueue = new FailedExportQueue(BasePath);
            if (failedQueue.Count > 0)
            {
                Log.Information("Procesando {Count} exportaciones pendientes de reintentos", failedQueue.Count);
                await ProcessFailedExportsAsync(failedQueue);
            }

            try
            {
                Log.Information("═══════════════════════════════════════════════════════════");
                Log.Information("   Farmacopilot Agent v1.0.0 - Exportación automática ERP");
                Log.Information("═══════════════════════════════════════════════════════════");
                Log.Information("Inicio: {Timestamp}", DateTime.Now);
                
                // Verificar actualizaciones (solo si no es --verify-update)
                if (!args.Contains("--verify-update"))
                {
                    var updater = new AutoUpdater(BasePath, Log.Logger);
                    var updated = await updater.CheckAndUpdateAsync();
                    
                    if (updated)
                    {
                        Log.Information("Actualización aplicada. El agente se reiniciará.");
                        return 0; // Salir para que el script de actualización reinicie
                    }
                }
            
                // Cargar configuración
                var configManager = new ConfigManager(BasePath, Log.Logger);
                var config = await configManager.LoadConfigAsync();
            
                if (config == null)
                {
                    Log.Error("No se pudo cargar la configuración desde {Path}", Path.Combine(BasePath, "config.json"));
                    return 1;
                }

                Log.Information("✓ Configuración cargada");
                Log.Information("  - Farmacia ID: {FarmaciaId}", config.FarmaciaId);
                Log.Information("  - ERP: {ErpType} v{Version}", config.ErpType, config.ErpVersion);
                Log.Information("  - Base de datos: {DbType}", config.DbType);
                Log.Information("  - Tablas habilitadas: {Count}", config.TablesToExport.Count(t => t.Enabled));
            
                // Crear RetryManager
                var retryManager = new RetryManager(Log.Logger);

                // Test Mode: Validacion PostgreSQL deshabilitado
                Log.Warning("⚠️ TEST MODE: Validación PostgreSQL deshabilitada");
                Log.Information("Procediendo directamente a detección de ERP y exportación...");
                ClientStatus? clientStatus = null;
                /*
                // Verificar estado del cliente desde PostgreSQL con reintentos
                Log.Information("Verificando estado del cliente en PostgreSQL...");
                var postgresConnection = configManager.GetDecryptedPostgresConnection(config);
                var statusChecker = new PostgresStatusChecker(postgresConnection, Log.Logger);
                
                ClientStatus? clientStatus = await retryManager.ExecuteAsync(async () =>
                    await statusChecker.GetClientStatusAsync(config.FarmaciaId));
                
                if (clientStatus == null)
                {
                    Log.Error("No se pudo verificar estado del cliente después de reintentos");
                    Log.Warning("El agente continuará con la exportación, pero puede estar desactualizado");
                    // No abortamos - permitimos exportación offline
                }
                else if (!clientStatus.Active)
                {
                    Log.Warning("═══════════════════════════════════════════════════════════");
                    Log.Warning("   CLIENTE INACTIVO");
                    Log.Warning("═══════════════════════════════════════════════════════════");
                    Log.Warning("Motivo: {Reason}", clientStatus.Reason);
                    
                    var taskManager = new TaskSchedulerManager(Log.Logger);
                    var taskDisabled = taskManager.DisableTask("Farmacopilot_Export");
                    
                    if (taskDisabled)
                    {
                        Log.Information("Tarea programada deshabilitada correctamente");
                    }
                    else
                    {
                        Log.Warning("No se pudo deshabilitar la tarea programada");
                    }
                    
                    return 0;
                }
                else
                {
                    Log.Information("✓ Cliente activo - Plan: {Plan}", clientStatus.Plan ?? "N/A");
                    if (clientStatus.SubscriptionExpiresAt.HasValue)
                    {
                        var daysRemaining = (clientStatus.SubscriptionExpiresAt.Value - DateTime.UtcNow).Days;
                        Log.Information("  - Vencimiento: {Date} ({Days} días)", 
                            clientStatus.SubscriptionExpiresAt.Value.ToString("yyyy-MM-dd"), daysRemaining);
                    }
                }
                */
                // Obtener información de última exportación
                var lastExportManager = new LastExportManager(BasePath, Log.Logger);
                var lastExport = await lastExportManager.GetLastExportTimestampAsync();
                
                if (lastExport.HasValue)
                {
                    Log.Information("Última exportación exitosa: {Date}", lastExport.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                    Log.Information("Tipo de exportación: INCREMENTAL");
                }
                else
                {
                    Log.Information("Primera exportación o exportación previa falló");
                    Log.Information("Tipo de exportación: COMPLETA");
                }
                
                // Preparar conexión a base de datos según tipo de ERP
                Log.Information("───────────────────────────────────────────────────────────");
                Log.Information("Conectando a base de datos del ERP...");
                
                var connectionString = configManager.GetDecryptedConnectionString(config);
                var outputPath = Path.Combine(BasePath, "staging");
                Directory.CreateDirectory(outputPath);
                
                DbConnection connection;
                
                if (config.DbType.Equals("oracle", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Tipo: Oracle Database 11g (Nixfarma)");
                    connection = new OracleConnection(connectionString);
                }
                else if (config.DbType.Equals("sqlserver", StringComparison.OrdinalIgnoreCase))
                {
                    Log.Information("Tipo: SQL Server 2019 (Farmatic)");
                    connection = new SqlConnection(connectionString);
                }
                else
                {
                    Log.Error("Tipo de base de datos no soportado: {DbType}", config.DbType);
                    throw new InvalidOperationException($"Base de datos no soportada: {config.DbType}");
                }
                
                // Conectar con reintentos
                await retryManager.ExecuteDbAsync(async () =>
                {
                    await connection.OpenAsync();
                    return true;
                });
                
                Log.Information("✓ Conexión establecida correctamente");
                
                // Crear exportador RAW genérico
                var exporter = new RawExporter(
                    connection, 
                    outputPath, 
                    config.FarmaciaId, 
                    Log.Logger
                );
                
                // Verificar conectividad con reintentos
                var connectionOk = await retryManager.ExecuteDbAsync(async () =>
                    await exporter.TestConnectionAsync(connectionString));
                
                if (!connectionOk)
                {
                    Log.Error("Falló la verificación de conectividad a la base de datos");
                    await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, false, "connection_failed");
                    return 1;
                }
                
                // Cargar credenciales Graph API
                Log.Information("───────────────────────────────────────────────────────────");
                Log.Information("Inicializando uploader de SharePoint...");
                
                var credentialsProvider = new GraphCredentialsProvider(BasePath, Log.Logger);
                var graphCredentials = credentialsProvider.GetCredentials();
                
                var uploader = new SharePointGraphUploader(
                    config.FarmaciaId,
                    graphCredentials,
                    Log.Logger
                );
                
                // Configurar tipo de ERP para determinar carpeta en SharePoint
                uploader.SetErpType(config.ErpType);
                
                Log.Information("✓ Credenciales Graph API cargadas");
                Log.Information("  - Carpeta destino: /{ErpFolder}/{FarmaciaId}/", 
                    config.ErpType == "nixfarma" ? "Nixfarma" : "Farmatic", 
                    config.FarmaciaId);
                
                // EXPORTAR CADA TABLA (Estrategia RAW: SELECT * sin transformaciones)
                Log.Information("═══════════════════════════════════════════════════════════");
                Log.Information("   INICIANDO EXPORTACIÓN DE TABLAS");
                Log.Information("═══════════════════════════════════════════════════════════");
                
                var allSuccess = true;
                var totalRowsExported = 0;
                var tablesProcessed = 0;
                var tablesFailed = 0;
                
                var tablesToExport = config.TablesToExport
                    .Where(t => t.Enabled)
                    .OrderBy(t => t.Priority)
                    .ToList();
                
                Log.Information("Total de tablas a procesar: {Count}", tablesToExport.Count);
                
                foreach (var tableConfig in tablesToExport)
                {
                    Log.Information("───────────────────────────────────────────────────────────");
                    Log.Information("Tabla: {TableName}", tableConfig.TableName);
                    
                    try
                    {
                        var tableStopwatch = Stopwatch.StartNew();
                        
                        // ✅ FASE 4: Exportar con compresión y particionado automático
                        var exportResults = await retryManager.ExecuteDbAsync(async () =>
                            await exporter.ExportTableRawWithCompressionAsync(
                                tableConfig.TableName,
                                tableConfig.IncrementalColumn,
                                tableConfig.LastExportTimestamp
                            ));
                        
                        tableStopwatch.Stop();
                        
                        // ✅ FASE 4: Manejar múltiples archivos (por particionado)
                        var allPartsSuccessful = true;
                        var totalRowsThisTable = 0;
                        
                        foreach (var exportResult in exportResults)
                        {
                            exportResult.DurationMs = tableStopwatch.ElapsedMilliseconds / exportResults.Count;
                            
                            if (exportResult.Success && exportResult.RowsExported > 0)
                            {
                                Log.Information("✓ Exportación exitosa");
                                Log.Information("  - Archivo: {File}", Path.GetFileName(exportResult.FilePath));
                                Log.Information("  - Registros: {Rows:N0}", exportResult.RowsExported);
                                Log.Information("  - Tamaño: {Size:N0} KB", exportResult.FileSizeBytes / 1024);
                                
                                totalRowsThisTable += exportResult.RowsExported;
                        
                                // Subir a SharePoint con reintentos
                                Log.Information("  Subiendo a SharePoint...");
                                var uploadSuccess = await retryManager.ExecuteUploadAsync(async () =>
                                    await uploader.UploadFileAsync(
                                        exportResult.FilePath,
                                        $"/Clients/{config.FarmaciaId}/Raw/"
                                    ));
                        
                                if (uploadSuccess)
                                {
                                    exportResult.UploadedToSharePoint = true;
                                    Log.Information("  ✓ Archivo subido correctamente");
                        
                                    // Validar integridad SHA256 con reintentos
                                    var validationSuccess = await retryManager.ExecuteAsync(async () =>
                                        await uploader.ValidateUploadAsync(
                                            $"/Clients/{config.FarmaciaId}/Raw/{Path.GetFileName(exportResult.FilePath)}",
                                            exportResult.Sha256Hash
                                        ));
                                    
                                    if (validationSuccess)
                                    {
                                        exportResult.ValidationPassed = true;
                                        Log.Information("  ✓ Validación SHA256 exitosa");
                                        
                                        // Limpiar archivo local tras validación exitosa
                                        try
                                        {
                                            File.Delete(exportResult.FilePath);
                                            if (File.Exists(exportResult.FilePath + ".sha256"))
                                                File.Delete(exportResult.FilePath + ".sha256");
                                            Log.Debug("  Archivos locales eliminados");
                                        }
                                        catch (Exception ex)
                                        {
                                            Log.Warning(ex, "  No se pudieron eliminar archivos locales (no crítico)");
                                        }
                                    }
                                    else
                                    {
                                        Log.Warning("  ⚠ Validación SHA256 falló");
                                        allPartsSuccessful = false;
                                    }
                                }
                                else
                                {
                                    // Encolar para reintento diferido
                                    Log.Error("  ✗ Error al subir archivo después de reintentos");
                                    await failedQueue.EnqueueAsync(
                                        tableConfig.TableName,
                                        tableConfig.LastExportTimestamp,
                                        "Upload failed after retries"
                                    );
                                    allPartsSuccessful = false;
                                }
                            }
                            else if (exportResult.RowsExported == 0)
                            {
                                Log.Information("  ℹ No hay datos nuevos para exportar");
                                // No es un error - simplemente no hay cambios
                            }
                            else
                            {
                                Log.Error("  ✗ Error en exportación: {Error}", exportResult.ErrorDetails);
                                allPartsSuccessful = false;
                            }
                        }
                        
                        // ✅ FASE 4: Actualizar timestamp solo si TODAS las partes fueron exitosas
                        if (allPartsSuccessful && totalRowsThisTable > 0)
                        {
                            tableConfig.LastExportTimestamp = DateTime.UtcNow;
                            tablesProcessed++;
                            totalRowsExported += totalRowsThisTable;
                        }
                        else if (!allPartsSuccessful)
                        {
                            await failedQueue.EnqueueAsync(
                                tableConfig.TableName,
                                tableConfig.LastExportTimestamp,
                                "Partial upload failure"
                            );
                            tablesFailed++;
                            allSuccess = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "  ✗ Excepción durante procesamiento de tabla {Table}", tableConfig.TableName);
                        await failedQueue.EnqueueAsync(
                            tableConfig.TableName,
                            tableConfig.LastExportTimestamp,
                            ex.Message
                        );
                        tablesFailed++;
                        allSuccess = false;
                    }
                }
                
                // Guardar config actualizado con nuevos timestamps
                await configManager.SaveConfigAsync(config);
                
                stopwatch.Stop();
                
                // Actualizar estado global
                Log.Information("═══════════════════════════════════════════════════════════");
                Log.Information("   RESUMEN DE EXPORTACIÓN");
                Log.Information("═══════════════════════════════════════════════════════════");
                Log.Information("Tablas procesadas exitosamente: {Success}", tablesProcessed);
                Log.Information("Tablas fallidas: {Failed}", tablesFailed);
                Log.Information("Total de registros exportados: {Total:N0}", totalRowsExported);
                Log.Information("Exportaciones pendientes en queue: {Pending}", failedQueue.Count);
                Log.Information("Duración total: {Duration:N1}s", stopwatch.Elapsed.TotalSeconds);
                
                if (allSuccess)
                {
                    await lastExportManager.UpdateLastExportAsync(config.FarmaciaId, true);
                    
                    if (clientStatus != null)
                    {
                        await statusChecker.UpdateLastActivityAsync(config.FarmaciaId);
                    }
                    
                    Log.Information("Estado: ✓ ÉXITO TOTAL");
                }
                else
                {
                    await lastExportManager.UpdateLastExportAsync(
                        config.FarmaciaId, 
                        false, 
                        $"partial_failure_{tablesFailed}_tables"
                    );
                    Log.Warning("Estado: ⚠ COMPLETADO CON ERRORES");
                }
                
                await connection.CloseAsync();
                await connection.DisposeAsync();
            
                Log.Information("═══════════════════════════════════════════════════════════");
                Log.Information("Fin: {Timestamp}", DateTime.Now);
                
                return allSuccess ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "ERROR CRÍTICO en Farmacopilot Agent");
                Log.Fatal("Duración antes del error: {Duration:N1}s", stopwatch.Elapsed.TotalSeconds);
                return 1;
            }
            finally
            {
                await Log.CloseAndFlushAsync();
            }
        }

        private static async Task ProcessFailedExportsAsync(FailedExportQueue queue)
        {
            var processedCount = 0;
            var maxToProcess = Math.Min(queue.Count, 5); // Procesar máximo 5 por ejecución

            while (processedCount < maxToProcess)
            {
                var item = await queue.DequeueAsync();
                if (item == null) break;

                try
                {
                    Log.Information("Reintentando exportación fallida: {Table} (intento {Retry})", 
                        item.TableName, item.RetryCount + 1);

                    // TODO: Re-ejecutar exportación para esta tabla específica
                    // Por ahora, solo re-encolar si no excede máximo de reintentos
                    
                    if (item.RetryCount < 3)
                    {
                        await queue.RequeueAsync(item);
                    }
                    else
                    {
                        Log.Error("Tabla {Table} excedió máximo de reintentos. Descartada.", item.TableName);
                    }

                    processedCount++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error procesando exportación fallida de {Table}", item.TableName);
                    await queue.RequeueAsync(item);
                }
            }

            if (processedCount > 0)
            {
                Log.Information("Procesadas {Count} exportaciones pendientes", processedCount);
            }
        }
    }
}
