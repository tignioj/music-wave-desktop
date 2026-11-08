@echo off
cd /d "%~dp0"
if exist "artifacts\MusicWave\MusicWave.exe" (
  start "" "artifacts\MusicWave\MusicWave.exe"
) else (
  dotnet run --project "src\MusicWave\MusicWave.csproj" -c Release
)
