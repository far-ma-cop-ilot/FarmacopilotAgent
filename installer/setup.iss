#define MyAppName "Farmacopilot Agent"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Farmacopilot SL"
#define MyAppURL "https://www.farmacopilot.com"
#define MyAppExeName "FarmacopilotAgent.Runner.exe"

[Setup]
AppId={{B8E9F3A2-4D7C-4F8E-9B2A-8C5D6E7F8A9B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName=C:\FarmacopilotAgent
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=output
OutputBaseFilename=FarmacopilotAgentInstaller_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
DisableProgramGroupPage=yes

[Code]
var
  FarmaciaIdPage: TInputQueryWizardPage;
  FarmaciaId: String;

procedure InitializeWizard;
begin
  FarmaciaIdPage := CreateInputQueryPage(wpWelcome,
    'Identificación de Farmacia',
    'Por favor ingrese su ID de farmacia',
    'Este ID le fue proporcionado tras completar el pago. Formato: FAR2025001');
  FarmaciaIdPage.Add('ID de Farmacia:', False);
  FarmaciaIdPage.Values[0] := GetPreviousData('FarmaciaId', '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = FarmaciaIdPage.ID then
  begin
    FarmaciaId := FarmaciaIdPage.Values[0];
    if Length(FarmaciaId) < 5 then
    begin
      MsgBox('Por favor ingrese un ID de farmacia válido', mbError, MB_OK);
      Result := False;
    end;
  end;
end;

procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'FarmaciaId', FarmaciaId);
end;

[Files]
; Ejecutables y DLLs
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; ✅ NUEVO: Credenciales cifradas pre-configuradas
Source: "secrets.embedded"; DestDir: "{app}"; DestName: "secrets.enc"; Flags: ignoreversion

; Scripts
Source: "..\scripts\export.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "..\scripts\install-task.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

; Mappings
Source: "..\mappings\*.json"; DestDir: "{app}\mappings"; Flags: ignoreversion

; Licencia
Source: "license.rtf"; DestDir: "{app}"; Flags: ignoreversion

[Dirs]
Name: "{app}\logs"; Permissions: users-full
Name: "{app}\staging"; Permissions: users-full

[Run]
; Detectar ERP y crear config.json inicial
Filename: "{app}\{#MyAppExeName}"; Parameters: "--setup --farmacia-id ""{code:GetFarmaciaId}"""; Flags: runhidden waituntilterminated; StatusMsg: "Detectando ERP instalado..."

; Crear tarea programada
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\scripts\install-task.ps1"" -FarmaciaId ""{code:GetFarmaciaId}"""; Flags: runhidden; StatusMsg: "Configurando tarea programada..."

; Primera ejecución de prueba
Filename: "{app}\{#MyAppExeName}"; Flags: runhidden waituntilterminated; StatusMsg: "Ejecutando primera extracción de prueba..."

[UninstallRun]
; Eliminar tarea programada
Filename: "schtasks.exe"; Parameters: "/Delete /TN ""Farmacopilot_Export"" /F"; Flags: runhidden

[Code]
function GetFarmaciaId(Param: String): String;
begin
  Result := FarmaciaId;
end;
