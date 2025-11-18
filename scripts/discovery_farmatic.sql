-- ═══════════════════════════════════════════════════════════════════════════
-- QUERIES DE DESCUBRIMIENTO - FARMATIC (SQL Server)
-- ═══════════════════════════════════════════════════════════════════════════
-- Ejecutar ANTES de configurar el agente para identificar la estructura real
-- Versión: 16.00.9837
-- Base de datos: CGCOF (SQL Server 2019)
-- ═══════════════════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────────────────
-- 1. INFORMACIÓN DEL SISTEMA
-- ───────────────────────────────────────────────────────────────────────────

-- Versión de SQL Server
SELECT @@VERSION AS 'SQL_Server_Version';

-- Información de la base de datos actual
SELECT 
    DB_NAME() AS 'Database_Name',
    compatibility_level AS 'Compatibility_Level',
    recovery_model_desc AS 'Recovery_Model',
    state_desc AS 'State',
    create_date AS 'Created'
FROM sys.databases 
WHERE name = DB_NAME();

-- ───────────────────────────────────────────────────────────────────────────
-- 2. VERIFICACIÓN DE TABLAS FARMATIC
-- ───────────────────────────────────────────────────────────────────────────

-- Listar TODAS las tablas que existen en la BD
SELECT 
    TABLE_SCHEMA,
    TABLE_NAME,
    TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME;

-- Verificar existencia de tablas específicas Farmatic
SELECT 
    CASE 
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'ventas') THEN 'SI' 
        ELSE 'NO' 
    END AS 'ventas_existe',
    CASE 
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'linea venta') THEN 'SI' 
        ELSE 'NO' 
    END AS 'linea_venta_existe',
    CASE 
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'articu') THEN 'SI' 
        ELSE 'NO' 
    END AS 'articu_existe',
    CASE 
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'proveedor') THEN 'SI' 
        ELSE 'NO' 
    END AS 'proveedor_existe',
    CASE 
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'recep') THEN 'SI' 
        ELSE 'NO' 
    END AS 'recep_existe';

-- ───────────────────────────────────────────────────────────────────────────
-- 3. ESTRUCTURA COMPLETA DE TABLAS PRINCIPALES
-- ───────────────────────────────────────────────────────────────────────────

-- Estructura de tabla VENTAS
PRINT '=== ESTRUCTURA: ventas ===';
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    NUMERIC_PRECISION,
    NUMERIC_SCALE,
    IS_NULLABLE,
    COLUMN_DEFAULT,
    ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'ventas'
ORDER BY ORDINAL_POSITION;

-- Estructura de tabla LINEA VENTA
PRINT '=== ESTRUCTURA: linea venta ===';
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    NUMERIC_PRECISION,
    NUMERIC_SCALE,
    IS_NULLABLE,
    COLUMN_DEFAULT,
    ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'linea venta'
ORDER BY ORDINAL_POSITION;

-- Estructura de tabla ARTICU
PRINT '=== ESTRUCTURA: articu ===';
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    NUMERIC_PRECISION,
    NUMERIC_SCALE,
    IS_NULLABLE,
    ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'articu'
ORDER BY ORDINAL_POSITION;

-- Estructura de tabla PROVEEDOR
PRINT '=== ESTRUCTURA: proveedor ===';
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE,
    ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'proveedor'
ORDER BY ORDINAL_POSITION;

-- Estructura de tabla RECEP
PRINT '=== ESTRUCTURA: recep ===';
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    NUMERIC_PRECISION,
    NUMERIC_SCALE,
    IS_NULLABLE,
    ORDINAL_POSITION
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'recep'
ORDER BY ORDINAL_POSITION;

-- ───────────────────────────────────────────────────────────────────────────
-- 4. IDENTIFICAR CAMPOS DE FECHA/TIMESTAMP (crítico para incremental)
-- ───────────────────────────────────────────────────────────────────────────

-- Campos tipo fecha en todas las tablas principales
PRINT '=== CAMPOS DE FECHA ===';
SELECT 
    TABLE_NAME,
    COLUMN_NAME,
    DATA_TYPE,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('ventas', 'linea venta', 'articu', 'proveedor', 'recep', 
                     'albaran dev', 'coste venta', 'linea albaran', 'venta aux')
  AND DATA_TYPE IN ('date', 'datetime', 'datetime2', 'smalldatetime', 'timestamp')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

-- ───────────────────────────────────────────────────────────────────────────
-- 5. FOREIGN KEYS Y RELACIONES
-- ───────────────────────────────────────────────────────────────────────────

-- Ver todas las FKs de las tablas principales
PRINT '=== FOREIGN KEYS ===';
SELECT 
    fk.name AS 'FK_Name',
    OBJECT_NAME(fk.parent_object_id) AS 'Parent_Table',
    COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS 'Parent_Column',
    OBJECT_NAME(fk.referenced_object_id) AS 'Referenced_Table',
    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS 'Referenced_Column'
FROM sys.foreign_keys AS fk
INNER JOIN sys.foreign_key_columns AS fkc 
    ON fk.object_id = fkc.constraint_object_id
WHERE OBJECT_NAME(fk.parent_object_id) IN ('ventas', 'linea venta', 'recep')
ORDER BY fk.name;

-- ───────────────────────────────────────────────────────────────────────────
-- 6. ÍNDICES (importante para performance de extracción)
-- ───────────────────────────────────────────────────────────────────────────

-- Índices de tabla ventas
PRINT '=== ÍNDICES: ventas ===';
SELECT 
    i.name AS 'Index_Name',
    i.type_desc AS 'Index_Type',
    COL_NAME(ic.object_id, ic.column_id) AS 'Column_Name',
    ic.key_ordinal AS 'Key_Order',
    ic.is_included_column AS 'Is_Included'
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE OBJECT_NAME(i.object_id) = 'ventas'
ORDER BY i.name, ic.key_ordinal;

-- Índices de tabla linea venta
PRINT '=== ÍNDICES: linea venta ===';
SELECT 
    i.name AS 'Index_Name',
    i.type_desc AS 'Index_Type',
    COL_NAME(ic.object_id, ic.column_id) AS 'Column_Name',
    ic.key_ordinal AS 'Key_Order'
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE OBJECT_NAME(i.object_id) = 'linea venta'
ORDER BY i.name, ic.key_ordinal;

-- ───────────────────────────────────────────────────────────────────────────
-- 7. VOLUMEN DE DATOS Y RANGOS
-- ───────────────────────────────────────────────────────────────────────────

-- Contar registros en cada tabla
PRINT '=== VOLUMEN DE DATOS ===';
SELECT 
    'ventas' AS 'Tabla',
    COUNT(*) AS 'Total_Registros'
FROM [ventas]
UNION ALL
SELECT 
    'linea venta',
    COUNT(*)
FROM [linea venta]
UNION ALL
SELECT 
    'articu',
    COUNT(*)
FROM [articu]
UNION ALL
SELECT 
    'proveedor',
    COUNT(*)
FROM [proveedor]
UNION ALL
SELECT 
    'recep',
    COUNT(*)
FROM [recep];

-- ───────────────────────────────────────────────────────────────────────────
-- 8. MUESTRA DE DATOS (primeros registros)
-- ───────────────────────────────────────────────────────────────────────────

-- IMPORTANTE: Revisar manualmente estos datos para identificar campos de fecha

-- Muestra de VENTAS
PRINT '=== MUESTRA: ventas (últimos 10 registros) ===';
SELECT TOP 10 *
FROM [ventas]
ORDER BY 1 DESC;  -- Ajustar según campo de fecha real

-- Muestra de LINEA VENTA
PRINT '=== MUESTRA: linea venta (últimos 10 registros) ===';
SELECT TOP 10 *
FROM [linea venta]
ORDER BY 1 DESC;  -- Ajustar según campo de fecha real

-- Muestra de ARTICU
PRINT '=== MUESTRA: articu (primeros 10 registros) ===';
SELECT TOP 10 *
FROM [articu];

-- ───────────────────────────────────────────────────────────────────────────
-- 9. VERIFICACIÓN DE NULLS (importante para SELECT *)
-- ───────────────────────────────────────────────────────────────────────────

-- Contar NULLs en tabla ventas (ejecutar después de identificar columnas)
PRINT '=== ANÁLISIS DE NULLS: ventas ===';
-- NOTA: Ajustar nombres de columnas después de ver estructura
-- Ejemplo genérico:
SELECT 
    COUNT(*) AS 'Total_Rows',
    SUM(CASE WHEN fecha IS NULL THEN 1 ELSE 0 END) AS 'fecha_nulls'
    -- Agregar más columnas críticas según estructura real
FROM [ventas];

-- ───────────────────────────────────────────────────────────────────────────
-- 10. PRUEBA DE EXTRACCIÓN INCREMENTAL (simulación)
-- ───────────────────────────────────────────────────────────────────────────

-- NOTA: Ajustar nombre del campo de fecha según estructura real
PRINT '=== PRUEBA EXTRACCIÓN INCREMENTAL ===';

-- Supongamos que el campo de fecha se llama 'fecha' o 'fecha_venta'
-- Ajustar según estructura real descubierta arriba

-- Contar registros de últimos 30 días
DECLARE @FechaCorte DATETIME = DATEADD(DAY, -30, GETDATE());

SELECT 
    COUNT(*) AS 'Registros_Ultimos_30_Dias'
FROM [ventas]
WHERE fecha > @FechaCorte;  -- Ajustar nombre de columna

-- Distribución por día (últimos 7 días)
SELECT 
    CAST(fecha AS DATE) AS 'Fecha',
    COUNT(*) AS 'Num_Registros'
FROM [ventas]
WHERE fecha >= DATEADD(DAY, -7, GETDATE())
GROUP BY CAST(fecha AS DATE)
ORDER BY Fecha DESC;

-- ───────────────────────────────────────────────────────────────────────────
-- 11. VERIFICAR TIPOS DE DATOS PROBLEMÁTICOS
-- ───────────────────────────────────────────────────────────────────────────

-- Buscar columnas con tipos binarios o muy grandes (excluir de SELECT *)
PRINT '=== COLUMNAS PROBLEMÁTICAS (binarias/grandes) ===';
SELECT 
    TABLE_NAME,
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('ventas', 'linea venta', 'articu', 'proveedor', 'recep')
  AND (
      DATA_TYPE IN ('image', 'varbinary', 'binary', 'text', 'ntext', 'xml')
      OR CHARACTER_MAXIMUM_LENGTH > 4000
  )
ORDER BY TABLE_NAME, COLUMN_NAME;

-- ───────────────────────────────────────────────────────────────────────────
-- 12. CAMPOS ÚNICOS / PRIMARY KEYS
-- ───────────────────────────────────────────────────────────────────────────

PRINT '=== PRIMARY KEYS ===';
SELECT 
    OBJECT_NAME(ic.object_id) AS 'Table_Name',
    COL_NAME(ic.object_id, ic.column_id) AS 'Column_Name',
    i.name AS 'Constraint_Name'
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE i.is_primary_key = 1
  AND OBJECT_NAME(ic.object_id) IN ('ventas', 'linea venta', 'articu', 'proveedor', 'recep')
ORDER BY OBJECT_NAME(ic.object_id), ic.key_ordinal;

-- ───────────────────────────────────────────────────────────────────────────
-- 13. TAMAÑO DE LAS TABLAS (importante para estimar tiempos)
-- ───────────────────────────────────────────────────────────────────────────

PRINT '=== TAMAÑO DE TABLAS ===';
SELECT 
    t.name AS 'Table_Name',
    p.rows AS 'Row_Count',
    SUM(a.total_pages) * 8 AS 'Total_KB',
    SUM(a.used_pages) * 8 AS 'Used_KB',
    (SUM(a.total_pages) - SUM(a.used_pages)) * 8 AS 'Unused_KB'
FROM sys.tables t
INNER JOIN sys.indexes i ON t.object_id = i.object_id
INNER JOIN sys.partitions p ON i.object_id = p.object_id AND i.index_id = p.index_id
INNER JOIN sys.allocation_units a ON p.partition_id = a.container_id
WHERE t.name IN ('ventas', 'linea venta', 'articu', 'proveedor', 'recep', 
                 'albaran dev', 'coste venta', 'linea albaran', 'venta aux', 'vendedor', 'vlab')
  AND i.object_id > 255
  AND i.index_id <= 1
GROUP BY t.name, p.rows
ORDER BY p.rows DESC;

-- ═══════════════════════════════════════════════════════════════════════════
-- RESUMEN DE INFORMACIÓN A DOCUMENTAR
-- ═══════════════════════════════════════════════════════════════════════════
/*
DESPUÉS DE EJECUTAR ESTE SCRIPT, DOCUMENTA:

1. Nombres EXACTOS de tablas (respetando mayúsculas/minúsculas y espacios)
2. Por cada tabla:
   - Campo(s) de fecha/timestamp para extracción incremental
   - Primary key
   - Número aproximado de registros
   - Campos que NO se deben exportar (binarios, muy grandes)
3. Relaciones entre tablas (si son necesarias para el negocio)
4. Volumen de datos histórico (primera fecha vs última fecha)

EJEMPLO DE SALIDA ESPERADA:

Tabla: ventas
- Campo fecha: fecha (datetime)
- PK: numero_venta (int)
- Registros: 450,000
- Primera fecha: 2020-01-01
- Última fecha: 2025-01-17
- Campos a excluir: firma_digital (image)

NOTAS IMPORTANTES:
- SQL Server es case-insensitive para nombres de objetos
- Los nombres con espacios deben ir entre [corchetes]
- Verificar si hay campos calculados o triggers
- Documentar cualquier particularidad del esquema
*/
