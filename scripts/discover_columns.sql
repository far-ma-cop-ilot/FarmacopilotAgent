-- Para Oracle (Nixfarma)
SELECT 
    table_name,
    column_name,
    data_type,
    data_length,
    nullable
FROM user_tab_columns
WHERE table_name IN (
    'AH_VENTAS',
    'AH_VENTA_LINEAS',
    'AB_ARTICULOS_FICHA_E',
    'AB_LABORATORIOS',
    'AD_PROVEEDORES'
)
ORDER BY table_name, column_id;

-- Para SQL Server (Farmatic)
SELECT 
    t.name AS table_name,
    c.name AS column_name,
    ty.name AS data_type,
    c.max_length,
    c.is_nullable
FROM sys.tables t
INNER JOIN sys.columns c ON t.object_id = c.object_id
INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
WHERE t.name IN (
    'ventas',
    'linea venta',
    'articu',
    'proveedor',
    'recep'
)
ORDER BY t.name, c.column_id;
