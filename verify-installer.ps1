param([Parameter(Mandatory = $true)][string]$Installer)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$installerPath = (Resolve-Path -LiteralPath $Installer).Path
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\DeskShot_is1'
if (Test-Path -LiteralPath $registryPath) { throw 'Ya hay una instalacion registrada de DeskShot. No se modifica para hacer esta prueba.' }
$shortcutFolder = Join-Path ([Environment]::GetFolderPath('Programs')) 'DeskShot'
# Inno ignora /GROUP cuando DisableProgramGroupPage=yes: no utilizar accesos existentes.
if (Test-Path -LiteralPath $shortcutFolder) { throw 'Ya existe el grupo de Inicio DeskShot. No se modifica para hacer esta prueba.' }
if (Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Programs/DeskShot')) { throw 'Ya existe una carpeta de DeskShot instalada. No se utiliza para hacer esta prueba.' }
$runtimeVersion = (Get-Item -LiteralPath (Join-Path $env:WINDIR 'System32/vcruntime140.dll')).VersionInfo
if ($runtimeVersion.FileMajorPart -lt 14 -or ($runtimeVersion.FileMajorPart -eq 14 -and $runtimeVersion.FileMinorPart -lt 29)) { throw 'Esta prueba requiere Visual C++ instalado; no ejecuta instalaciones elevadas del runtime.' }
$testId = [Guid]::NewGuid().ToString('N')
$verificationRoot = Join-Path $PSScriptRoot "artifacts/installer-verification/$testId"
$installationPath = Join-Path $verificationRoot 'Installed'
New-Item -ItemType Directory -Path $verificationRoot -Force | Out-Null
$settingsPath = Join-Path $env:LOCALAPPDATA 'DeskShot/settings.json'
$settingsHash = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', '/TASKS=', ('/DIR="' + $installationPath + '"'))
function Invoke-TestSetup([string]$LogName) {
    $runArguments = $arguments + ('/LOG="' + (Join-Path $verificationRoot $LogName) + '"')
    $run = Start-Process -FilePath $installerPath -ArgumentList $runArguments -WindowStyle Hidden -Wait -PassThru
    return $run.ExitCode
}
try {
if ((Invoke-TestSetup 'install.log') -ne 0) { throw 'Fallo la instalacion de prueba.' }
$installedExe = Join-Path $installationPath 'DeskShot.exe'
foreach ($file in @('DeskShot.exe', 'DeskShot.UpdateLauncher.exe', 'coreclr.dll', 'tessdata/spa.traineddata', 'tessdata/eng.traineddata', 'MANUAL.txt', 'unins000.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $installationPath $file))) { throw "Falta un archivo instalado: $file" }
}
$registration = Get-ItemProperty -LiteralPath $registryPath
if ($registration.InstallLocation.TrimEnd('\') -ne $installationPath.TrimEnd('\')) { throw 'El registro no apunta a la carpeta de prueba.' }
if (-not (Test-Path -LiteralPath (Join-Path $shortcutFolder 'DeskShot.lnk'))) { throw 'No se creo el acceso directo de Inicio.' }
$beforeHash = (Get-FileHash -LiteralPath $installedExe).Hash
Write-Host 'PASS Instalacion por usuario, acceso de Inicio, registro y modelos OCR.'

$guard = [Threading.Mutex]::new($false, 'Local\DeskShotInstallerGuard')
try { $blockedExitCode = Invoke-TestSetup 'blocked.log' } finally { $guard.Dispose() }
if ($blockedExitCode -eq 0 -or (Get-FileHash -LiteralPath $installedExe).Hash -ne $beforeHash) { throw 'El instalador no bloqueo la actualizacion de una instancia en uso.' }
Write-Host 'PASS Instancia abierta bloquea actualizar sin cerrar procesos.'

[IO.File]::WriteAllText((Join-Path $installationPath 'user-owned.txt'), 'Archivo de usuario: conservar al quitar DeskShot.')
if ((Invoke-TestSetup 'update.log') -ne 0) { throw 'Fallo la reinstalacion en la misma carpeta.' }
if (-not (Test-Path -LiteralPath (Join-Path $installationPath 'user-owned.txt'))) { throw 'La actualizacion elimino un archivo ajeno.' }
Write-Host 'PASS Reinstalacion conserva la carpeta, el acceso y los archivos ajenos.'

$uninstallArguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', ('/LOG="' + (Join-Path $verificationRoot 'uninstall.log') + '"'))
$uninstall = Start-Process -FilePath (Join-Path $installationPath 'unins000.exe') -ArgumentList $uninstallArguments -WindowStyle Hidden -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw 'Fallo la desinstalacion de prueba.' }
if ((Test-Path -LiteralPath $installedExe) -or (Test-Path -LiteralPath $registryPath) -or (Test-Path -LiteralPath (Join-Path $shortcutFolder 'DeskShot.lnk'))) { throw 'La desinstalacion dejo archivos de programa, accesos o registro.' }
if (-not (Test-Path -LiteralPath (Join-Path $installationPath 'user-owned.txt'))) { throw 'La desinstalacion elimino un archivo ajeno.' }
$afterSettingsHash = if (Test-Path -LiteralPath $settingsPath) { (Get-FileHash -LiteralPath $settingsPath).Hash } else { $null }
if ($settingsHash -ne $afterSettingsHash) { throw 'Se modificaron las preferencias del usuario.' }
Write-Host 'PASS Desinstalacion retira programa y accesos, conserva archivos ajenos y preferencias.'
Write-Host "Evidencias: $verificationRoot"
}
finally {
    $remainingRegistration = Get-ItemProperty -LiteralPath $registryPath -ErrorAction SilentlyContinue
    $remainingUninstaller = Join-Path $installationPath 'unins000.exe'
    if ($remainingRegistration -and $remainingRegistration.InstallLocation.TrimEnd('\') -eq $installationPath.TrimEnd('\') -and (Test-Path -LiteralPath $remainingUninstaller)) {
        $cleanup = Start-Process -FilePath $remainingUninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -WindowStyle Hidden -Wait -PassThru
        if ($cleanup.ExitCode -ne 0) { Write-Warning 'La instalacion de prueba necesita revision; consulta las evidencias.' }
    }
}
