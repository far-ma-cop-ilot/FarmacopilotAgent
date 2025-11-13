-- ═══════════════════════════════════════════════════════════════════════════
-- QUERIES DE DESCUBRIMIENTO - NIXFARMA (SQL Server)
-- ═══════════════════════════════════════════════════════════════════════════
-- Ejecutar ANTES de configurar el agente para identificar la estructura real
-- ═══════════════════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────────────────
-- 1. INFORMACIÓN DEL SISTEMA
-- ───────────────────────────────────────────────────────────────────────────

-- Versión de SQL Server
SELECT @@VERSION AS 'SQL_Server_Version';

-- Versión de base de datos
SELECT 
    name AS 'Database_Name',
    compatibility_level AS 'Compatibility_Level',
    recovery_model_desc AS 'Recovery_Model'
FROM sys.databases 
WHERE name IN ('Nixfarma', 'NIX', 'FARMACIA', 'FARMA_DB');

-- ───────────────────────────────────────────────────────────────────────────
-- 2. DESCUBRIMIENTO DE TABLAS
-- ───────────────────────────────────────────────────────────────────────────

-- Listar TODAS las tablas relacionadas con ventas
SELECT 
    TABLE_SCHEMA,
    TABLE_NAME,
    TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME LIKE '%VENTA%' 
   OR TABLE_NAME LIKE '%VENDA%'
   OR TABLE_NAME LIKE '%TICKET%'
   OR TABLE_NAME LIKE '%FACTURA%'
ORDER BY TABLE_NAME;

-- Listar TODAS las tablas relacionadas con artículos/productos
SELECT 
    TABLE_SCHEMA,
    TABLE_NAME,
    TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME LIKE '%ARTICULO%' 
   OR TABLE_NAME LIKE '%PRODUCTO%'
   OR TABLE_NAME LIKE '%ITEM%'
ORDER BY TABLE_NAME;

-- Listar TODAS las tablas relacionadas con stock
SELECT 
    TABLE_SCHEMA,
    TABLE_NAME,
    TABLE_TYPE
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_NAME LIKE '%STOCK%' 
   OR TABLE_NAME LIKE '%ALMACEN%'
   OR TABLE_NAME LIKE '%EXISTENCIA%'
ORDER BY TABLE_NAME;

-- ───────────────────────────────────────────────────────────────────────────
-- 3. ESTRUCTURA DE TABLAS PRINCIPALES
-- ───────────────────────────────────────────────────────────────────────────

-- Opción A: Usar sp_columns (más detallado)
EXEC sp_columns 'VentasLineas';
-- O para v10.x:
EXEC sp_columns 'VENTAS_LINEAS';

-- Opción B: Usar INFORMATION_SCHEMA (estándar SQL)
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    NUMERIC_PRECISION,
    NUMERIC_SCALE,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'VentasLineas'  -- O 'VENTAS_LINEAS' para v10.x
ORDER BY ORDINAL_POSITION;

-- Estructura de tabla ARTICULOS
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('Articulos', 'ARTICULOS')
ORDER BY ORDINAL_POSITION;

-- ───────────────────────────────────────────────────────────────────────────
-- 4. RELACIONES Y FOREIGN KEYS
-- ───────────────────────────────────────────────────────────────────────────

-- Ver todas las FKs de la tabla de ventas
SELECT 
    fk.name AS 'FK_Name',
    OBJECT_NAME(fk.parent_object_id) AS 'Parent_Table',
    COL_NAME(fkc.parent_object_id, fkc.parent_column_id) AS 'Parent_Column',
    OBJECT_NAME(fk.referenced_object_id) AS 'Referenced_Table',
    COL_NAME(fkc.referenced_object_id, fkc.referenced_column_id) AS 'Referenced_Column'
FROM sys.foreign_keys AS fk
INNER JOIN sys.foreign_key_columns AS fkc 
    ON fk.object_id = fkc.constraint_object_id
WHERE OBJECT_NAME(fk.parent_object_id) LIKE '%VENTA%'
ORDER BY fk.name;

-- ───────────────────────────────────────────────────────────────────────────
-- 5. VERIFICACIÓN DE DATOS
-- ───────────────────────────────────────────────────────────────────────────

-- Contar registros en tabla de ventas
SELECT COUNT(*) AS 'Total_Registros_Ventas'
FROM VentasLineas;  -- O VENTAS_LINEAS para v10.x

-- Contar registros por fecha (últimos 30 días)
SELECT 
    CAST(FechaVenta AS DATE) AS 'Fecha',
    COUNT(*) AS 'Num_Registros'
FROM VentasLineas
WHERE FechaVenta >= DATEADD(DAY, -30, GETDATE())
GROUP BY CAST(FechaVenta AS DATE)
ORDER BY Fecha DESC;

-- Muestra de datos (primeros 10 registros más recientes)
SELECT TOP 10 *
FROM VentasLineas
ORDER BY FechaVenta DESC;

-- ───────────────────────────────────────────────────────────────────────────
-- 6. VERIFICACIÓN DE CAMPOS CRÍTICOS
-- ───────────────────────────────────────────────────────────────────────────

-- Verificar campo de fecha (nombre puede variar)
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('VentasLineas', 'VENTAS_LINEAS')
  AND COLUMN_NAME LIKE '%FECHA%'
ORDER BY ORDINAL_POSITION;

-- Verificar campo de código producto (nombre puede variar)
SELECT COLUMN_NAME
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('VentasLineas', 'VENTAS_LINEAS')
  AND (COLUMN_NAME LIKE '%ARTICULO%' 
    OR COLUMN_NAME LIKE '%PRODUCTO%'
    OR COLUMN_NAME LIKE '%CODIGO%')
ORDER BY ORDINAL_POSITION;

-- Verificar valores únicos en TIPO_VENTA (importante para filtros)
SELECT DISTINCT TipoVenta, COUNT(*) AS 'Count'
FROM VentasLineas
GROUP BY TipoVenta
ORDER BY Count DESC;

-- ───────────────────────────────────────────────────────────────────────────
-- 7. VERIFICACIÓN DE NULLS (IMPORTANTE PARA QUERIES)
-- ───────────────────────────────────────────────────────────────────────────

-- Contar NULLs en campos importantes
SELECT 
    SUM(CASE WHEN CodigoArticulo IS NULL THEN 1 ELSE 0 END) AS 'CodigoArticulo_Nulls',
    SUM(CASE WHEN FechaVenta IS NULL THEN 1 ELSE 0 END) AS 'FechaVenta_Nulls',
    SUM(CASE WHEN Cantidad IS NULL THEN 1 ELSE 0 END) AS 'Cantidad_Nulls',
    SUM(CASE WHEN PrecioUnitario IS NULL THEN 1 ELSE 0 END) AS 'PrecioUnitario_Nulls',
    SUM(CASE WHEN NumeroReceta IS NULL THEN 1 ELSE 0 END) AS 'NumeroReceta_Nulls',
    COUNT(*) AS 'Total_Rows'
FROM VentasLineas;

-- ───────────────────────────────────────────────────────────────────────────
-- 8. VERIFICACIÓN DE JOIN CON ARTICULOS
-- ───────────────────────────────────────────────────────────────────────────

-- Probar JOIN entre ventas y artículos
SELECT TOP 10
    v.CodigoArticulo,
    a.Descripcion,
    v.Cantidad,
    v.PrecioUnitario
FROM VentasLineas v
LEFT JOIN Articulos a ON v.CodigoArticulo = a.Codigo
ORDER BY v.FechaVenta DESC;

-- Verificar si hay ventas sin artículo correspondiente (huérfanos)
SELECT COUNT(*) AS 'Ventas_Sin_Articulo'
FROM VentasLineas v
LEFT JOIN Articulos a ON v.CodigoArticulo = a.Codigo
WHERE a.Codigo IS NULL;

-- ───────────────────────────────────────────────────────────────────────────
-- 9. RANGOS DE FECHAS Y VOLUMEN DE DATOS
-- ───────────────────────────────────────────────────────────────────────────

-- Primera y última fecha de venta
SELECT 
    MIN(FechaVenta) AS 'Primera_Venta',
    MAX(FechaVenta) AS 'Ultima_Venta',
    DATEDIFF(DAY, MIN(FechaVenta), MAX(FechaVenta)) AS 'Dias_Historico'
FROM VentasLineas;

-- Volumen de datos por año
SELECT 
    YEAR(FechaVenta) AS 'Año',
    COUNT(*) AS 'Num_Registros',
    SUM(ImporteVenta) AS 'Total_Importe'
FROM VentasLineas
GROUP BY YEAR(FechaVenta)
ORDER BY Año DESC;

-- ───────────────────────────────────────────────────────────────────────────
-- 10. DETECTAR VERSIÓN DE NIXFARMA
-- ───────────────────────────────────────────────────────────────────────────

-- Método 1: Desde tabla de configuración (si existe)
SELECT valor AS 'Nixfarma_Version'
FROM configuracion 
WHERE parametro = 'VERSION_SISTEMA';

-- Método 2: Por nomenclatura de tablas
-- Si existe VentasLineas (PascalCase) → v11.x
-- Si existe VENTAS_LINEAS (UPPERCASE) → v10.x
SELECT 
    CASE 
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'VentasLineas')
            THEN 'v11.x (PascalCase)'
        WHEN EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'VENTAS_LINEAS')
            THEN 'v10.x (UPPERCASE)'
        ELSE 'Version Desconocida'
    END AS 'Detected_Version';

-- ═══════════════════════════════════════════════════════════════════════════
-- NOTAS IMPORTANTES
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 1. Ejecutar estas queries en orden
-- 2. Documentar TODOS los resultados en un archivo .txt
-- 3. Especial atención a:
--    - Nombres exactos de tablas
--    - Nombres exactos de columnas (especialmente fecha y código producto)
--    - Si descripción está en VENTAS o en ARTICULOS (JOIN necesario)
--    - Campos que pueden ser NULL (usar ISNULL/COALESCE)
-- 4. Si una query falla, pasar a la siguiente
-- 5. Guardar sample de datos (SIN INFORMACIÓN REAL) para testing
--
-- ═══════════════════════════════════════════════════════════════════════════
