; Transkript (C# .NET 8) — Script Inno Setup
; Source : dossier publish\ généré par build.bat
; Sortie  : publish\Transkript-Setup.exe

#define AppName    "Transkript"
#define AppVersion "1.0.0"
#define AppExe     "Transkript.exe"

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Transkript
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=publish
OutputBaseFilename=Transkript-Setup
SetupIconFile=app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExe}
MinVersion=6.1

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"

[CustomMessages]
french.ModelNote=Au premier lancement, Transkript téléchargera automatiquement le modèle de reconnaissance vocale Whisper (~488 Mo).%nAssurez-vous d'être connecté à Internet lors du premier démarrage.

[Tasks]
Name: "desktopicon"; Description: "Créer un raccourci sur le Bureau"; GroupDescription: "Icônes supplémentaires :"

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}";              Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app.ico"
Name: "{group}\Désinstaller {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}";        Filename: "{app}\{#AppExe}"; IconFilename: "{app}\app.ico"; Tasks: desktopicon

[UninstallDelete]
; Supprime les modèles Whisper téléchargés dans AppData lors de la désinstallation
Type: filesandordirs; Name: "{userappdata}\SuperTranscript"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    MsgBox(CustomMessage('ModelNote'), mbInformation, MB_OK);
end;
