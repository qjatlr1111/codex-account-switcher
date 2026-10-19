@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Build-ClaudeInstaller.ps1" %*
set "EXIT_CODE=%ERRORLEVEL%"
if not "%EXIT_CODE%"=="0" (
    echo.
    echo Installer build failed with exit code %EXIT_CODE%.
    pause
)
exit /b %EXIT_CODE%
