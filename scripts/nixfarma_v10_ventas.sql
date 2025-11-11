-- Exportación incremental de ventas para Nixfarma v10.x
-- Usa nomenclatura antigua (MAYÚSCULAS)
-- @LastExportTimestamp: parámetro para extracción incremental

SELECT 
    v.FECHA_VENTA as fecha,
    v.CODIGO_ARTICULO as codigo_producto,
    a.DESCRIPCION as nombre_producto,
    v.CANTIDAD as cantidad,
    v.PRECIO_UNITARIO as precio_unitario,
    v.IMPORTE_VENTA as importe_total,
    v.TIPO_VENTA as tipo_venta,
    v.NUMERO_RECETA as numero_receta,
    v.CNP as codigo_nacional,
    v.DESCUENTO as descuento,
    v.IVA as iva,
    v.ID_TICKET as id_ticket
FROM VENTAS_LINEAS v WITH (NOLOCK)
INNER JOIN ARTICULOS a WITH (NOLOCK) 
    ON v.CODIGO_ARTICULO = a.CODIGO
WHERE v.FECHA_VENTA > @LastExportTimestamp
ORDER BY v.FECHA_VENTA ASC;
