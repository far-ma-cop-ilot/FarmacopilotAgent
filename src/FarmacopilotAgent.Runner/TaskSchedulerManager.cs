using System;
using System.Diagnostics;
using Serilog;

namespace FarmacopilotAgent.Runner
{
    public class TaskSchedulerManager
    {
        private readonly ILogger _logger;

        public TaskSchedulerManager(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Deshabilita una tarea programada de Windows
        /// </summary>
        public bool DisableTask(string taskName)
        {
            try
            {
                _logger.Information("Deshabilitando tarea programada: {TaskName}", taskName);

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Change /TN \"{taskName}\" /DISABLE",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.Error("No se pudo iniciar schtasks.exe");
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    _logger.Information("Tarea deshabilitada correctamente");
                    return true;
                }
                else
                {
                    var error = process.StandardError.ReadToEnd();
                    _logger.Warning("Error al deshabilitar tarea. Exit code: {ExitCode}, Error: {Error}", 
                        process.ExitCode, error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Excepción al deshabilitar tarea");
                return false;
            }
        }

        /// <summary>
        /// Habilita una tarea programada de Windows
        /// </summary>
        public bool EnableTask(string taskName)
        {
            try
            {
                _logger.Information("Habilitando tarea programada: {TaskName}", taskName);

                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Change /TN \"{taskName}\" /ENABLE",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                {
                    _logger.Error("No se pudo iniciar schtasks.exe");
                    return false;
                }

                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    _logger.Information("Tarea habilitada correctamente");
                    return true;
                }
                else
                {
                    var error = process.StandardError.ReadToEnd();
                    _logger.Warning("Error al habilitar tarea. Exit code: {ExitCode}, Error: {Error}", 
                        process.ExitCode, error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Excepción al habilitar tarea");
                return false;
            }
        }

        /// <summary>
        /// Verifica si una tarea programada existe
        /// </summary>
        public bool TaskExists(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = $"/Query /TN \"{taskName}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al verificar existencia de tarea");
                return false;
            }
        }
    }
}
