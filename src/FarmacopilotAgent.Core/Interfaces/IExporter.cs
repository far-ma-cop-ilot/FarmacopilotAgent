using System;
using System.Threading.Tasks;
using FarmacopilotAgent.Core.Models;

namespace FarmacopilotAgent.Core.Interfaces
{
    public interface IExporter
    {
        /// <summary>
        /// Prueba la conexión a la base de datos
        /// </summary>
        Task<bool> TestConnectionAsync(string connectionString);

        /// <summary>
        /// Exporta datos desde la última exportación (incremental) o completo si lastExportTimestamp es null
        /// </summary>
        Task<ExportResult> ExportDataAsync(DateTime? lastExportTimestamp = null);

        /// <summary>
        /// Detecta información del ERP (versión, tipo de BD, etc.)
        /// </summary>
        Task<ErpInfo> DetectErpInfoAsync();
    }

    public interface IUploader
    {
        /// <summary>
        /// Sube un archivo local a una ruta remota (SharePoint)
        /// </summary>
        Task<bool> UploadFileAsync(string localFilePath, string remotePath);

        /// <summary>
        /// Valida la integridad del archivo subido comparando SHA256
        /// </summary>
        Task<bool> ValidateUploadAsync(string remotePath, string expectedSha256);
    }

    public interface IErpDetector
    {
        /// <summary>
        /// Detecta el ERP instalado en el sistema
        /// </summary>
        ErpInfo DetectErp();

        /// <summary>
        /// Valida que la instalación del ERP sea correcta
        /// </summary>
        bool ValidateErpInstallation(ErpInfo erpInfo);
    }
}
