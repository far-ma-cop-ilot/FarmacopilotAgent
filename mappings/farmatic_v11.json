-- Exportación incremental de ventas para Farmatic v11.x
-- Base de datos: Oracle
-- Tabla principal: VENTAS_DETALLE
-- :lastExport: parámetro para extracción incremental (bind variable)

SELECT 
    v.FECHA_VENTA as fecha,
    v.COD_ARTICULO as codigo_producto,
    a.DESCRIPCION as nombre_producto,
    v.CANTIDAD as cantidad,
    v.PRECIO_UNITARIO as precio_unitario,
    v.IMPORTE_TOTAL as importe_total,
    v.TIPO_VENTA as tipo_venta,
    NVL(v.NUM_RECETA, '') as numero_receta,
    NVL(v.COD_NACIONAL, '') as codigo_nacional,
    NVL(v.DESCUENTO, 0) as descuento,
    NVL(v.IVA, 0) as iva,
    v.ID_TICKET as id_ticket
FROM VENTAS_DETALLE v
INNER JOIN ARTICULOS a ON v.COD_ARTICULO = a.CODIGO
WHERE v.FECHA_VENTA > :lastExport
ORDER BY v.FECHA_VENTA ASC;

-- Notas:
-- 1. Oracle usa :lastExport para bind variables (no @lastExport como SQL Server)
-- 2. NVL() equivalente a ISNULL() en SQL Server
-- 3. FECHA_VENTA debe ser tipo DATE o TIMESTAMP
-- 4. Para primera exportación (full), pasar fecha antigua: TO_DATE('1900-01-01', 'YYYY-MM-DD')

-- Query de ejemplo para testing (últimos 10 registros):
-- SELECT * FROM VENTAS_DETALLE ORDER BY FECHA_VENTA DESC FETCH FIRST 10 ROWS ONLY;
