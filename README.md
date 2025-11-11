# Farmacopilot Agent - Sprint 1 MVP

Agente local de extracción de datos para ERPs de farmacia (Nixfarma y Farmatic).

## 🎯 Características Sprint 1

- ✅ Detección automática de ERP instalado (Nixfarma/Farmatic)
- ✅ Conexión segura a base de datos local
- ✅ Extracción incremental de datos (ventas, stock)
- ✅ Cifrado de credenciales con Windows DPAPI
- ✅ Subida automática a SharePoint con Microsoft Graph API
- ✅ Validación de estado del cliente contra PostgreSQL
- ✅ Tarea programada con auto-desactivación
- ✅ Logging estructurado y rotativo

## 📋 Requisitos previos

- Windows 10/11 (64-bit)
- .NET 8.0 Runtime
- SQL Server (para Nixfarma) u Oracle (para Farmatic)
- Permisos de administrador
- Acceso a Internet (HTTPS)

## 🛠️ Compilación
```powershell
# Restaurar dependencias
dotnet restore FarmacopilotAgent.sln

# Compilar solución
dotnet build FarmacopilotAgent.sln -c Release

# Publicar aplicación
dotnet publish src/FarmacopilotAgent.Runner/FarmacopilotAgent.Runner.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o installer/publish
```

## 📦 Generar instalador
```powershell
# Ejecutar script de build con credenciales
.\build-installer.ps1 `
  -TenantId "your-tenant-id" `
  -ClientId "your-client-id" `
  -ClientSecret "your-client-secret" `
  -SharePointSiteId "your-site-id"
```

## 🚀 Instalación

1. Descargar `FarmacopilotAgentInstaller_v1.0.0.exe`
2. Ejecutar como administrador
3. Ingresar ID de farmacia (formato: FAR2025001)
4. El instalador detectará automáticamente el ERP
5. Primera exportación se ejecutará automáticamente

## 📁 Estructura de archivos
```
C:\FarmacopilotAgent\
├── FarmacopilotAgent.Runner.exe
├── config.json (cifrado)
├── secrets.enc (credenciales Graph API)
├── last_export.json
├── mappings/
│   ├── nixfarma_v10.json
│   ├── nixfarma_v11.json
│   └── farmatic_v11.json
├── scripts/
│   ├── export.ps1
│   ├── install-task.ps1
│   ├── nixfarma_v10_ventas.sql
│   └── nixfarma_v11_ventas.sql
├── logs/
│   └── agent.log
└── staging/
    └── (archivos CSV temporales)
```

## 🔒 Seguridad

- Credenciales cifradas con Windows DPAPI (scope: LocalMachine)
- Tokens OAuth almacenados de forma segura
- Logs sin información sensible (PII)
- Validación SHA256 de archivos subidos

## 🔄 Proceso de ejecución

1. **Verificación de estado**: consulta PostgreSQL para validar cliente activo
2. **Detección de versión**: identifica ERP y versión automáticamente
3. **Carga de mapping**: selecciona mapping JSON correcto
4. **Extracción incremental**: solo datos desde última exportación
5. **Generación CSV**: formato estandarizado con SHA256
6. **Subida a SharePoint**: usando credenciales de servicio
7. **Actualización de estado**: marca última actividad en PostgreSQL

## 📊 Formato de exportación

**Archivo**: `ventas_FAR{ID}_YYYYMMDD_HHMMSS.csv`

**Formato**: UTF-8, delimitador `;`

**Campos**:
- fecha
- codigo_producto
- nombre_producto
- cantidad
- precio_unitario
- importe_total
- tipo_venta
- numero_receta
- codigo_nacional

## ⏰ Tarea programada

**Nombre**: `Farmacopilot_Export`

**Horario**: Diario a las 03:00 AM

**Usuario**: SYSTEM

**Comportamiento**:
- Se auto-deshabilita si el cliente está inactivo
- Reintentos automáticos en caso de fallo
- Timeout: 2 horas

## 🐛 Troubleshooting

### Error de conexión a base de datos
```powershell
# Verificar conexión manual
sqlcmd -S localhost -U sa -P password -Q "SELECT @@VERSION"
```

### Error de subida a SharePoint
- Verificar conectividad a Internet
- Comprobar credenciales en `secrets.enc`
- Revisar logs en `C:\FarmacopilotAgent\logs\agent.log`

### Tarea programada no se ejecuta
```powershell
# Verificar estado de la tarea
Get-ScheduledTask -TaskName "Farmacopilot_Export"

# Ejecutar manualmente
C:\FarmacopilotAgent\FarmacopilotAgent.Runner.exe
```

## 📝 Logs

Los logs se almacenan en: `C:\FarmacopilotAgent\logs\agent.log`

**Retención**: 30 días

**Formato**: JSON estructurado
```json
{
  "Timestamp": "2025-11-11 03:00:00",
  "Level": "Information",
  "Message": "Exportación completada: 1234 registros"
}
```

## 🔄 Actualizaciones

El agente verificará automáticamente nuevas versiones al ejecutarse.

## 📞 Soporte

Para soporte técnico: soporte@farmacopilot.com

## 📄 Licencia

Copyright © 2025 Farmacopilot SL. Todos los derechos reservados.
