@echo off
rem Compileaza un singur EXE in folderul "publish" (necesita .NET 8 SDK)
dotnet publish "%~dp0DismRepairCenter.csproj" -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o "%~dp0publish"
if errorlevel 1 (echo. & echo BUILD FAILED & pause & exit /b 1)
echo.
echo Gata: %~dp0publish\DismRepairCenter.exe
pause
