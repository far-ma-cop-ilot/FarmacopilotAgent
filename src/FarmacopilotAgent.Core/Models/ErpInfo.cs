using System;
using System.Collections.Generic;

namespace FarmacopilotAgent.Core.Models
{
    /// <summary>
    /// Información del ERP detectado en el sistema
    /// </summary>
    public class ErpInfo
    {
        /// <summary>
        /// Tipo de ERP: "Nixfarma" o "Farmatic"
        /// </summary>
        public string ErpType { get; set; } = string.Empty;

        /// <summary>
        /// Versión exacta del ERP (ej: "9.1.8.20", "16.00.9837")
        /// </summary>
        public string Version { get; set; } = string.Empty;

        /// <summary>
        /// Ruta de instalación del ERP
        /// </summary>
        public string InstallPath { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de base de datos: "Oracle" o "SQL Server"
        /// </summary>
        public string DatabaseType { get; set; } = string.Empty;

        /// <summary>
        /// Versión de la base de datos detectada
        /// </summary>
        public string? DatabaseVersion { get; set; }

        /// <summary>
        /// Método usado para detectar el ERP: "registry", "file_system", "manual", "database_query"
        /// </summary>
        public string DetectionMethod { get; set; } = string.Empty;

        /// <summary>
        /// Número de serie del ERP (si está disponible)
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// Nombre de la instancia de base de datos
        /// </summary>
        public string? DatabaseInstance { get; set; }

        /// <summary>
        /// Nombre de la base de datos
        /// </summary>
        public string? DatabaseName { get; set; }

        /// <summary>
        /// Lista de tablas detectadas en la base de datos
        /// </summary>
        public List<string> DetectedTables { get; set; } = new();

        /// <summary>
        /// Indica si la instalación es compatible con el agente
        /// </summary>
        public bool IsCompatible { get; set; } = true;

        /// <summary>
        /// Razones de incompatibilidad (si las hay)
        /// </summary>
        public List<string> IncompatibilityReasons { get; set; } = new();

        /// <summary>
        /// Timestamp de la detección
        /// </summary>
        public DateTime DetectionTimestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Información adicional del entorno (opcional)
        /// </summary>
        public Dictionary<string, string> AdditionalInfo { get; set; } = new();

        /// <summary>
        /// Indica si se requiere configuración manual
        /// </summary>
        public bool RequiresManualConfiguration { get; set; }

        /// <summary>
        /// Nivel de confianza en la detección (0-100)
        /// </summary>
        public int ConfidenceLevel { get; set; } = 100;
    }
}
