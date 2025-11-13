-- Exportación incremental de ventas para Farmatic v12.x
-- Base de datos: Oracle
-- Tabla principal: VEN_DETALLE
-- :lastExport: parámetro para extracción incremental (bind variable)

SELECT 
    v.FECHA as fecha,
    v.ARTICULO_ID as codigo_producto,
    a.DESCRIPCION as nombre_producto,
    v.CANTIDAD as cantidad,
    v.PVP_UNITARIO as precio_unitario,
    v.IMPORTE as importe_total,
    v.TIPO as tipo_venta,
    NVL(v.RECETA_NUM, '') as numero_receta,
    NVL(v.CN, '') as codigo_nacional,
    NVL(v.DTO, 0) as descuento,
    NVL(v.IVA_PCT, 0) as iva,
    v.TICKET_ID as id_ticket
FROM VEN_DETALLE v
INNER JOIN ART_MAESTRO a ON v.ARTICULO_ID = a.ID
WHERE v.FECHA > :lastExport
ORDER BY v.FECHA ASC;

-- Notas:
-- 1. Farmatic v12.x usa nomenclatura más corta y modernizada
-- 2. IDs numéricos (ARTICULO_ID) en lugar de códigos alfanuméricos
-- 3. ART_MAESTRO en lugar de ARTICULOS
-- 4. PVP_UNITARIO (Precio Venta Público) en lugar de PRECIO_UNITARIO
-- 5. DTO (Descuento) en lugar de DESCUENTO
-- 6. CN (Código Nacional) en lugar de COD_NACIONAL

-- Query de ejemplo para testing (últimos 10 registros):
-- SELECT * FROM VEN_DETALLE ORDER BY FECHA DESC FETCH FIRST 10 ROWS ONLY;

-- Query para verificar estructura de tabla:
-- SELECT COLUMN_NAME, DATA_TYPE, DATA_LENGTH, NULLABLE 
-- FROM USER_TAB_COLUMNS 
-- WHERE TABLE_NAME = 'VEN_DETALLE' 
-- ORDER BY COLUMN_ID;
