# build-installer.ps1 - Compila el instalador con credenciales embebidas

param(
    [Parameter(Mandatory=$false)]
    [string]$TenantId = "d543ed3a-c274-41c8-ad2e-393a36c2d1fc",
    
    [Parameter(Mandatory=$false)]
    [string]$ClientId = "27f13cc9-95e5-4b8e-b7a3-dff7e3b9ec26",
    
    [Parameter(Mandatory=$false)]
    [string]$ClientSecret = "IGU8Q~ODfxHsw_LSjAEAHep3C55fjvkyhiddScrx",
    
    [Parameter(Mandatory=$false)]
    [string]$SharePointSiteId = "d14f0b31-c267-4493-82ea-02447a8cc665"
)

$ErrorActionPreference = "Stop"

Write-Host "🔨 Compilando Farmacopilot Agent Installer..."

# 1. Restaurar y compilar
Write-Host "📦 Restaurando dependencias..."
dotnet restore FarmacopilotAgent.sln

Write-Host "🏗️  Compilando solución..."
dotnet build FarmacopilotAgent.sln -c Release

# 2. Publicar self-contained
Write-Host "📦 Publicando aplicación..."
dotnet publish src/FarmacopilotAgent.Runner/FarmacopilotAgent.Runner.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o installer/publish

# 3. Generar archivo secrets.embedded (cifrado con DPAPI)
Write-Host "🔐 Generando credenciales cifradas..."
dotnet publish src/SetupWizard/SetupWizard.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true 

$credentials = @{
    TenantId = $TenantId
    ClientId = $ClientId
    ClientSecret = $ClientSecret
    SharePointSiteId = $SharePointSiteId
} | ConvertTo-Json

# Crear un ejecutable temporal para cifrar con DPAPI
$tempCsFile = @"
using System;
using System.Security.Cryptography;
using System.Text;
using System.IO;

class Encryptor {
    static void Main(string[] args) {
        var plainText = args[0];
        var entropy = Encoding.UTF8.GetBytes("FarmacopilotAgent2025");
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = ProtectedData.Protect(plainBytes, entropy, DataProtectionScope.LocalMachine);
        File.WriteAllText("installer/secrets.embedded", Convert.ToBase64String(encryptedBytes));
    }
}
"@

$tempCsFile | Out-File -FilePath "temp_encryptor.cs" -Encoding UTF8
csc.exe /out:temp_encryptor.exe temp_encryptor.cs
.\temp_encryptor.exe $credentials
Remove-Item temp_encryptor.cs, temp_encryptor.exe

Write-Host "✅ Credenciales cifradas generadas en installer/secrets.embedded"

# 4. Compilar instalador con InnoSetup
if (Get-Command iscc -ErrorAction SilentlyContinue) {
    Write-Host "📦 Generando instalador..."
    iscc installer/setup.iss
    Write-Host "✅ Instalador generado en installer/output/"
} else {
    Write-Warning "⚠️  InnoSetup no encontrado. Compilar manualmente setup.iss"
}

Write-Host ""
Write-Host "✅ Build completado"
Write-Host "📁 Archivos en: installer/output/"
