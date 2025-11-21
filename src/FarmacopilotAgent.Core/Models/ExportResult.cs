using System;

namespace FarmacopilotAgent.Core.Models
{
    /// <summary>
    /// Resultado de una operación de exportación
    /// </summary>
    public class ExportResult
    {
        /// <summary>
        /// Indica si la exportación fue exitosa
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Mensaje descriptivo del resultado
        /// </summary>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Número de registros exportados
        /// </summary>
        public int RowsExported { get; set; }

        /// <summary>
        /// Ruta del archivo CSV generado
        /// </summary>
        public string FilePath { get; set; } = string.Empty;

        /// <summary>
        /// Hash SHA256 del archivo para validación de integridad
        /// </summary>
        public string Sha256Hash { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp de la exportación
        /// </summary>
        public DateTime ExportTimestamp { get; set; }

        /// <summary>
        /// Detalles del error (si la exportación falló)
        /// </summary>
        public string? ErrorDetails { get; set; }

        /// <summary>
        /// Tipo de exportación realizada: "full" o "incremental"
        /// </summary>
        public string ExportType { get; set; } = "full";

        /// <summary>
        /// Nombre de la tabla exportada
        /// </summary>
        public string? TableName { get; set; }

        /// <summary>
        /// Tamaño del archivo en bytes
        /// </summary>
        public long FileSizeBytes { get; set; }

        /// <summary>
        /// Duración de la exportación en milisegundos
        /// </summary>
        public long DurationMs { get; set; }

        /// <summary>
        /// Indica si el archivo fue subido exitosamente a SharePoint
        /// </summary>
        public bool UploadedToSharePoint { get; set; }

        /// <summary>
        /// Indica si la validación SHA256 post-upload fue exitosa
        /// </summary>
        public bool ValidationPassed { get; set; }

        /// <summary>
        /// Número de columnas en el CSV exportado
        /// </summary>
        public int ColumnCount { get; set; }

        /// <summary>
        /// Primera fecha encontrada en los datos (para tracking)
        /// </summary>
        public DateTime? FirstRecordDate { get; set; }

        /// <summary>
        /// Última fecha encontrada en los datos (para tracking)
        /// </summary>
        public DateTime? LastRecordDate { get; set; }
        /// <summary>
        /// Lista de tablas que fallaron durante la exportación
        /// </summary>
        public List<string>? TablesFailed { get; set; }
    }
}
