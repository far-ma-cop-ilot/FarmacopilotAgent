# build-setup.ps1 - Compila SetupWizard.exe self-contained
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$projectPath = "src/SetupWizard/SetupWizard.csproj"

Write-Host "Restaurando paquetes..."
dotnet restore $projectPath

Write-Host "Compilando en modo $Configuration..."
dotnet build $projectPath -c $Configuration

Write-Host "Publicando como single-file self-contained..."
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o ./publish

Write-Host "¡Listo! SetupWizard.exe en ./publish/SetupWizard.exe"
Write-Host "Prueba: .\publish\SetupWizard.exe"
