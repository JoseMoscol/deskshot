param(
    [string]$Compiler = '',
    [string]$VCRedist = '',
    [switch]$SkipPackage
)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
[xml]$project = Get-Content -LiteralPath 'src/DeskShot/DeskShot.csproj'
$installerVersion = [string]$project.Project.PropertyGroup.Version
if ($installerVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Version no valida.' }
$releaseFolder = Join-Path $PSScriptRoot "artifacts/releases/v$installerVersion"
$setupPath = Join-Path $releaseFolder "DeskShot-$installerVersion-win-x64-Setup.exe"
if (Test-Path -LiteralPath $setupPath) { throw "Ya existe $setupPath. No se reemplazan instaladores publicados." }
if (-not $Compiler) { $Compiler = Join-Path $PSScriptRoot 'artifacts/tools/inno-6.7.3/ISCC.exe' }
if (-not (Test-Path -LiteralPath $Compiler)) { throw 'Indica -Compiler con la ruta a ISCC.exe de Inno Setup 6.7 o posterior.' }
if (-not $VCRedist) { $VCRedist = Join-Path $PSScriptRoot 'artifacts/tools/downloads/vc_redist.x64.exe' }
if (-not (Test-Path -LiteralPath $VCRedist)) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $VCRedist) -Force | Out-Null
    Invoke-WebRequest -Uri 'https://aka.ms/vc14/vc_redist.x64.exe' -OutFile $VCRedist
}
$signature = Get-AuthenticodeSignature -LiteralPath $VCRedist
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(?:^|, )O=Microsoft Corporation(?:,|$)') {
    throw 'El redistribuible no tiene una firma valida de Microsoft.'
}
if (-not $SkipPackage) {
    & (Join-Path $PSScriptRoot 'package-release.ps1')
    if (-not $?) { throw 'No se pudo preparar el paquete de aplicacion.' }
}
$archivePath = Join-Path $releaseFolder "DeskShot-$installerVersion-win-x64.zip"
if (-not (Test-Path -LiteralPath $archivePath)) { throw 'Falta el ZIP verificado de esta version.' }
$expectedHash = (Get-Content -LiteralPath ($archivePath + '.sha256') -Raw).Split(' ')[0]
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) { throw 'El ZIP no coincide con su SHA-256.' }
$installStaging = Join-Path $PSScriptRoot ('artifacts/installer-staging/' + [Guid]::NewGuid().ToString('N'))
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($archivePath, $installStaging)
$sourceFolder = Join-Path $installStaging 'DeskShot'
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'packaging/MANUAL.txt') -Destination (Join-Path $sourceFolder 'MANUAL.txt')
$runtimeHash = (Get-FileHash -LiteralPath $VCRedist -Algorithm SHA256).Hash.ToLowerInvariant()
$runtimeNotice = "Microsoft Visual C++ x64 Redistributable`r`nOrigen: https://aka.ms/vc14/vc_redist.x64.exe`r`nVersion: $((Get-Item -LiteralPath $VCRedist).VersionInfo.FileVersion)`r`nSHA256: $runtimeHash`r`nCopyright Microsoft Corporation. Se incluye el instalador oficial sin modificar.`r`nLa instalacion y el uso se rigen por sus terminos de licencia.`r`n"
[IO.File]::WriteAllText((Join-Path $sourceFolder 'ThirdParty/VisualCpp-Redistributable.txt'), $runtimeNotice, [Text.UTF8Encoding]::new($false))
& $Compiler '/Q' "/DAppVersion=$installerVersion" "/DSourceDir=$sourceFolder" "/DOutputDir=$releaseFolder" "/DVCRedist=$VCRedist" (Join-Path $PSScriptRoot 'packaging/DeskShot.iss')
if ($LASTEXITCODE -ne 0) { throw 'Fallo la compilacion del instalador.' }
$setupHash = (Get-FileHash -LiteralPath $setupPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($setupPath + '.sha256', "$setupHash  $([IO.Path]::GetFileName($setupPath))`r`n", [Text.Encoding]::ASCII)
Write-Host "Instalador listo: $setupPath"
Write-Host "SHA256: $setupHash"
