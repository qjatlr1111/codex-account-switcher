@echo off
setlocal
set "APP_EXE=%~dp0CodexAccountWidget\bin\Debug\net8.0-windows\CodexAccountSwitcher.exe"

if not exist "%APP_EXE%" (
    rem 실행 파일이 없을 때만 빌드합니다.
    dotnet build "%~dp0CodexAccountWidget.sln"
    if errorlevel 1 exit /b 1
)

start "" "%APP_EXE%"
endlocal
