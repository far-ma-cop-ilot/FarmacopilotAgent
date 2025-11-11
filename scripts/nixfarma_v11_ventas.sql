-- Exportación incremental de ventas para Nixfarma v11.x
-- Usa nomenclatura nueva (PascalCase)
-- @LastExportTimestamp: parámetro para extracción incremental

SELECT 
    v.FechaVenta as fecha,
    v.CodigoArticulo as codigo_producto,
    a.Descripcion as nombre_producto,
    v.Cantidad as cantidad,
    v.PrecioUnitario as precio_unitario,
    v.ImporteVenta as importe_total,
    v.TipoVenta as tipo_venta,
    v.NumeroReceta as numero_receta,
    v.CodigoNacional as codigo_nacional,
    v.Descuento as descuento,
    v.Iva as iva,
    v.IdTicket as id_ticket
FROM VentasLineas v WITH (NOLOCK)
INNER JOIN Articulos a WITH (NOLOCK) 
    ON v.CodigoArticulo = a.Codigo
WHERE v.FechaVenta > @LastExportTimestamp
ORDER BY v.FechaVenta ASC;
