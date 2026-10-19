# Claude 계정 작업 표시줄 위젯

Codex 계정 위젯과 같은 방식으로 Windows 11 작업 표시줄 왼쪽에 붙어 **현재 Claude 계정과 5시간·주간 잔여 사용량**을 보여주는 WPF 위젯입니다. 위젯을 누르면 등록한 계정 목록이 위로 펼쳐지고, 계정을 누르면 **Claude Code 로그인**이 그 계정으로 전환됩니다.

> 비공식 커뮤니티 도구이며 Anthropic의 공식·제휴·승인 제품이 아닙니다. 사용량 조회에 쓰는 OAuth 엔드포인트는 공개 API가 아니므로 예고 없이 바뀔 수 있습니다.

## 다운로드

Claude 위젯은 Codex 위젯과 같은 저장소에서 `claude-v` 태그로 따로 배포합니다. 저장소의 "Latest" 표시는 Codex 위젯이 계속 쓰므로, Claude 위젯은 아래 버전별 링크로 받습니다.

- [ClaudeAccountSwitcher-Setup.exe (claude-v1.0.0 설치 파일)](https://github.com/qjatlr1111/codex-account-switcher/releases/download/claude-v1.0.0/ClaudeAccountSwitcher-Setup.exe)
- [ClaudeAccountSwitcher.exe (claude-v1.0.0 무설치 파일)](https://github.com/qjatlr1111/codex-account-switcher/releases/download/claude-v1.0.0/ClaudeAccountSwitcher.exe)
- [SHA256SUMS.txt (claude-v1.0.0 무결성 확인 파일)](https://github.com/qjatlr1111/codex-account-switcher/releases/download/claude-v1.0.0/SHA256SUMS.txt)
- [Claude 위젯 릴리스 전체 보기](https://github.com/qjatlr1111/codex-account-switcher/releases?q=claude-v&expanded=true)

설치형은 Windows 로그인 자동 실행과 시작 메뉴·제거 프로그램이 함께 등록됩니다. 두 파일 모두 코드 서명되지 않아 처음 실행할 때 SmartScreen 경고가 나올 수 있습니다. 설치 후에는 트레이 메뉴의 `업데이트 확인`으로 새 `claude-v` 릴리스를 받아 SHA-256을 확인한 뒤 자동 설치합니다.

## Codex 위젯과 무엇이 같고 다른가

| 기능 | Codex 위젯 | Claude 위젯 |
| --- | --- | --- |
| 작업 표시줄 오버레이·호버 표시 | ✅ | ✅ (Codex 위젯이 떠 있으면 그 오른쪽에 나란히 배치) |
| 현재 계정·요금제 표시 | ✅ | ✅ (`MAX 20x`, `PRO` 등) |
| 단기·주간 잔여량 | ✅ App Server | ✅ `5시간` / `주간` + 초기화까지 남은 시간, Opus·Sonnet 주간 한도(있을 때) |
| 계정 추가 | 공식 브라우저 로그인 | 공식 `claude auth login` 창 (구버전은 `claude` 첫 실행 로그인) |
| 현재 로그인 가져오기 | – | ✅ 이미 로그인된 계정을 재로그인 없이 등록 |
| 계정 전환 | Codex 앱 종료 → 인증 교체 → 재시작 | 실행 중인 Claude Code CLI 세션 종료(선택) → 인증 교체 → 새 세션부터 적용 |
| 실행 중일 때만 표시 | Codex 창 감지 | Claude 데스크톱 창 또는 Claude Code CLI 감지 |
| model provider 전환 | ✅ | – (Claude Code는 `ANTHROPIC_BASE_URL` 등 환경 변수 방식) |

### 연출

- **링 게이지**: 아이콘 둘레에 5시간 잔여량이 호(arc)로 차오르고, 같은 호를 넓은 반투명 펜으로 한 번 더 그려 블룸을 냅니다. 15% 이하이면 붉은색으로 바뀌고 뒤쪽 글로우가 맥동합니다.
- **스파크 아이콘**: 마우스를 올리면 BackEase로 72° 튕기듯 회전하며 커집니다.
- **패널 등장**: 살짝 작은 크기에서 튀어오르듯(pop) 나타나고, 계정 카드가 45ms 간격으로 순서대로 떠오르며, 사용량 막대가 0에서부터 차오릅니다.
- **헤더 빛줄기**: 구분선을 따라 코랄 하이라이트가 흐릅니다(패널이 보일 때만 재생).
- **전환 화면**: 서로 반대로 도는 점선 궤도 두 개, 공전하는 혜성, 숨 쉬는 스파크, 4단계 진행 점.
- 반복 애니메이션은 패널이 숨겨지면 멈춰 CPU를 쓰지 않습니다.

## 실행

.NET 8 SDK와 Claude Code가 설치되어 있어야 합니다.

```powershell
dotnet build ClaudeAccountWidget/ClaudeAccountWidget.csproj
dotnet run --project ClaudeAccountWidget
```

또는 루트의 `run-claude.cmd`를 실행합니다. `--diagnostic` 인수를 주면 작업 표시줄에 붙지 않는 일반 창으로 떠서 디자인을 확인하기 좋습니다.

```powershell
dotnet run --project ClaudeAccountWidget -- --diagnostic
```

## 배포 파일 만들기

Inno Setup 6이 설치된 Windows에서 루트의 `Build-ClaudeInstaller.cmd`를 실행하거나 PowerShell에서 실행합니다.

```powershell
./scripts/Build-ClaudeInstaller.ps1 -ApplicationVersion 1.0.0
```

```text
dist/claude/
├─ ClaudeAccountSwitcher.exe
├─ ClaudeAccountSwitcher-Setup.exe
└─ SHA256SUMS.txt
```

## GitHub 배포

[`.github/workflows/claude-release.yml`](../.github/workflows/claude-release.yml)이 `claude-v1.0.0` 같은 태그를 받으면 Windows runner에서 위 세 파일을 만들어 GitHub Release에 올립니다.

- Codex 워크플로(`release.yml`)는 `v*.*.*` 태그에만 반응하므로 두 앱의 빌드가 섞이지 않습니다.
- Claude 릴리스는 항상 `--latest=false`로 게시합니다. 그래서 Codex의 `releases/latest/download/...` 링크와 Codex 앱 업데이트 확인은 계속 Codex 릴리스를 가리킵니다.
- 새 버전을 낼 때는 `ClaudeAccountWidget.csproj`의 `<Version>`과 이 문서의 다운로드 링크 태그를 함께 올립니다.

```powershell
git add .
git commit -m "Claude 계정 위젯 추가"
git push
git tag claude-v1.0.0
git push origin claude-v1.0.0
```

배포 전에는 저장소의 `AGENTS.md` 규칙대로 변경 파일·패키지에 인증 파일이나 개인 정보가 없는지 점검합니다.

## 동작 방식

- 계정마다 `%USERPROFILE%\.claude-account-switcher\profiles\<id>` 폴더를 만들고, 공식 다중 계정 방식대로 이 폴더를 `CLAUDE_CONFIG_DIR`로 지정해 로그인합니다.
- 전환할 때는
  1. (선택) 실행 중인 Claude Code CLI 세션 종료
  2. 현재 활성 계정의 최신 인증(Claude Code가 갱신한 토큰)을 그 계정의 프로필로 되돌려 보관
  3. 선택한 계정의 `.credentials.json`을 `%USERPROFILE%\.claude\.credentials.json`에 복사(기존 파일은 `.credentials.widget-backup.json`으로 1개 백업)
  4. `%USERPROFILE%\.claude.json`의 `oauthAccount`만 교체(프로젝트 기록·MCP 설정 등 나머지는 그대로)
  5. 1.2초 뒤 인증 파일이 되돌려지지 않았는지 확인
- 사용량은 `https://api.anthropic.com/api/oauth/usage`의 `five_hour`/`seven_day`의 `utilization`을 읽어 `남은 사용량 = 100 - utilization`으로 표시합니다. 조회 한도를 고려해 활성 계정은 1분, 전체는 5분마다 갱신합니다.
- 활성 계정의 토큰은 Claude Code가 갱신하도록 두고, 비활성 계정의 토큰이 만료되면 위젯이 refresh token으로 갱신해 프로필에 다시 저장합니다. 갱신이 거부되면 "전환하면 Claude Code가 갱신합니다"라고 표시합니다.
- 터미널에서 `/login`으로 계정을 바꿔도 2초 안에 위젯의 활성 계정 표시가 따라갑니다.

## 주의사항

- **켜 둔 Claude Code 세션**은 메모리에 이전 계정의 토큰을 들고 있다가 갱신 시 인증 파일을 다시 쓸 수 있습니다. 그래서 전환 확인 창에서 세션 종료를 기본으로 권장합니다. 종료한 세션은 같은 폴더에서 `claude --continue`로 이어갈 수 있습니다.
- **Claude 데스크톱 앱**의 로그인은 별도로 관리되므로 이 위젯이 바꾸지 않습니다. 데스크톱 앱은 "실행 중일 때만 표시" 판단에만 사용합니다.
- npm(`node.exe`)으로 설치한 Claude Code 세션은 프로세스 이름으로 구분할 수 없어 자동 종료 대상에서 빠집니다. 네이티브 설치(`claude.exe`)를 권장합니다.
- `.credentials.json`에는 액세스·리프레시 토큰이 평문으로 들어 있습니다. 프로필 폴더는 현재 사용자·SYSTEM·Administrators만 접근하도록 ACL을 제한하지만, 공유 PC에서는 사용하지 마세요.
- 활성 계정은 제거할 수 없습니다. 우클릭 제거는 위젯 전용 프로필 폴더만 지우며 Claude 계정 자체를 삭제하지 않습니다.

## 점검 명령

```powershell
dotnet run --project ClaudeAccountWidget.Smoke -- --detect-claude   # claude 프로세스 분류와 CLI 경로
dotnet run --project ClaudeAccountWidget.Smoke -- --check-current   # 현재 로그인 계정의 사용량 조회(이메일은 마스킹)
dotnet run --project ClaudeAccountWidget.Smoke -- --check-updates   # claude-v 릴리스 업데이트 확인
```

## 구성

```text
ClaudeAccountWidget/
├─ OverlayWindow.xaml              작업 표시줄 위젯 (링 게이지 + 스파크)
├─ AccountPanelWindow.xaml         계정 목록 / 전환 진행 화면
├─ SwitchConfirmationWindow.xaml   전환 확인 + CLI 세션 종료 선택
├─ Controls/
│  ├─ AnimatedMeter.cs             목표값을 부드럽게 따라가는 미터 베이스
│  ├─ ArcGauge.cs                  블룸이 있는 원형 게이지
│  ├─ UsageBar.cs                  하이라이트·광점이 있는 막대
│  └─ SparkGlyph.cs                방사형 스파크 아이콘
├─ Services/
│  ├─ ClaudePaths.cs               Claude Code 설정·인증 파일 위치
│  ├─ ClaudeCredentialStore.cs     .credentials.json / .claude.json 원자적 읽기·쓰기
│  ├─ ClaudeApiClient.cs           사용량·프로필 조회, 토큰 갱신
│  ├─ ClaudeAccountService.cs      로그인, 가져오기, 전환, 사용량 새로고침
│  ├─ ClaudeProcessMonitor.cs      데스크톱 앱 / CLI 세션 구분·종료
│  ├─ UpdateCheckService.cs        claude-v 릴리스 업데이트 확인
│  ├─ UpdateInstallerService.cs    설치 파일 다운로드·SHA-256 검증
│  └─ ProfileStore.cs              위젯 계정 목록 저장
└─ ViewModels/MainViewModel.cs
```
