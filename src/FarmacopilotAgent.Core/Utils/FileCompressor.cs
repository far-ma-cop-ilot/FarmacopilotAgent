using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Serilog;

namespace FarmacopilotAgent.Core.Utils
{
    public class FileCompressor
    {
        private readonly ILogger _logger;
        private const int MaxFileSizeBytes = 100 * 1024 * 1024; // 100MB

        public FileCompressor(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Comprime archivo con gzip si supera umbral
        /// </summary>
        public async Task<string> CompressIfNeededAsync(string filePath)
        {
            var fileInfo = new FileInfo(filePath);
            
            if (fileInfo.Length < 10 * 1024 * 1024) // <10MB no comprimir
            {
                _logger.Debug("Archivo {File} no requiere compresión ({Size} KB)", 
                    fileInfo.Name, fileInfo.Length / 1024);
                return filePath;
            }
            
            var compressedPath = filePath + ".gz";
            
            _logger.Information("Comprimiendo {File} ({Size:N0} KB)...", 
                fileInfo.Name, fileInfo.Length / 1024);
            
            await using var originalStream = File.OpenRead(filePath);
            await using var compressedStream = File.Create(compressedPath);
            await using var gzipStream = new GZipStream(compressedStream, CompressionLevel.Optimal);
            
            await originalStream.CopyToAsync(gzipStream);
            
            var compressedInfo = new FileInfo(compressedPath);
            var compressionRatio = (1 - (double)compressedInfo.Length / fileInfo.Length) * 100;
            
            _logger.Information("Archivo comprimido: {Original} KB → {Compressed} KB ({Ratio:N1}% reducción)",
                fileInfo.Length / 1024, 
                compressedInfo.Length / 1024,
                compressionRatio);
            
            // Eliminar original si compresión fue exitosa
            File.Delete(filePath);
            
            return compressedPath;
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
            
            _logger.Information("Total líneas: {Total}. Líneas por partición: {PerPart}", 
                totalLines, linesPerPartition);
            
            await using var reader = new StreamReader(filePath);
            var header = await reader.ReadLineAsync();
            
            var partNumber = 1;
            var currentLines = 0;
            StreamWriter? writer = null;
            
            while (!reader.EndOfStream)
            {
                if (currentLines == 0)
                {
                    var partPath = filePath.Replace(".csv", $"_part{partNumber:D3}.csv");
                    writer = new StreamWriter(partPath);
                    await writer.WriteLineAsync(header); // Header en cada partición
                    partitions.Add(partPath);
                    
                    _logger.Information("Creando partición {Part}: {File}", 
                        partNumber, Path.GetFileName(partPath));
                }
                
                var line = await reader.ReadLineAsync();
                if (line != null)
                {
                    await writer!.WriteLineAsync(line);
                    currentLines++;
                    
                    if (currentLines >= linesPerPartition)
                    {
                        await writer.FlushAsync();
                        writer.Dispose();
                        currentLines = 0;
                        partNumber++;
                    }
                }
            }
            
            writer?.Dispose();
            
            // Eliminar archivo original
            File.Delete(filePath);
            
            _logger.Information("Archivo particionado en {Count} archivos", partitions.Count);
            
            return partitions;
        }

        private async Task<int> CountLinesAsync(string filePath)
        {
            var lineCount = 0;
            await using var reader = new StreamReader(filePath);
            
            while (await reader.ReadLineAsync() != null)
            {
                lineCount++;
            }
            
            return lineCount;
        }
    }
}
