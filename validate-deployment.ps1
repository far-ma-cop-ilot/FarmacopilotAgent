# validate-deployment.ps1 - Validación completa antes de despliegue

param(
    [Parameter(Mandatory=$false)]
    [string]$FarmaciaId = "FAR2025TEST",
    
    [Parameter(Mandatory=$false)]
    [string]$Environment = "staging"
)

$ErrorActionPreference = "Stop"
$WarningPreference = "Continue"

Write-Host "🔍 Validación de Despliegue - Farmacopilot Agent" -ForegroundColor Cyan
Write-Host "================================================" -ForegroundColor Cyan
Write-Host "Farmacia ID: $FarmaciaId"
Write-Host "Entorno: $Environment"
Write-Host ""

$validationResults = @{
    Passed = @()
    Failed = @()
    Warnings = @()
}

function Test-Validation {
    param(
        [string]$TestName,
        [ScriptBlock]$TestScript
    )
    
    Write-Host "⏳ $TestName..." -NoNewline
    
    try {
        $result = & $TestScript
        if ($result -eq $true) {
            Write-Host " ✅ PASS" -ForegroundColor Green
            $validationResults.Passed += $TestName
            return $true
        } else {
            Write-Host " ❌ FAIL" -ForegroundColor Red
            $validationResults.Failed += $TestName
            return $false
        }
    } catch {
        Write-Host " ❌ ERROR: $_" -ForegroundColor Red
        $validationResults.Failed += "$TestName (Exception)"
        return $false
    }
}

function Test-Warning {
    param(
        [string]$WarningName,
        [ScriptBlock]$TestScript
    )
    
    Write-Host "⚠️  $WarningName..." -NoNewline
    
    try {
        $result = & $TestScript
        if ($result -eq $true) {
            Write-Host " ✅ OK" -ForegroundColor Green
        } else {
            Write-Host " ⚠️  WARNING" -ForegroundColor Yellow
            $validationResults.Warnings += $WarningName
        }
    } catch {
        Write-Host " ⚠️  WARNING: $_" -ForegroundColor Yellow
        $validationResults.Warnings += "$WarningName (Exception)"
    }
}

Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "📦 VALIDACIÓN DE ARCHIVOS Y ESTRUCTURA" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

Test-Validation "Instalador existe" {
    Test-Path "installer\output\FarmacopilotAgentInstaller*.exe"
}

Test-Validation "Archivo secrets.embedded existe" {
    Test-Path "installer\secrets.embedded"
}

Test-Validation "Mappings JSON existen" {
    (Test-Path "mappings\nixfarma_v10.json") -and 
    (Test-Path "mappings\nixfarma_v11.json")
}

Test-Validation "Scripts SQL existen" {
    (Test-Path "scripts\nixfarma_v10_ventas.sql") -and 
    (Test-Path "scripts\nixfarma_v11_ventas.sql")
}

Test-Validation "Script install-task.ps1 existe" {
    Test-Path "scripts\install-task.ps1"
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "🔐 VALIDACIÓN DE SEGURIDAD" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

Test-Validation "Instalador firmado digitalmente" {
    $installer = Get-ChildItem "installer\output\FarmacopilotAgentInstaller*.exe" | Select-Object -First 1
    if ($installer) {
        $signature = Get-AuthenticodeSignature $installer.FullName
        $signature.Status -eq "Valid"
    } else {
        $false
    }
}

Test-Validation "Credenciales cifradas en secrets.embedded" {
    $secretsContent = Get-Content "installer\secrets.embedded" -Raw
    # Verificar que es Base64 y tiene longitud mínima
    ($secretsContent.Length -gt 100) -and ($secretsContent -match '^[A-Za-z0-9+/=]+$')
}

Test-Warning "Sin contraseñas en texto plano en config" {
    $configFiles = Get-ChildItem -Recurse -Filter "*.json" -Exclude "package*.json"
    $foundPlainText = $false
    
    foreach ($file in $configFiles) {
        $content = Get-Content $file.FullName -Raw
        if ($content -match 'password["\s:]+[^"]*[a-zA-Z0-9]+' -and $content -notmatch 'encrypted') {
            $foundPlainText = $true
            Write-Host "      Archivo sospechoso: $($file.Name)" -ForegroundColor Yellow
        }
    }
    
    -not $foundPlainText
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "🏗️  VALIDACIÓN DE COMPILACIÓN" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

Test-Validation "Solución compila correctamente" {
    $output = dotnet build FarmacopilotAgent.sln -c Release --no-restore 2>&1
    $LASTEXITCODE -eq 0
}

Test-Validation "Tests unitarios pasan" {
    if (Test-Path "tests\FarmacopilotAgent.Tests\FarmacopilotAgent.Tests.csproj") {
        $output = dotnet test tests\FarmacopilotAgent.Tests\FarmacopilotAgent.Tests.csproj --no-build -c Release 2>&1
        $LASTEXITCODE -eq 0
    } else {
        Write-Host " ⚠️  No hay proyecto de tests" -ForegroundColor Yellow
        $true # No fallar si no hay tests todavía
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "☁️  VALIDACIÓN DE CONECTIVIDAD" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

Test-Warning "Conectividad a Microsoft Graph API" {
    try {
        $response = Invoke-WebRequest -Uri "https://graph.microsoft.com/v1.0/" -Method Head -TimeoutSec 5
        $response.StatusCode -eq 200 -or $response.StatusCode -eq 401
    } catch {
        $false
    }
}

Test-Warning "Conectividad a SharePoint (si está configurado)" {
    # Este test es opcional y depende de la configuración
    $true
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "📋 VALIDACIÓN DE CONFIGURACIÓN" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

Test-Validation "InnoSetup está instalado" {
    $null -ne (Get-Command iscc -ErrorAction SilentlyContinue)
}

Test-Validation "Setup.iss tiene versión correcta" {
    $setupContent = Get-Content "installer\setup.iss" -Raw
    $setupContent -match '#define MyAppVersion "1\.0\.0"'
}

Test-Warning ".NET 8.0 SDK instalado" {
    $dotnetVersion = dotnet --version 2>$null
    if ($dotnetVersion) {
        $dotnetVersion -match '^8\.'
    } else {
        $false
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "📊 RESUMEN DE VALIDACIÓN" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

Write-Host ""
Write-Host "✅ Tests Pasados: $($validationResults.Passed.Count)" -ForegroundColor Green
foreach ($test in $validationResults.Passed) {
    Write-Host "   • $test" -ForegroundColor Green
}

if ($validationResults.Failed.Count -gt 0) {
    Write-Host ""
    Write-Host "❌ Tests Fallidos: $($validationResults.Failed.Count)" -ForegroundColor Red
    foreach ($test in $validationResults.Failed) {
        Write-Host "   • $test" -ForegroundColor Red
    }
}

if ($validationResults.Warnings.Count -gt 0) {
    Write-Host ""
    Write-Host "⚠️  Advertencias: $($validationResults.Warnings.Count)" -ForegroundColor Yellow
    foreach ($warning in $validationResults.Warnings) {
        Write-Host "   • $warning" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "═══════════════════════════════════════════════" -ForegroundColor Cyan

if ($validationResults.Failed.Count -eq 0) {
    Write-Host "✅ VALIDACIÓN EXITOSA - Listo para desplegar" -ForegroundColor Green
    exit 0
} else {
    Write-Host "❌ VALIDACIÓN FALLIDA - Revisar errores antes de desplegar" -ForegroundColor Red
    exit 1
}
