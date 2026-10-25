param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBuildEnableWorkloadResolver = 'false'

[xml]$project = Get-Content -LiteralPath 'src/DeskShot/DeskShot.csproj'
$releaseVersion = [string]$project.Project.PropertyGroup.Version
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'La version del proyecto no es valida para publicar.' }
$archiveName = "DeskShot-$releaseVersion-win-x64.zip"
$releaseFolder = Join-Path $PSScriptRoot "artifacts/releases/v$releaseVersion"
$archivePath = Join-Path $releaseFolder $archiveName
if (Test-Path -LiteralPath $archivePath) { throw "Ya existe $archivePath. No se reemplazan paquetes existentes." }
$stagingFolder = Join-Path $PSScriptRoot ('artifacts/release-staging/' + [Guid]::NewGuid().ToString('N'))
$applicationFolder = Join-Path $stagingFolder 'DeskShot'
New-Item -ItemType Directory -Path $applicationFolder -Force | Out-Null

dotnet publish src/DeskShot/DeskShot.csproj -c Release -r win-x64 --self-contained true -o $applicationFolder --nologo -m:1 -nr:false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Fallo la publicacion autocontenida de DeskShot.' }

$runtimeConfiguration = Get-Content -LiteralPath (Join-Path $applicationFolder 'DeskShot.runtimeconfig.json') -Raw | ConvertFrom-Json
if (-not $runtimeConfiguration.runtimeOptions.includedFrameworks -or $runtimeConfiguration.runtimeOptions.frameworks) { throw 'El paquete requiere un runtime externo.' }
$restoredAssets = Get-Content -LiteralPath 'src/DeskShot/obj/project.assets.json' -Raw | ConvertFrom-Json
foreach ($framework in $runtimeConfiguration.runtimeOptions.includedFrameworks) {
    $packageName = $framework.name.ToLowerInvariant() + '.runtime.win-x64'
    $packageFolder = $null
    foreach ($packageRoot in $restoredAssets.packageFolders.PSObject.Properties.Name) {
        $candidateFolder = Join-Path (Join-Path $packageRoot $packageName) $framework.version
        if (Test-Path -LiteralPath $candidateFolder) { $packageFolder = $candidateFolder; break }
    }
    if (-not $packageFolder) { throw "No se encuentra el paquete de licencias de $($framework.name)." }
    if ($framework.name -eq 'Microsoft.NETCore.App') {
        Copy-Item -LiteralPath (Join-Path $packageFolder 'LICENSE.TXT') -Destination (Join-Path $applicationFolder 'ThirdParty/DotNet-Runtime-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $packageFolder 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $applicationFolder 'ThirdParty/DotNet-Runtime-NOTICES.txt')
    }
    elseif ($framework.name -eq 'Microsoft.WindowsDesktop.App') {
        Copy-Item -LiteralPath (Join-Path $packageFolder 'LICENSE') -Destination (Join-Path $applicationFolder 'ThirdParty/WindowsDesktop-Runtime-LICENSE.txt')
    }
}
foreach ($requiredFile in @('DeskShot.exe', 'DeskShot.dll', 'DeskShot.UpdateLauncher.exe', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll', 'Tesseract.dll', 'tessdata/spa.traineddata', 'tessdata/eng.traineddata', 'x64/tesseract50.dll', 'x64/leptonica-1.82.0.dll', 'ThirdParty/NOTICE.txt', 'ThirdParty/DotNet-Runtime-LICENSE.txt', 'ThirdParty/DotNet-Runtime-NOTICES.txt', 'ThirdParty/WindowsDesktop-Runtime-LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $applicationFolder $requiredFile))) { throw "El paquete esta incompleto: falta $requiredFile." }
}

if (-not $SkipTests) {
    $testFolder = Join-Path $stagingFolder 'Verification'
    dotnet publish tests/DeskShot.Tests/DeskShot.Tests.csproj -c Release -r win-x64 --self-contained true -o $testFolder --nologo -m:1 -nr:false -p:PublishSingleFile=false -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo preparar el verificador con el runtime incluido.' }
    $applicationHash = (Get-FileHash -LiteralPath (Join-Path $applicationFolder 'DeskShot.dll') -Algorithm SHA256).Hash
    $testedHash = (Get-FileHash -LiteralPath (Join-Path $testFolder 'DeskShot.dll') -Algorithm SHA256).Hash
    if ($applicationHash -ne $testedHash) { throw 'El verificador no usa la misma compilacion de DeskShot que el paquete.' }
    & (Join-Path $testFolder 'DeskShot.Tests.exe') (Join-Path $stagingFolder 'Evidence') --skip-hotkeys
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas fallaron. No se genera la descarga.' }
}

$readmeText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'packaging/LEEME.txt')).Replace('{{VERSION}}', $releaseVersion)
[IO.File]::WriteAllText((Join-Path $applicationFolder 'LEEME.txt'), $readmeText, [Text.UTF8Encoding]::new($false))
New-Item -ItemType Directory -Path $releaseFolder -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::CreateFromDirectory($applicationFolder, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $true)
$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($archivePath + '.sha256', "$archiveHash  $archiveName`r`n", [Text.Encoding]::ASCII)
Write-Host "Descarga lista: $archivePath"
Write-Host "SHA256: $archiveHash"
