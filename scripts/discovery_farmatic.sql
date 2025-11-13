-- ═══════════════════════════════════════════════════════════════════════════
-- QUERIES DE DESCUBRIMIENTO - FARMATIC (Oracle Database)
-- ═══════════════════════════════════════════════════════════════════════════
-- Ejecutar ANTES de configurar el agente para identificar la estructura real
-- Conectar usando SQL*Plus, SQL Developer o cualquier cliente Oracle
-- ═══════════════════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────────────────
-- 1. INFORMACIÓN DEL SISTEMA
-- ───────────────────────────────────────────────────────────────────────────

-- Versión de Oracle
SELECT BANNER FROM V$VERSION WHERE ROWNUM = 1;

-- Información del esquema actual
SELECT 
    USERNAME AS "Current_User",
    ACCOUNT_STATUS,
    CREATED,
    DEFAULT_TABLESPACE
FROM DBA_USERS 
WHERE USERNAME = USER;

-- Nombre de la base de datos
SELECT NAME, OPEN_MODE FROM V$DATABASE;

-- ───────────────────────────────────────────────────────────────────────────
-- 2. DESCUBRIMIENTO DE TABLAS
-- ───────────────────────────────────────────────────────────────────────────

-- Listar TODAS las tablas relacionadas con ventas
SELECT 
    TABLE_NAME,
    NUM_ROWS,
    TABLESPACE_NAME
FROM USER_TABLES
WHERE TABLE_NAME LIKE '%VEN%' 
   OR TABLE_NAME LIKE '%VENTA%'
   OR TABLE_NAME LIKE '%TICKET%'
   OR TABLE_NAME LIKE '%FACTURA%'
ORDER BY TABLE_NAME;

-- Listar TODAS las tablas relacionadas con artículos/productos
SELECT 
    TABLE_NAME,
    NUM_ROWS,
    TABLESPACE_NAME
FROM USER_TABLES
WHERE TABLE_NAME LIKE '%ART%' 
   OR TABLE_NAME LIKE '%ARTICULO%'
   OR TABLE_NAME LIKE '%PRODUCTO%'
   OR TABLE_NAME LIKE '%MAESTRO%'
ORDER BY TABLE_NAME;

-- Listar TODAS las tablas relacionadas con stock
SELECT 
    TABLE_NAME,
    NUM_ROWS,
    TABLESPACE_NAME
FROM USER_TABLES
WHERE TABLE_NAME LIKE '%STK%'
   OR TABLE_NAME LIKE '%STOCK%'
   OR TABLE_NAME LIKE '%ALMACEN%'
   OR TABLE_NAME LIKE '%EXISTENCIA%'
ORDER BY TABLE_NAME;

-- ───────────────────────────────────────────────────────────────────────────
-- 3. ESTRUCTURA DE TABLAS PRINCIPALES
-- ───────────────────────────────────────────────────────────────────────────

-- Estructura completa de tabla de ventas (v11.x)
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    DATA_LENGTH,
    DATA_PRECISION,
    DATA_SCALE,
    NULLABLE,
    COLUMN_ID
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'VENTAS_DETALLE'  -- O 'VEN_DETALLE' para v12.x
ORDER BY COLUMN_ID;

-- Estructura de tabla de artículos
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    DATA_LENGTH,
    NULLABLE,
    COLUMN_ID
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME IN ('ARTICULOS', 'ART_MAESTRO')
ORDER BY COLUMN_ID;

-- Estructura de tabla de stock
SELECT 
    COLUMN_NAME,
    DATA_TYPE,
    DATA_LENGTH,
    NULLABLE,
    COLUMN_ID
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME IN ('STOCK_ACTUAL', 'STK_ACTUAL')
ORDER BY COLUMN_ID;

-- ───────────────────────────────────────────────────────────────────────────
-- 4. RELACIONES Y CONSTRAINTS
-- ───────────────────────────────────────────────────────────────────────────

-- Ver todas las Foreign Keys
SELECT 
    a.constraint_name AS "FK_Name",
    a.table_name AS "Parent_Table",
    a.column_name AS "Parent_Column",
    c_pk.table_name AS "Referenced_Table",
    b.column_name AS "Referenced_Column"
FROM user_cons_columns a
JOIN user_constraints c ON a.constraint_name = c.constraint_name
JOIN user_constraints c_pk ON c.r_constraint_name = c_pk.constraint_name
JOIN user_cons_columns b ON c_pk.constraint_name = b.constraint_name
WHERE c.constraint_type = 'R'
  AND a.table_name LIKE '%VEN%'
ORDER BY a.constraint_name;

-- Ver índices de la tabla de ventas
SELECT 
    INDEX_NAME,
    COLUMN_NAME,
    COLUMN_POSITION,
    DESCEND
FROM USER_IND_COLUMNS
WHERE TABLE_NAME IN ('VENTAS_DETALLE', 'VEN_DETALLE')
ORDER BY INDEX_NAME, COLUMN_POSITION;

-- ───────────────────────────────────────────────────────────────────────────
-- 5. VERIFICACIÓN DE DATOS
-- ───────────────────────────────────────────────────────────────────────────

-- Contar registros en tabla de ventas
SELECT COUNT(*) AS "Total_Registros_Ventas"
FROM VENTAS_DETALLE;  -- O VEN_DETALLE para v12.x

-- Contar registros por fecha (últimos 30 días)
SELECT 
    TRUNC(FECHA_VENTA) AS "Fecha",
    COUNT(*) AS "Num_Registros"
FROM VENTAS_DETALLE
WHERE FECHA_VENTA >= SYSDATE - 30
GROUP BY TRUNC(FECHA_VENTA)
ORDER BY "Fecha" DESC;

-- Muestra de datos (primeros 10 registros más recientes)
-- Oracle 12c+:
SELECT *
FROM VENTAS_DETALLE
ORDER BY FECHA_VENTA DESC
FETCH FIRST 10 ROWS ONLY;

-- Oracle 11g (sin FETCH):
SELECT * FROM (
    SELECT *
    FROM VENTAS_DETALLE
    ORDER BY FECHA_VENTA DESC
) WHERE ROWNUM <= 10;

-- ───────────────────────────────────────────────────────────────────────────
-- 6. VERIFICACIÓN DE CAMPOS CRÍTICOS
-- ───────────────────────────────────────────────────────────────────────────

-- Verificar campo de fecha (nombre puede variar)
SELECT COLUMN_NAME, DATA_TYPE
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME IN ('VENTAS_DETALLE', 'VEN_DETALLE')
  AND COLUMN_NAME LIKE '%FECHA%'
ORDER BY COLUMN_ID;

-- Verificar campo de código producto (nombre puede variar)
SELECT COLUMN_NAME, DATA_TYPE
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME IN ('VENTAS_DETALLE', 'VEN_DETALLE')
  AND (COLUMN_NAME LIKE '%ARTICULO%' 
    OR COLUMN_NAME LIKE '%PRODUCTO%'
    OR COLUMN_NAME LIKE '%COD%'
    OR COLUMN_NAME LIKE '%ID%')
ORDER BY COLUMN_ID;

-- Verificar valores únicos en TIPO_VENTA (importante para filtros)
SELECT TIPO_VENTA, COUNT(*) AS "Count"
FROM VENTAS_DETALLE
GROUP BY TIPO_VENTA
ORDER BY "Count" DESC;

-- ───────────────────────────────────────────────────────────────────────────
-- 7. VERIFICACIÓN DE NULLS (IMPORTANTE PARA QUERIES)
-- ───────────────────────────────────────────────────────────────────────────

-- Contar NULLs en campos importantes
SELECT 
    SUM(CASE WHEN COD_ARTICULO IS NULL THEN 1 ELSE 0 END) AS "CodigoArticulo_Nulls",
    SUM(CASE WHEN FECHA_VENTA IS NULL THEN 1 ELSE 0 END) AS "FechaVenta_Nulls",
    SUM(CASE WHEN CANTIDAD IS NULL THEN 1 ELSE 0 END) AS "Cantidad_Nulls",
    SUM(CASE WHEN PRECIO_UNITARIO IS NULL THEN 1 ELSE 0 END) AS "PrecioUnitario_Nulls",
    SUM(CASE WHEN NUM_RECETA IS NULL THEN 1 ELSE 0 END) AS "NumeroReceta_Nulls",
    COUNT(*) AS "Total_Rows"
FROM VENTAS_DETALLE;

-- ───────────────────────────────────────────────────────────────────────────
-- 8. VERIFICACIÓN DE JOIN CON ARTICULOS
-- ───────────────────────────────────────────────────────────────────────────

-- Probar JOIN entre ventas y artículos (v11.x)
SELECT * FROM (
    SELECT
        v.COD_ARTICULO,
        a.DESCRIPCION,
        v.CANTIDAD,
        v.PRECIO_UNITARIO
    FROM VENTAS_DETALLE v
    LEFT JOIN ARTICULOS a ON v.COD_ARTICULO = a.CODIGO
    ORDER BY v.FECHA_VENTA DESC
) WHERE ROWNUM <= 10;

-- Verificar si hay ventas sin artículo correspondiente (huérfanos)
SELECT COUNT(*) AS "Ventas_Sin_Articulo"
FROM VENTAS_DETALLE v
LEFT JOIN ARTICULOS a ON v.COD_ARTICULO = a.CODIGO
WHERE a.CODIGO IS NULL;

-- Para v12.x (IDs numéricos):
SELECT * FROM (
    SELECT
        v.ARTICULO_ID,
        a.DESCRIPCION,
        v.CANTIDAD,
        v.PVP_UNITARIO
    FROM VEN_DETALLE v
    LEFT JOIN ART_MAESTRO a ON v.ARTICULO_ID = a.ID
    ORDER BY v.FECHA DESC
) WHERE ROWNUM <= 10;

-- ───────────────────────────────────────────────────────────────────────────
-- 9. RANGOS DE FECHAS Y VOLUMEN DE DATOS
-- ───────────────────────────────────────────────────────────────────────────

-- Primera y última fecha de venta
SELECT 
    MIN(FECHA_VENTA) AS "Primera_Venta",
    MAX(FECHA_VENTA) AS "Ultima_Venta",
    TRUNC(MAX(FECHA_VENTA) - MIN(FECHA_VENTA)) AS "Dias_Historico"
FROM VENTAS_DETALLE;

-- Volumen de datos por año
SELECT 
    EXTRACT(YEAR FROM FECHA_VENTA) AS "Año",
    COUNT(*) AS "Num_Registros",
    SUM(IMPORTE_TOTAL) AS "Total_Importe"
FROM VENTAS_DETALLE
GROUP BY EXTRACT(YEAR FROM FECHA_VENTA)
ORDER BY "Año" DESC;

-- ───────────────────────────────────────────────────────────────────────────
-- 10. DETECTAR VERSIÓN DE FARMATIC
-- ───────────────────────────────────────────────────────────────────────────

-- Método 1: Desde tabla de configuración (si existe)
SELECT VALOR AS "Farmatic_Version"
FROM CONFIGURACION 
WHERE PARAMETRO = 'VERSION_SISTEMA'
AND ROWNUM = 1;

-- Método 2: Desde tabla PARAMETROS
SELECT VALOR AS "Farmatic_Version"
FROM PARAMETROS 
WHERE NOMBRE = 'VERSION'
AND ROWNUM = 1;

-- Método 3: Por nomenclatura de tablas
-- Si existe VEN_DETALLE (abreviado) → v12.x
-- Si existe VENTAS_DETALLE (completo) → v11.x
SELECT 
    CASE 
        WHEN EXISTS (SELECT 1 FROM USER_TABLES WHERE TABLE_NAME = 'VEN_DETALLE')
            THEN 'v12.x (Nomenclatura moderna)'
        WHEN EXISTS (SELECT 1 FROM USER_TABLES WHERE TABLE_NAME = 'VENTAS_DETALLE')
            THEN 'v11.x (Nomenclatura clásica)'
        ELSE 'Version Desconocida'
    END AS "Detected_Version"
FROM DUAL;

-- ───────────────────────────────────────────────────────────────────────────
-- 11. VERIFICACIÓN DE CONEXIÓN Y PERMISOS
-- ───────────────────────────────────────────────────────────────────────────

-- Verificar permisos sobre tablas
SELECT 
    TABLE_NAME,
    PRIVILEGE
FROM USER_TAB_PRIVS
WHERE TABLE_NAME LIKE '%VEN%'
   OR TABLE_NAME LIKE '%ART%'
   OR TABLE_NAME LIKE '%STK%'
ORDER BY TABLE_NAME, PRIVILEGE;

-- Verificar espacio disponible en tablespace
SELECT 
    TABLESPACE_NAME,
    ROUND(SUM(BYTES)/1024/1024, 2) AS "Size_MB"
FROM USER_SEGMENTS
GROUP BY TABLESPACE_NAME
ORDER BY "Size_MB" DESC;

-- ───────────────────────────────────────────────────────────────────────────
-- 12. PERFORMANCE Y ESTADÍSTICAS
-- ───────────────────────────────────────────────────────────────────────────

-- Verificar si las tablas tienen estadísticas actualizadas
SELECT 
    TABLE_NAME,
    NUM_ROWS,
    LAST_ANALYZED,
    STALE_STATS
FROM USER_TAB_STATISTICS
WHERE TABLE_NAME IN ('VENTAS_DETALLE', 'VEN_DETALLE', 'ARTICULOS', 'ART_MAESTRO')
ORDER BY TABLE_NAME;

-- Ver tamaño de las tablas principales
SELECT 
    SEGMENT_NAME AS "Table_Name",
    ROUND(BYTES/1024/1024, 2) AS "Size_MB",
    BLOCKS
FROM USER_SEGMENTS
WHERE SEGMENT_TYPE = 'TABLE'
  AND (SEGMENT_NAME LIKE '%VEN%' OR SEGMENT_NAME LIKE '%ART%')
ORDER BY BYTES DESC;

-- ═══════════════════════════════════════════════════════════════════════════
-- VERIFICACIÓN DE TNS NAMES (ORACLE CLIENT)
-- ═══════════════════════════════════════════════════════════════════════════

-- En Windows, ejecutar desde CMD:
-- type %ORACLE_HOME%\network\admin\tnsnames.ora

-- Ejemplo de entrada TNS esperada:
/*
FARMATIC =
  (DESCRIPTION =
    (ADDRESS = (PROTOCOL = TCP)(HOST = localhost)(PORT = 1521))
    (CONNECT_DATA =
      (SERVICE_NAME = FARMATIC)
    )
  )
*/

-- Probar conexión desde SQL*Plus:
-- sqlplus username/password@FARMATIC

-- ═══════════════════════════════════════════════════════════════════════════
-- NOTAS IMPORTANTES
-- ═══════════════════════════════════════════════════════════════════════════
--
-- 1. Oracle es case-sensitive para nombres en queries, pero los metadatos
--    se almacenan en UPPERCASE por defecto
-- 2. Usar USER_* views para ver objetos del usuario actual
-- 3. Usar DBA_* views si tienes permisos DBA (requiere privilegios)
-- 4. ROWNUM funciona diferente que TOP en SQL Server
-- 5. Oracle 12c+ soporta FETCH FIRST, versiones anteriores solo ROWNUM
-- 6. NVL() es el equivalente a ISNULL() de SQL Server
-- 7. SYSDATE equivale a GETDATE()
-- 8. Oracle usa :bindVariable para parámetros, no @parameter
-- 9. Documentar TODO en archivo .txt para referencia
-- 10. CRÍTICO: Verificar TNS Names configurado correctamente
--
-- ═══════════════════════════════════════════════════════════════════════════
