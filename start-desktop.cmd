@echo off
cd /d "%~dp0"
if exist "desktop\bin\Release\net8.0-windows\ASH.exe" (
 start "" "desktop\bin\Release\net8.0-windows\ASH.exe"
) else (
 dotnet run --project desktop\Ash.Nexus.csproj -c Release
)

