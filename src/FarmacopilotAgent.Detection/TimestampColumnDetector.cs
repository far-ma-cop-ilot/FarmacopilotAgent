using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Serilog;

namespace FarmacopilotAgent.Detection
{
    /// <summary>
    /// Detecta automáticamente columnas apropiadas para extracción incremental
    /// </summary>
    public class TimestampColumnDetector
    {
        private readonly ILogger _logger;
        
        private static readonly string[] TimestampColumnNames = new[]
        {
            "FECHA", "FECHA_VENTA", "FECHA_MODIFICACION", "FECHA_CREACION",
            "FECHA_ALTA", "FECHA_ULT_MOD", "TIMESTAMP", "CREATED_AT", "UPDATED_AT",
            "LASTMODIFIED", "LAST_UPDATE", "FECHA_RECEP", "FECHA_PEDIDO"
        };

        public TimestampColumnDetector(ILogger logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Detecta la mejor columna para extracción incremental en una tabla
        /// </summary>
        public async Task<string?> DetectBestTimestampColumnAsync(
            DbConnection connection, 
            string tableName)
        {
            try
            {
                _logger.Information("Detectando columna timestamp para {Table}", tableName);
                
                var columns = await GetTableColumnsAsync(connection, tableName);
                
                // 1. Buscar columnas con nombres conocidos de timestamp
                var timestampColumn = columns
                    .Where(c => TimestampColumnNames.Any(name => 
                        c.Name.ToUpper().Contains(name)))
                    .OrderByDescending(c => GetColumnPriority(c.Name))
                    .FirstOrDefault();
                
                if (timestampColumn != null)
                {
                    _logger.Information("Columna timestamp detectada: {Column} (tipo: {Type})", 
                        timestampColumn.Name, timestampColumn.DataType);
                    return timestampColumn.Name;
                }
                
                // 2. Buscar cualquier columna de tipo fecha/datetime
                var dateColumn = columns
                    .Where(c => IsDateTimeType(c.DataType))
                    .FirstOrDefault();
                
                if (dateColumn != null)
                {
                    _logger.Information("Columna fecha detectada: {Column} (tipo: {Type})", 
                        dateColumn.Name, dateColumn.DataType);
                    return dateColumn.Name;
                }
                
                _logger.Warning("No se encontró columna timestamp apropiada en {Table}", tableName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error detectando columna timestamp en {Table}", tableName);
                return null;
            }
        }

        private async Task<List<ColumnInfo>> GetTableColumnsAsync(
            DbConnection connection, 
            string tableName)
        {
            var columns = new List<ColumnInfo>();
            
            string query;
            if (connection.GetType().Name.Contains("Oracle"))
            {
                // Oracle
                query = @"
                    SELECT 
                        COLUMN_NAME,
                        DATA_TYPE
                    FROM USER_TAB_COLUMNS
                    WHERE TABLE_NAME = :tableName
                    ORDER BY COLUMN_ID";
            }
            else
            {
                // SQL Server
                query = @"
                    SELECT 
                        COLUMN_NAME,
                        DATA_TYPE
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = @tableName
                    ORDER BY ORDINAL_POSITION";
            }
            
            await using var command = connection.CreateCommand();
            command.CommandText = query;
            
            var param = command.CreateParameter();
            param.ParameterName = connection.GetType().Name.Contains("Oracle") 
                ? "tableName" 
                : "@tableName";
            param.Value = tableName.ToUpper();
            command.Parameters.Add(param);
            
            await using var reader = await command.ExecuteReaderAsync();
            
            while (await reader.ReadAsync())
            {
                columns.Add(new ColumnInfo
                {
                    Name = reader.GetString(0),
                    DataType = reader.GetString(1)
                });
            }
            
            return columns;
        }

        private int GetColumnPriority(string columnName)
        {
            var upper = columnName.ToUpper();
            
            if (upper.Contains("MODIFICACION") || upper.Contains("UPDATED")) return 10;
            if (upper.Contains("CREACION") || upper.Contains("CREATED")) return 9;
            if (upper.Contains("VENTA") || upper.Contains("FECHA")) return 8;
            if (upper.Contains("RECEP") || upper.Contains("PEDIDO")) return 7;
            
            return 5;
        }

        private bool IsDateTimeType(string dataType)
        {
            var upper = dataType.ToUpper();
            return upper.Contains("DATE") || 
                   upper.Contains("TIME") || 
                   upper.Contains("TIMESTAMP");
        }

        private class ColumnInfo
        {
            public string Name { get; set; } = string.Empty;
            public string DataType { get; set; } = string.Empty;
        }
    }
}
