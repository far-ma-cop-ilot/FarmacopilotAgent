using System;
using System.Collections.Generic;

namespace FarmacopilotAgent.Core.Models
{
    /// <summary>
    /// Configuración principal del agente Farmacopilot
    /// Se serializa/deserializa desde config.json en C:\FarmacopilotAgent\
    /// </summary>
    public class AgentConfig
    {
        /// <summary>
        /// Identificador único de la farmacia (ej: FAR2025001)
        /// Proporcionado durante la instalación tras el pago
        /// </summary>
        public string FarmaciaId { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de ERP detectado: "nixfarma" o "farmatic"
        /// </summary>
        public string ErpType { get; set; } = string.Empty;

        /// <summary>
        /// Versión exacta del ERP detectada
        /// - Nixfarma: "9.1.8.20"
        /// - Farmatic: "16.00.9837"
        /// </summary>
        public string ErpVersion { get; set; } = string.Empty;

        /// <summary>
        /// Tipo de base de datos detectada: "oracle" o "sqlserver"
        /// - Nixfarma 9.1.8.20 → Oracle Database 11g
        /// - Farmatic 16.00.9837 → SQL Server 2019
        /// </summary>
        public string DbType { get; set; } = string.Empty;

        /// <summary>
        /// Connection string cifrado con DPAPI para la base de datos del ERP
        /// Formato cifrado: "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA..."
        /// Formato plano (antes de cifrar):
        /// - Oracle: "Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))...)"
        /// - SQL Server: "Server=localhost\\SQLEXPRESS;Database=CGCOF;Integrated Security=true;..."
        /// </summary>
        public string DbConnectionEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// Connection string cifrado con DPAPI para PostgreSQL (validación estado cliente)
        /// Usado por PostgresStatusChecker para verificar si el cliente está activo
        /// Formato plano: "Host=db.farmacopilot.com;Port=5432;Database=farmacopilot_prod;Username=agent_user;Password=xxx"
        /// </summary>
        public string PostgresConnectionEncrypted { get; set; } = string.Empty;

        /// <summary>
        /// ID del sitio de SharePoint donde se suben los archivos CSV
        /// Obtenido de las credenciales Graph API embebidas en el instalador
        /// </summary>
        public string SharePointSiteId { get; set; } = string.Empty;

        /// <summary>
        /// Lista completa de tablas a exportar con configuración individual
        /// Cada tabla puede habilitarse/deshabilitarse y configurar extracción incremental
        /// </summary>
        public List<TableConfig> TablesToExport { get; set; } = new();

        /// <summary>
        /// Hora de ejecución programada de la tarea (formato HH:mm, ej: "03:00")
        /// Por defecto: 03:00 AM (horario local del servidor)
        /// </summary>
        public string ExportSchedule { get; set; } = "03:00";

        /// <summary>
        /// Timestamp de instalación del agente (UTC)
        /// Registrado durante la primera ejecución del instalador
        /// </summary>
        public DateTime LastInstallTs { get; set; }

        /// <summary>
        /// Versión del agente instalada actualmente
        /// Formato: "1.0.0"
        /// </summary>
        public string AgentVersion { get; set; } = "1.0.0";

        /// <summary>
        /// Path de instalación del ERP (opcional, para referencia y diagnóstico)
        /// Ejemplos:
        /// - Nixfarma: "C:\Program Files\Pulso Informatica\Nixfarma"
        /// - Farmatic: "C:\Program Files\Consoft\Farmatic"
        /// </summary>
        public string? ErpInstallPath { get; set; }

        /// <summary>
        /// Método de detección utilizado durante la instalación
        /// Valores posibles: "registry", "manual", "auto", "file_system"
        /// </summary>
        public string? DetectionMethod { get; set; }

        /// <summary>
        /// Número de serie del ERP (si está disponible)
        /// - Nixfarma: "ñf091414"
        /// - Farmatic: "808011255"
        /// </summary>
        public string? SerialNumber { get; set; }

        /// <summary>
        /// Nombre de la base de datos del ERP
        /// - Nixfarma: "NIXFARMA" (Oracle)
        /// - Farmatic: "CGCOF" (SQL Server)
        /// </summary>
        public string? DatabaseName { get; set; }
    }

    /// <summary>
    /// Configuración de una tabla individual a exportar
    /// Permite control granular por tabla (habilitación, incremental, prioridad)
    /// </summary>
    public class TableConfig
    {
        /// <summary>
        /// Nombre EXACTO de la tabla en la base de datos
        /// IMPORTANTE: Respetar mayúsculas/minúsculas y espacios
        /// 
        /// Nixfarma (Oracle - UPPERCASE):
        /// - "AB_ARTICULOS_FICHA_E"
        /// - "AH_VENTAS"
        /// - "AH_VENTA_LINEAS"
        /// 
        /// Farmatic (SQL Server - lowercase con espacios):
        /// - "ventas"
        /// - "linea venta"  ← Notar el espacio
        /// - "articu"
        /// </summary>
        public string TableName { get; set; } = string.Empty;

        /// <summary>
        /// Columna a usar para extracción incremental (null = full export siempre)
        /// Debe ser una columna de tipo DATE/DATETIME/TIMESTAMP
        /// 
        /// Ejemplos:
        /// - Nixfarma: "FECHA", "FECHA_VENTA", "FECHA_MODIFICACION"
        /// - Farmatic: "fecha", "fecha_venta", "fecha_recep"
        /// 
        /// Si es null, siempre se hace SELECT * completo (recomendado para tablas maestras pequeñas)
        /// </summary>
        public string? IncrementalColumn { get; set; }

        /// <summary>
        /// Indica si esta tabla está habilitada para exportación
        /// Útil para deshabilitar temporalmente tablas sin eliminar la configuración
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Timestamp de la última exportación exitosa (para extracción incremental)
        /// Actualizado automáticamente tras cada exportación validada
        /// Si es null, se hace full export en la primera ejecución
        /// </summary>
        public DateTime? LastExportTimestamp { get; set; }

        /// <summary>
        /// Prioridad de exportación (menor número = mayor prioridad)
        /// Las tablas se procesan en orden de prioridad ascendente
        /// 
        /// Sugerencias:
        /// - 10: Tablas críticas (ventas, linea venta)
        /// - 50: Tablas importantes (articu, proveedor)
        /// - 100: Tablas auxiliares (default)
        /// </summary>
        public int Priority { get; set; } = 100;

        /// <summary>
        /// Notas o comentarios sobre esta tabla (opcional)
        /// Útil para documentar el propósito o particularidades
        /// </summary>
        public string? Notes { get; set; }
    }
}
