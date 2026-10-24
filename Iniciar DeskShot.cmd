@echo off
setlocal
cd /d "%~dp0"
if exist "artifacts\DeskShot-0.6.0\DeskShot.exe" (
    start "" "artifacts\DeskShot-0.6.0\DeskShot.exe"
    exit /b 0
)
set DOTNET_CLI_HOME=%CD%\.dotnet
set DOTNET_CLI_TELEMETRY_OPTOUT=1
set DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
set DOTNET_GENERATE_ASPNET_CERTIFICATE=false
set MSBuildEnableWorkloadResolver=false
echo Preparando DeskShot. Se necesita el SDK de .NET 10 para esta primera compilacion.
dotnet publish src\DeskShot\DeskShot.csproj -c Release --self-contained false -o artifacts\DeskShot-0.6.0 --nologo -m:1 -nr:false
if errorlevel 1 (
    echo No se pudo compilar. Revisa el mensaje anterior.
    pause
    exit /b 1
)
start "" "artifacts\DeskShot-0.6.0\DeskShot.exe"
