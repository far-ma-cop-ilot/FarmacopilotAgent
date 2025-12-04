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
    /// Soporta Oracle (con esquemas como APPUL.tabla) y SQL Server
    /// </summary>
    public class TimestampColumnDetector
    {
        private readonly ILogger _logger;
        
        private static readonly string[] TimestampColumnNames = new[]
        {
            "FECHA", "FECHA_VENTA", "FECHA_MODIFICACION", "FECHA_CREACION",
            "FECHA_ALTA", "FECHA_ULT_MOD", "TIMESTAMP", "CREATED_AT", "UPDATED_AT",
            "LASTMODIFIED", "LAST_UPDATE", "FECHA_RECEP", "FECHA_PEDIDO",
            "FECHAHORA", "FECHA_HORA", "FEC_ALTA", "FEC_MOD"
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
                _logger.Debug("Detectando columna timestamp para {Table}", tableName);
                
                var columns = await GetTableColumnsAsync(connection, tableName);
                
                if (columns.Count == 0)
                {
                    _logger.Warning("No se encontraron columnas para {Table}", tableName);
                    return null;
                }
                
                // 1. Buscar columnas con nombres conocidos de timestamp
                var timestampColumn = columns
                    .Where(c => TimestampColumnNames.Any(name => 
                        c.Name.ToUpper().Contains(name)))
                    .OrderByDescending(c => GetColumnPriority(c.Name))
                    .FirstOrDefault();
                
                if (timestampColumn != null)
                {
                    _logger.Debug("Columna timestamp detectada: {Column} (tipo: {Type})", 
                        timestampColumn.Name, timestampColumn.DataType);
                    return timestampColumn.Name;
                }
                
                // 2. Buscar cualquier columna de tipo fecha/datetime
                var dateColumn = columns
                    .Where(c => IsDateTimeType(c.DataType))
                    .FirstOrDefault();
                
                if (dateColumn != null)
                {
                    _logger.Debug("Columna fecha detectada: {Column} (tipo: {Type})", 
                        dateColumn.Name, dateColumn.DataType);
                    return dateColumn.Name;
                }
                
                _logger.Debug("No se encontró columna timestamp apropiada en {Table}", tableName);
                return null;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error detectando columna timestamp en {Table}", tableName);
                return null;
            }
        }

        private async Task<List<ColumnInfo>> GetTableColumnsAsync(
            DbConnection connection, 
            string tableName)
        {
            var columns = new List<ColumnInfo>();
            bool isOracle = connection.GetType().Name.Contains("Oracle");
            
            string query;
            string schemaName = "";
            string simpleTableName = tableName;
            
            // Separar esquema de nombre de tabla si existe (APPUL.AH_VENTAS -> APPUL, AH_VENTAS)
            if (tableName.Contains("."))
            {
                var parts = tableName.Split('.');
                schemaName = parts[0].ToUpper();
                simpleTableName = parts[1].ToUpper();
            }
            
            if (isOracle)
            {
                if (!string.IsNullOrEmpty(schemaName))
                {
                    // Oracle con esquema específico
                    query = @"
                        SELECT 
                            COLUMN_NAME,
                            DATA_TYPE
                        FROM ALL_TAB_COLUMNS
                        WHERE OWNER = :schemaName
                          AND TABLE_NAME = :tableName
                        ORDER BY COLUMN_ID";
                }
                else
                {
                    // Oracle sin esquema (usa USER_TAB_COLUMNS)
                    query = @"
                        SELECT 
                            COLUMN_NAME,
                            DATA_TYPE
                        FROM USER_TAB_COLUMNS
                        WHERE TABLE_NAME = :tableName
                        ORDER BY COLUMN_ID";
                }
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
            
            if (isOracle)
            {
                // Parámetro tableName
                var tableParam = command.CreateParameter();
                tableParam.ParameterName = "tableName";
                tableParam.Value = simpleTableName.ToUpper();
                command.Parameters.Add(tableParam);
                
                // Parámetro schemaName si existe
                if (!string.IsNullOrEmpty(schemaName))
                {
                    var schemaParam = command.CreateParameter();
                    schemaParam.ParameterName = "schemaName";
                    schemaParam.Value = schemaName;
                    command.Parameters.Add(schemaParam);
                }
            }
            else
            {
                var param = command.CreateParameter();
                param.ParameterName = "@tableName";
                param.Value = simpleTableName;
                command.Parameters.Add(param);
            }
            
            try
            {
                await using var reader = await command.ExecuteReaderAsync();
                
                while (await reader.ReadAsync())
                {
                    columns.Add(new ColumnInfo
                    {
                        Name = reader.GetString(0),
                        DataType = reader.GetString(1)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warning("Error obteniendo columnas de {Table}: {Error}", tableName, ex.Message);
            }
            
            return columns;
        }

        private int GetColumnPriority(string columnName)
        {
            var upper = columnName.ToUpper();
            
            if (upper.Contains("MODIFICACION") || upper.Contains("UPDATED") || upper.Contains("ULT_MOD")) return 10;
            if (upper.Contains("CREACION") || upper.Contains("CREATED") || upper.Contains("ALTA")) return 9;
            if (upper.Contains("VENTA") || upper == "FECHA" || upper == "FECHAHORA") return 8;
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