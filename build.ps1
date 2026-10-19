param([switch]$SkipTests, [switch]$CheckDesktop)
$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBuildEnableWorkloadResolver = 'false'

dotnet build DeskShot.slnx -c Release --nologo -m:1 -nr:false
if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar DeskShot.' }

if (-not $SkipTests) {
    $testArguments = @('artifacts/verification')
    if ($CheckDesktop) { $testArguments += '--desktop' }
    dotnet tests/DeskShot.Tests/bin/Release/net10.0-windows/DeskShot.Tests.dll @testArguments
    if ($LASTEXITCODE -ne 0) { throw 'Falló la verificación. No se publicó el ejecutable.' }
}

dotnet publish src/DeskShot/DeskShot.csproj -c Release --no-restore --self-contained false -o artifacts/DeskShot-0.5 --nologo -m:1 -nr:false
if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar DeskShot.' }
Write-Host "Listo: $PSScriptRoot\artifacts\DeskShot-0.5\DeskShot.exe"
