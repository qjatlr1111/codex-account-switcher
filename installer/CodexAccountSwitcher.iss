#ifndef PublishDir
  #error PublishDir must be provided.
#endif
#ifndef OutputDir
  #error OutputDir must be provided.
#endif
#ifndef ApplicationVersion
  #define ApplicationVersion "1.1.0"
#endif

[Setup]
AppId={{C92F6E91-DC38-4D83-A989-590B4479968B}
AppName=Codex 계정 전환 위젯
AppVersion={#ApplicationVersion}
AppPublisher=qjatlr1111
AppPublisherURL=https://github.com/qjatlr1111/codex-account-switcher
AppSupportURL=https://github.com/qjatlr1111/codex-account-switcher/issues
DefaultDirName={localappdata}\Programs\Codex Account Switcher
DefaultGroupName=Codex 계정 전환 위젯
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=CodexAccountSwitcher-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\CodexAccountSwitcher.exe
VersionInfoVersion={#ApplicationVersion}.0
VersionInfoProductName=Codex Account Switcher
VersionInfoDescription=Codex 계정 전환 작업 표시줄 위젯 설치 프로그램

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕 화면 바로 가기 만들기"; GroupDescription: "추가 바로 가기:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\CodexAccountSwitcher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Codex 계정 전환 위젯"; Filename: "{app}\CodexAccountSwitcher.exe"
Name: "{autodesktop}\Codex 계정 전환 위젯"; Filename: "{app}\CodexAccountSwitcher.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "CodexAccountSwitcher"; ValueData: "&quot;{app}\CodexAccountSwitcher.exe&quot;"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\CodexAccountSwitcher.exe"; Description: "Codex 계정 전환 위젯 실행"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/c taskkill /IM CodexAccountSwitcher.exe /F"; Flags: runhidden; RunOnceId: "StopWidget"
