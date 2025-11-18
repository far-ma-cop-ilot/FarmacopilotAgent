using System;
using System.Text.Json.Serialization;

namespace FarmacopilotAgent.Core.Models
{
    namespace FarmacopilotAgent.Core.Models
{
    public class AgentConfig
    {
        [JsonPropertyName("farmacia_id")]
        public string FarmaciaId { get; set; } = string.Empty;

        [JsonPropertyName("erp_type")]
        public string ErpType { get; set; } = string.Empty; // "nixfarma" o "farmatic"

        [JsonPropertyName("erp_version")]
        public string ErpVersion { get; set; } = string.Empty;

        [JsonPropertyName("db_type")]
        public string DbType { get; set; } = string.Empty; // "oracle" o "sqlserver"

        [JsonPropertyName("db_connection_encrypted")]
        public string DbConnectionEncrypted { get; set; } = string.Empty;

        [JsonPropertyName("sharepoint_site_id")]
        public string SharePointSiteId { get; set; } = string.Empty; // Del instalador

        [JsonPropertyName("tables_to_export")]
        public List<TableExportConfig> TablesToExport { get; set; } = new();

        [JsonPropertyName("export_schedule")]
        public string ExportSchedule { get; set; } = "03:00";

        [JsonPropertyName("last_install_ts")]
        public DateTime LastInstallTimestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("agent_version")]
        public string AgentVersion { get; set; } = "1.0.0";

        [JsonPropertyName("postgres_connection_encrypted")]
        public string PostgresConnectionEncrypted { get; set; } = string.Empty;
    }

    public class TableExportConfig
    {
        [JsonPropertyName("table_name")]
        public string TableName { get; set; } = string.Empty;

        [JsonPropertyName("incremental_column")]
        public string? IncrementalColumn { get; set; } // null = full export

        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        [JsonPropertyName("last_export_ts")]
        public DateTime? LastExportTimestamp { get; set; }
    }
}

        [JsonPropertyName("graph_client_secret_encrypted")]
        public string GraphClientSecretEncrypted { get; set; } = string.Empty;

        [JsonPropertyName("sharepoint_site_id")]
        public string SharePointSiteId { get; set; } = string.Empty;

        [JsonPropertyName("export_schedule")]
        public string ExportSchedule { get; set; } = "03:00";

        [JsonPropertyName("last_install_ts")]
        public DateTime LastInstallTimestamp { get; set; } = DateTime.UtcNow;

        [JsonPropertyName("agent_version")]
        public string AgentVersion { get; set; } = "1.0.0";

        [JsonPropertyName("telemetry_enabled")]
        public bool TelemetryEnabled { get; set; } = false;

        [JsonPropertyName("api_base_url")]
        public string ApiBaseUrl { get; set; } = "https://api.farmacopilot.com";

        [JsonPropertyName("postgres_connection_encrypted")]
        public string PostgresConnectionEncrypted { get; set; } = string.Empty;
            }

    // Resto de modelos sin cambios
    public class ErpInfo
    {
        public string ErpType { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string InstallPath { get; set; } = string.Empty;
        public string DatabaseType { get; set; } = string.Empty;
        public string DetectionMethod { get; set; } = string.Empty;
    }

    public class ExportResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int RowsExported { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public string Sha256Hash { get; set; } = string.Empty;
        public DateTime ExportTimestamp { get; set; }
        public string ErrorDetails { get; set; } = string.Empty;
    }

    public class ClientStatus
    {
        [JsonPropertyName("farmacia_id")]
        public string FarmaciaId { get; set; } = string.Empty;

        [JsonPropertyName("active")]
        public bool Active { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = string.Empty;

        [JsonPropertyName("last_check")]
        public DateTime LastCheck { get; set; }
    }

    public class LastExport
    {
        [JsonPropertyName("farmacia_id")]
        public string FarmaciaId { get; set; } = string.Empty;

        [JsonPropertyName("last_export_ts")]
        public DateTime LastExportTimestamp { get; set; }

        [JsonPropertyName("last_success")]
        public bool LastSuccess { get; set; }

        [JsonPropertyName("last_error")]
        public string LastError { get; set; } = string.Empty;
    }
}
