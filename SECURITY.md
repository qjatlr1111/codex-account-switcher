# 보안 안내

## 지원 범위

이 프로젝트는 OpenAI의 공식 제품이 아닌 비공식 Windows 유틸리티입니다. Codex의 로컬 파일 기반 인증 캐시와 App Server를 사용하므로 Codex 업데이트에 따라 호환성이 달라질 수 있습니다.

## 민감한 인증정보

Codex의 `auth.json`에는 액세스 토큰이 들어 있습니다. 비밀번호와 동일하게 취급하고 다음 정보를 GitHub 이슈, Pull Request, 채팅 또는 공개 로그에 첨부하지 마세요.

- `auth.json`, `auth.widget-backup.json`
- `%USERPROFILE%\.codex-account-switcher` 폴더
- `%USERPROFILE%\.codex`의 인증 관련 파일
- 액세스 토큰, 리프레시 토큰, API 키

앱은 `%USERPROFILE%\.codex-account-switcher`의 ACL을 현재 Windows 사용자, SYSTEM, Administrators로 제한합니다. Codex 호환성을 위해 인증 캐시는 여전히 로컬 디스크에 평문으로 존재하므로 공유 PC나 공용 Windows 계정에서는 사용하지 않는 것을 권장합니다.

## 취약점 신고

보안 문제를 발견한 경우 실제 토큰이나 개인 계정 정보를 공개 이슈에 포함하지 마세요. 저장소 소유자에게 재현에 필요한 최소한의 비식별 정보만 전달하세요. 인증정보가 이미 노출됐다면 먼저 Codex에서 로그아웃하고 관련 ChatGPT/OpenAI 세션을 폐기한 뒤 다시 로그인하세요.

## 배포 파일

현재 Windows 실행 파일과 설치 프로그램은 코드 서명 인증서로 서명되지 않았습니다. GitHub Release의 `SHA256SUMS.txt`로 파일 해시를 확인할 수 있지만, Windows SmartScreen 경고가 표시될 수 있습니다.
