# install-task.ps1 - Crea tarea programada para Farmacopilot Agent

param(
    [Parameter(Mandatory=$true)]
    [string]$FarmaciaId
)

$ErrorActionPreference = "Stop"

try {
    Write-Host "Creando tarea programada para $FarmaciaId..."

    $taskName = "Farmacopilot_Export"
    $exePath = "C:\FarmacopilotAgent\FarmacopilotAgent.Runner.exe"
    
    # Eliminar tarea existente si existe
    $existingTask = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    if ($existingTask) {
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        Write-Host "Tarea existente eliminada"
    }

    # Crear acción
    $action = New-ScheduledTaskAction -Execute $exePath

    # Crear trigger (diario a las 03:00 AM)
    $trigger = New-ScheduledTaskTrigger -Daily -At "03:00"

    # Configurar para ejecutar con privilegios de SYSTEM
    $principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount -RunLevel Highest

    # Configuración adicional
    $settings = New-ScheduledTaskSettingsSet `
        -StartWhenAvailable `
        -DontStopIfGoingOnBatteries `
        -AllowStartIfOnBatteries `
        -ExecutionTimeLimit (New-TimeSpan -Hours 2)

    # Registrar tarea
    Register-ScheduledTask `
        -TaskName $taskName `
        -Action $action `
        -Trigger $trigger `
        -Principal $principal `
        -Settings $settings `
        -Description "Exportación automática de datos Farmacopilot para $FarmaciaId"

    Write-Host "✅ Tarea programada creada exitosamente"
    Write-Host "   Nombre: $taskName"
    Write-Host "   Horario: Diario a las 03:00 AM"

} catch {
    Write-Error "❌ Error al crear tarea programada: $_"
    exit 1
}
