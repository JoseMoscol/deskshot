#ifndef AppVersion
  #error AppVersion no definido
#endif
#ifndef SourceDir
  #error SourceDir no definido
#endif
#ifndef OutputDir
  #error OutputDir no definido
#endif
#ifndef VCRedist
  #error VCRedist no definido
#endif

[Setup]
AppId=DeskShot
AppName=DeskShot
AppVersion={#AppVersion}
AppPublisher=Jose Moscol
AppPublisherURL=https://github.com/JoseMoscol/deskshot
AppSupportURL=https://github.com/JoseMoscol/deskshot/issues
AppUpdatesURL=https://github.com/JoseMoscol/deskshot/releases/latest
DefaultDirName={localappdata}\Programs\DeskShot
DefaultGroupName=DeskShot
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=DeskShot-{#AppVersion}-win-x64-Setup
SetupIconFile=..\src\DeskShot\Assets\deskshot.ico
UninstallDisplayIcon={app}\DeskShot.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=no
RestartApplications=no
AppMutex=Local\DeskShotInstallerGuard
SetupMutex=Local\DeskShotSetup
UsePreviousAppDir=yes
UsePreviousTasks=yes
ChangesAssociations=no

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "Crear un acceso directo en el escritorio"; Flags: unchecked

[Files]
Source: "{#VCRedist}"; DestName: "vc_redist.x64.exe"; Flags: dontcopy
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DeskShot"; Filename: "{app}\DeskShot.exe"; WorkingDir: "{app}"
Name: "{group}\Manual de DeskShot"; Filename: "{app}\MANUAL.txt"
Name: "{autodesktop}\DeskShot"; Filename: "{app}\DeskShot.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\DeskShot.exe"; Description: "Abrir DeskShot"; Flags: nowait postinstall skipifsilent

[Code]
var
  RuntimeNeedsRestart: Boolean;

function HasVCRuntime: Boolean;
var
  MS, LS: Cardinal;
begin
  // Tesseract 5.2 usa el runtime MSVC v14. Se admite una version igual o
  // posterior a 14.29, sin depender de la vista del registro de Windows.
  Result := GetVersionNumbers(ExpandConstant('{sys}\vcruntime140.dll'), MS, LS);
  if Result then
    Result := (MS > 14 * 65536 + 29) or
      ((MS = 14 * 65536 + 29) and (LS >= 30139 * 65536));
  Result := Result and FileExists(ExpandConstant('{sys}\msvcp140.dll'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  if HasVCRuntime then begin
    Log('Visual C++ x64 disponible; no se modifica el runtime.');
    exit;
  end;
  ExtractTemporaryFile('vc_redist.x64.exe');
  if not ShellExec('runas', ExpandConstant('{tmp}\vc_redist.x64.exe'),
      '/install /passive /norestart', '', SW_SHOWNORMAL,
      ewWaitUntilTerminated, ExitCode) then begin
    Result := 'No se pudo preparar Visual C++ para el OCR. Autoriza su instalacion e intentalo de nuevo. Codigo: ' + IntToStr(ExitCode);
    exit;
  end;
  if ExitCode = 3010 then begin
    RuntimeNeedsRestart := True;
    if not HasVCRuntime then begin
      NeedsRestart := True;
      Result := 'Visual C++ requiere reiniciar Windows. Reinicia y vuelve a ejecutar el instalador de DeskShot.';
    end;
  end else if (ExitCode <> 0) or not HasVCRuntime then
    Result := 'No se pudo completar Visual C++ para el OCR. Codigo: ' + IntToStr(ExitCode) + '. La instalacion de DeskShot no ha comenzado.';
end;

function NeedRestart: Boolean;
begin
  Result := RuntimeNeedsRestart;
end;
