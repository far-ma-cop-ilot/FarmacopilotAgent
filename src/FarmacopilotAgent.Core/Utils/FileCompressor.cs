using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Serilog;

namespace FarmacopilotAgent.Core.Utils
{
    public class FileCompressor
    {
        private readonly ILogger _logger;
        private const long MaxFileSizeBytes = 100 * 1024 * 1024; // 100MB
        private const long MinSizeToCompress = 10 * 1024 * 1024; // 10MB

        public FileCompressor(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Comprime archivo con gzip si supera umbral.
        /// IMPORTANTE: Cierra todos los streams antes de eliminar el original.
        /// </summary>
        public async Task<string> CompressIfNeededAsync(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            
            if (fileInfo.Length < MinSizeToCompress)
            {
                _logger.Debug("Archivo {File} no requiere compresión ({Size} KB)", 
                    fileInfo.Name, fileInfo.Length / 1024);
                return filePath;
            }
            
            var compressedPath = filePath + ".gz";
            
            _logger.Information("Comprimiendo {File} ({Size:N0} KB)...", 
                fileInfo.Name, fileInfo.Length / 1024);

            try
            {
                // Usar bloques using separados para asegurar que se cierran los streams
                using (var originalStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                using (var compressedStream = new FileStream(compressedPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal))
                {
                    await originalStream.CopyToAsync(gzipStream);
                    // Flush explícito antes de cerrar
                    await gzipStream.FlushAsync();
                }
                
                // Ahora los streams están cerrados, podemos leer el tamaño
                var compressedInfo = new FileInfo(compressedPath);
                var compressionRatio = (1 - (double)compressedInfo.Length / fileInfo.Length) * 100;
                
                _logger.Information("Archivo comprimido: {Original} KB → {Compressed} KB ({Ratio:N1}% reducción)",
                    fileInfo.Length / 1024, 
                    compressedInfo.Length / 1024,
                    compressionRatio);
                
                // Pequeña pausa para asegurar que el SO libere el archivo
                await Task.Delay(100);
                
                // Eliminar original solo si la compresión fue exitosa
                if (File.Exists(compressedPath) && compressedInfo.Length > 0)
                {
                    try
                    {
                        File.Delete(filePath);
                        _logger.Debug("Archivo original eliminado: {File}", filePath);
                    }
                    catch (IOException ex)
                    {
                        _logger.Warning("No se pudo eliminar archivo original (no crítico): {Error}", ex.Message);
                        // No es crítico, el archivo comprimido existe
                    }
                }
                
                return compressedPath;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error al comprimir archivo {File}", filePath);
                
                // Si falla la compresión, eliminar el archivo .gz parcial si existe
                if (File.Exists(compressedPath))
                {
                    try { File.Delete(compressedPath); } catch { }
                }
                
                // Retornar el archivo original sin comprimir
                return filePath;
            }
        }

        /// <summary>
        /// Particiona archivo grande en múltiples archivos más pequeños
        /// </summary>
        public async Task<List<string>> PartitionIfNeededAsync(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            var partitions = new List<string>();
            
            if (fileInfo.Length <= MaxFileSizeBytes)
            {
                partitions.Add(filePath);
                return partitions;
            }
            
            _logger.Warning("Archivo {File} supera 100MB ({Size:N0} MB). Particionando...", 
                fileInfo.Name, fileInfo.Length / (1024 * 1024));
            
            var totalLines = await CountLinesAsync(filePath);
            var linesPerPartition = (int)(totalLines * MaxFileSizeBytes / (double)fileInfo.Length);
            
            // Mínimo 1000 líneas por partición
            linesPerPartition = Math.Max(linesPerPartition, 1000);
            
            _logger.Information("Total líneas: {Total}. Líneas por partición: ~{PerPart}", 
                totalLines, linesPerPartition);
            
            string? header = null;
            var partNumber = 1;
            var currentLines = 0;
            StreamWriter? writer = null;
            string? currentPartPath = null;
            
            try
            {
                using var reader = new StreamReader(filePath);
                header = await reader.ReadLineAsync();
                
                while (!reader.EndOfStream)
                {
                    if (currentLines == 0 || writer == null)
                    {
                        // Cerrar writer anterior si existe
                        if (writer != null)
                        {
                            await writer.FlushAsync();
                            writer.Dispose();
                            writer = null;
                            await Task.Delay(50); // Pequeña pausa
                        }
                        
                        currentPartPath = filePath.Replace(".csv", $"_part{partNumber:D3}.csv");
                        writer = new StreamWriter(currentPartPath, false, System.Text.Encoding.UTF8);
                        
                        // Header en cada partición
                        if (header != null)
                        {
                            await writer.WriteLineAsync(header);
                        }
                        
                        partitions.Add(currentPartPath);
                        
                        _logger.Information("Creando partición {Part}: {File}", 
                            partNumber, Path.GetFileName(currentPartPath));
                        
                        currentLines = 0;
                    }
                    
                    var line = await reader.ReadLineAsync();
                    if (line != null && writer != null)
                    {
                        await writer.WriteLineAsync(line);
                        currentLines++;
                        
                        if (currentLines >= linesPerPartition)
                        {
                            currentLines = 0;
                            partNumber++;
                        }
                    }
                }
            }
            finally
            {
                // Asegurar que el último writer se cierre
                if (writer != null)
                {
                    await writer.FlushAsync();
                    writer.Dispose();
                }
            }
            
            // Pausa antes de eliminar el original
            await Task.Delay(100);
            
            // Eliminar archivo original
            try
            {
                File.Delete(filePath);
                _logger.Debug("Archivo original eliminado después de particionar");
            }
            catch (IOException ex)
            {
                _logger.Warning("No se pudo eliminar archivo original después de particionar: {Error}", ex.Message);
            }
            
            _logger.Information("Archivo particionado en {Count} archivos", partitions.Count);
            
            return partitions;
        }

        private async Task<int> CountLinesAsync(string filePath)
        {
            var lineCount = 0;
            using var reader = new StreamReader(filePath);
            
            while (await reader.ReadLineAsync() != null)
            {
                lineCount++;
            }
            
            return lineCount;
        }
    }
}