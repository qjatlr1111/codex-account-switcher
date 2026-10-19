@echo off
setlocal
set "APP_EXE=%~dp0ClaudeAccountWidget\bin\Debug\net8.0-windows\ClaudeAccountSwitcher.exe"

if not exist "%APP_EXE%" (
    rem 실행 파일이 없을 때만 빌드합니다.
    dotnet build "%~dp0ClaudeAccountWidget\ClaudeAccountWidget.csproj"
    if errorlevel 1 exit /b 1
)

start "" "%APP_EXE%"
endlocal
