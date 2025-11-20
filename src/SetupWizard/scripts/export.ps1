#Requires -RunAsAdministrator
param(
    [switch]$TestMode
)

# Configuración inicial
$installPath = "C:\FarmacopilotAgent"
$configPath = Join-Path $installPath "config.json"
$lastExportPath = Join-Path $installPath "last_export.json"
$logPath = Join-Path $installPath "logs\$(Get-Date -Format 'yyyyMMdd').json"
$stagingPath = Join-Path $installPath "staging"

# Crear directorios
New-Item -ItemType Directory -Force -Path $stagingPath | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $logPath -Parent) | Out-Null

# Función log
function Write-Log {
    param($Level, $Message)
    $logEntry = @{ Timestamp = Get-Date -Format "o"; Level = $Level; Message = $Message } | ConvertTo-Json
    Add-Content -Path $logPath -Value $logEntry
}

Write-Log "INFO" "Iniciando exportación (TestMode: $($TestMode.IsPresent))"

# Cargar config (decrypt creds)
$config = Get-Content $configPath | ConvertFrom-Json
$farmaciaId = $config.farmacia_id
$erp = $config.erp
$encryptedCreds = [Convert]::FromBase64String($config.encrypted_creds)

try {
    # Decrypt DPAPI (solo en máquina local)
    $credsBytes = [System.Security.Cryptography.ProtectedData]::Unprotect($encryptedCreds, $null, [System.Security.Cryptography.DataProtectionScope]::LocalMachine)
    $creds = [System.Text.Encoding]::UTF8.GetString($credsBytes)
    $parts = $creds.Split(':')
    $username = $parts[0]
    $password = $parts[1]
    $connectionString = $parts[2] -replace '{USER}', $username -replace '{PASS}', $password

    Write-Log "INFO" "Credenciales decryptadas para $erp"
}
catch {
    Write-Log "ERROR" "Fallo decrypt creds: $($_.Exception.Message)"
    exit 1
}

# Cargar mappings por ERP
$mappingsPath = Join-Path $installPath "mappings\$erp_mappings.json"
if (!(Test-Path $mappingsPath)) {
    Write-Log "ERROR" "Mappings no encontrados: $mappingsPath"
    exit 1
}
$mappings = Get-Content $mappingsPath | ConvertFrom-Json
$tables = $mappings.tables | Where-Object { $_.enabled -eq $true }  # Filtrar por config (ventas, stock, etc.)

# Última export (para incremental)
$lastExport = if (Test-Path $lastExportPath) { Get-Content $lastExportPath | ConvertFrom-Json } else { @{ timestamp = "1900-01-01" } }
$now = Get-Date

# Conectar BD y exportar tablas
foreach ($table in $tables) {
    $sqlPath = Join-Path $installPath "scripts\$($erp)_$($table.name).sql"
    if (!(Test-Path $sqlPath)) {
        Write-Log "WARN" "Script SQL no encontrado: $sqlPath"
        continue
    }

    $query = Get-Content $sqlPath -Raw
    $query = $query -replace '@LAST_EXPORT', $lastExport.timestamp  # Incremental WHERE date > @LAST_EXPORT

    try {
        if ($erp -eq "Nixfarma") {
            # Oracle
            Add-Type -Path "C:\Oracle\product\11.2.0\client_1\odp.net\managed\common\Oracle.ManagedDataAccess.dll"  # Asumir instalado
            $conn = New-Object Oracle.ManagedDataAccess.Client.OracleConnection($connectionString)
            $conn.Open()
            $cmd = New-Object Oracle.ManagedDataAccess.Client.OracleCommand($query, $conn)
            $reader = $cmd.ExecuteReader()
        } else {
            # SQL Server
            Add-Type -AssemblyName System.Data
            $conn = New-Object System.Data.SqlClient.SqlConnection($connectionString)
            $conn.Open()
            $cmd = New-Object System.Data.SqlClient.SqlCommand($query, $conn)
            $reader = $cmd.ExecuteReader()
        }

        # Generar CSV
        $csvPath = Join-Path $stagingPath "$($table.name)_$farmaciaId_$(Get-Date -Format 'yyyyMMdd_HHmmss').csv"
        $stream = [System.IO.StreamWriter]::new($csvPath, $false, [System.Text.Encoding]::UTF8)
        # Header
        for ($i = 0; $i -lt $reader.FieldCount; $i++) { $stream.Write("$($reader.GetName($i)),") }
        $stream.WriteLine("")
        # Rows
        while ($reader.Read()) {
            for ($i = 0; $i -lt $reader.FieldCount; $i++) { $stream.Write("$($reader[$i]),") }
            $stream.WriteLine("")
        }
        $stream.Close()
        $reader.Close()
        $conn.Close()

        # Hash para verificación
        $hash = (Get-FileHash $csvPath -Algorithm SHA256).Hash
        Write-Log "INFO" "Exportada $($table.name): $($csvPath) (SHA256: $hash)"

        # Actualizar last_export por tabla si incremental
        $lastExport | Add-Member -NotePropertyName $table.name -NotePropertyValue $now.ToString("yyyy-MM-dd HH:mm:ss") -Force
    }
    catch {
        Write-Log "ERROR" "Fallo export $($table.name): $($_.Exception.Message)"
    }
}

# Subir a SharePoint
$token = $config.oauth_token  # Asumir refresh o stored (implementa MSAL si needed)
$headers = @{ Authorization = "Bearer $token"; "Content-Type" = "application/octet-stream" }
$driveId = $config.drive_id  # De config
$erpFolder = if ($erp -eq "Nixfarma") { "Nixfarma" } else { "Farmatic" }

foreach ($file in Get-ChildItem $stagingPath -Filter "*.csv") {
    $remotePath = "$erpFolder/$farmaciaId/$($file.Name)"
    $url = "https://graph.microsoft.com/v1.0/drives/$driveId/root:/$remotePath`:/content"

    $response = Invoke-RestMethod -Uri $url -Method Put -Headers $headers -InFile $file.FullName -Verbose:$false
    if ($response.id) {
        Write-Log "INFO" "Subida OK: $($file.Name) → $remotePath"
    } else {
        Write-Log "ERROR" "Fallo subida: $($file.Name)"
    }
}

# Actualizar last_export global
$lastExport | ConvertTo-Json | Set-Content $lastExportPath

# Cleanup staging (mantener 7 días)
Get-ChildItem $stagingPath | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-7) } | Remove-Item

Write-Log "INFO" "Exportación completada. Próxima: 03:00 AM"
if ($TestMode) { Write-Host "Test OK - Archivos en $stagingPath" }
