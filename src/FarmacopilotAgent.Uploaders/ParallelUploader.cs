using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace FarmacopilotAgent.Uploaders
{
    public class ParallelUploader
    {
        private readonly SharePointGraphUploader _uploader;
        private readonly ILogger _logger;
        private const int MaxParallelUploads = 3;

        public ParallelUploader(SharePointGraphUploader uploader, ILogger logger)
        {
            _uploader = uploader;
            _logger = logger;
        }

        /// <summary>
        /// Sube múltiples archivos en paralelo
        /// </summary>
        public async Task<Dictionary<string, bool>> UploadFilesAsync(
            List<string> filePaths, 
            string remotePath)
        {
            _logger.Information("Iniciando upload paralelo de {Count} archivos (max {Max} simultáneos)", 
                filePaths.Count, MaxParallelUploads);
            
            var results = new Dictionary<string, bool>();
            var semaphore = new System.Threading.SemaphoreSlim(MaxParallelUploads);
            
            var uploadTasks = filePaths.Select(async filePath =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var fileName = System.IO.Path.GetFileName(filePath);
                    _logger.Information("Subiendo {File}...", fileName);
                    
                    var success = await _uploader.UploadFileAsync(filePath, remotePath);
                    
                    lock (results)
                    {
                        results[filePath] = success;
                    }
                    
                    if (success)
                    {
                        _logger.Information("✓ {File} subido exitosamente", fileName);
                    }
                    else
                    {
                        _logger.Error("✗ Error subiendo {File}", fileName);
                    }
                    
                    return success;
                }
                finally
                {
                    semaphore.Release();
                }
            });
            
            await Task.WhenAll(uploadTasks);
            
            var successCount = results.Values.Count(v => v);
            _logger.Information("Upload paralelo completado: {Success}/{Total} exitosos", 
                successCount, results.Count);
            
            return results;
        }
    }
}
