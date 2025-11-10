public class NixfarmaExporter : IExporter
{
    public async Task<ExportResult> ExportDataAsync(DateTime? lastExportTimestamp)
    {
        // 1. Conectar a SQL Server
        // 2. Ejecutar query incremental
        // 3. Generar CSV con mapeo
        // 4. Calcular SHA256
        // 5. Retornar ExportResult
    }
}
