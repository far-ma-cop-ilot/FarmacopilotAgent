using System;

namespace FarmacopilotAgent.Core.Models
{
    /// <summary>
    /// Información de la última exportación realizada
    /// Se persiste en last_export.json para control de estado
    /// </summary>
    public class LastExport
    {
        /// <summary>
        /// Identificador de la farmacia
        /// </summary>
        public string FarmaciaId { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp de la última exportación
        /// </summary>
        public DateTime LastExportTimestamp { get; set; }

        /// <summary>
        /// Indica si la última exportación fue exitosa
        /// </summary>
        public bool LastSuccess { get; set; }

        /// <summary>
        /// Mensaje de error de la última exportación (si falló)
        /// </summary>
        public string LastError { get; set; } = string.Empty;

        /// <summary>
        /// Número de registros exportados en la última ejecución
        /// </summary>
        public int TotalRowsExported { get; set; }

        /// <summary>
        /// Número de tablas procesadas
        /// </summary>
        public int TablesProcessed { get; set; }

        /// <summary>
        /// Número de tablas que fallaron
        /// </summary>
        public int TablesFailed { get; set; }

        /// <summary>
        /// Duración de la última exportación en segundos
        /// </summary>
        public int DurationSeconds { get; set; }

        /// <summary>
        /// Versión del agente que realizó la exportación
        /// </summary>
        public string AgentVersion { get; set; } = string.Empty;

        /// <summary>
        /// Número de intentos realizados (útil para detectar fallos recurrentes)
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Timestamp del próximo intento programado
        /// </summary>
        public DateTime? NextScheduledExport { get; set; }
    }
}
