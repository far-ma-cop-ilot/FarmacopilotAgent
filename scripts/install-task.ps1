# install-task.ps1 - Crea tarea programada para Farmacopilot Agent

param(
    [Parameter(Mandatory=$true)]
    [string]$FarmaciaId,
    
    [Parameter(Mandatory=$false)]
    [string]$InstallPath = "C:\FarmacopilotAgent",
    
    [Parameter(Mandatory=$false)]
    [string]$ScheduleTime = "03:00"
)

$ErrorActionPreference = "Stop"

try {
    Write-Host "Creando tarea programada para $FarmaciaId..."

    $taskName = "Farmacopilot_Export"
    $exePath = Join-Path $InstallPath "FarmacopilotAgent.Runner.exe"
    
    # Verificar que el ejecutable existe
    if (!(Test-Path $exePath)) {
        throw "Ejecutable no encontrado: $exePath"
    }

    # Eliminar tarea existente si existe
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existingTask) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "Tarea existente eliminada"
    }

    # Crear acción - ejecutar directamente el .exe de C#
    $action = New-ScheduledTaskAction -Execute $exePath -WorkingDirectory $InstallPath

    # Crear trigger (diario a la hora especificada)
    $trigger = New-ScheduledTaskTrigger -Daily -At $ScheduleTime

    # Configurar para ejecutar con privilegios de SYSTEM
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

    # Configuración adicional
    $settings = New-ScheduledTaskSettingsSet `
        -StartWhenAvailable `
        -DontStopIfGoingOnBatteries `
        -AllowStartIfOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
        -RestartCount 3 `
        -RestartInterval (New-TimeSpan -Minutes 10)

    # Registrar tarea
    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description "Exportación automática de datos Farmacopilot para $FarmaciaId - Ejecuta FarmacopilotAgent.Runner.exe"

    Write-Host "✅ Tarea programada creada exitosamente"
    Write-Host "   Nombre: $taskName"
    Write-Host "   Ejecutable: $exePath"
    Write-Host "   Horario: Diario a las $ScheduleTime"

    # Ejecutar primera exportación de prueba
    Write-Host ""
    Write-Host "Ejecutando primera exportación de prueba..."
    $testProcess = Start-Process -FilePath $exePath -WorkingDirectory $InstallPath -Wait -PassThru -NoNewWindow
    
    if ($testProcess.ExitCode -eq 0) {
        Write-Host "✅ Primera exportación completada exitosamente"
    } else {
        Write-Warning "⚠️ Primera exportación terminó con código: $($testProcess.ExitCode)"
        Write-Warning "   Revise los logs en: $InstallPath\logs\"
    }

} catch {
    Write-Error "❌ Error al crear tarea programada: $_"
    exit 1
}
