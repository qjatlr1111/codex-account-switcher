#ifndef PublishDir
  #error PublishDir must be provided.
#endif
#ifndef OutputDir
  #error OutputDir must be provided.
#endif
#ifndef ApplicationVersion
  #define ApplicationVersion "1.0.0"
#endif

[Setup]
AppId={{F14CB180-883F-460E-919A-4D0964F0E416}
AppName=Claude 계정 전환 위젯
AppVersion={#ApplicationVersion}
AppPublisher=qjatlr1111
AppPublisherURL=https://github.com/qjatlr1111/codex-account-switcher
AppSupportURL=https://github.com/qjatlr1111/codex-account-switcher/issues
DefaultDirName={localappdata}\Programs\Claude Account Switcher
DefaultGroupName=Claude 계정 전환 위젯
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=ClaudeAccountSwitcher-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\ClaudeAccountSwitcher.exe
VersionInfoVersion={#ApplicationVersion}.0
VersionInfoProductName=Claude Account Switcher
VersionInfoDescription=Claude 계정 전환 작업 표시줄 위젯 설치 프로그램

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면 바로 가기 만들기"; GroupDescription: "추가 바로 가기:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\ClaudeAccountSwitcher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Claude 계정 전환 위젯"; Filename: "{app}\ClaudeAccountSwitcher.exe"
Name: "{autodesktop}\Claude 계정 전환 위젯"; Filename: "{app}\ClaudeAccountSwitcher.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClaudeAccountSwitcher"; ValueData: """{app}\ClaudeAccountSwitcher.exe"""; Flags: uninsdeletevalue

[Run]
Filename: "{app}\ClaudeAccountSwitcher.exe"; Description: "Claude 계정 전환 위젯 실행"; Flags: nowait postinstall

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /IM ClaudeAccountSwitcher.exe /F"; Flags: runhidden; RunOnceId: "StopClaudeWidget"
