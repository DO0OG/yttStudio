# yttStudio v0.2.4

## 이번 변경

- **YouTube 재생 JavaScript 런타임 보완** — 현재 yt-dlp의 YouTube 추출이 요구하는 외부 JavaScript challenge 런타임을 프로그램이 직접 준비합니다. 호환 Deno가 없으면 공식 `denoland/deno v2.9.6` 자산을 사용자 영역에 내려받아 파일 크기와 SHA-256을 검증한 뒤 사용합니다.
- **yt-dlp에 Deno 경로 명시** — yttStudio의 yt-dlp 사전 확인에는 관리 Deno 경로를 `--js-runtimes deno:<path>`로 전달합니다. Deno 설치 디렉터리는 현재 프로세스 `PATH`에도 등록되어 libmpv의 `ytdl_hook`이 실행하는 yt-dlp에서도 같은 런타임을 찾을 수 있습니다.
- **YouTube 오류 분류 수정** — `HTTP 403`, `HTTP 429`, `Sign in to confirm you're not a bot`을 일반 네트워크 장애로 오분류하지 않습니다. 접근 거부와 실제 DNS/연결/5xx 네트워크 오류를 분리하고, 사전 확인 실패 진단을 로그에 남깁니다.
- **기존 기본 재생 구조 유지** — libmpv와 yt-dlp의 프로그램 내부 설치 흐름은 유지됩니다. 로컬 영상과 YouTube 주소 재생 모두 기본 기능이며 별도 수동 설치를 요구하지 않습니다.
- **외부 런타임 비번들 게이트 강화** — yt-dlp/libmpv/KMediaMpv에 더해 `deno`/`deno.exe`도 yttStudio ZIP·설치 파일·DMG·AppImage에 직접 포함되지 않도록 릴리스 단계에서 검사합니다.

## 설치

.NET 런타임은 각 배포물에 포함됩니다. 지원 플랫폼에서는 영상 재생을 위해 사용자가 libmpv, yt-dlp 또는 Deno를 미리 설치할 필요가 없습니다.

| 파일 | 대상 | 설치 방법 |
|---|---|---|
| `yttStudio-v0.2.4-win-x64-setup.exe` | Windows x64 | 실행하면 시작 메뉴 등록과 파일 연결까지 처리합니다 |
| `yttStudio-v0.2.4-win-x64.zip` | Windows x64 (포터블) | 압축을 풀고 `YttStudio.App.exe` 실행 |
| `yttStudio-v0.2.4-osx-arm64.dmg` | macOS Apple Silicon | 열어서 `yttStudio.app`을 `Applications`로 이동 |
| `yttStudio-v0.2.4-osx-arm64.tar.gz` | macOS Apple Silicon | 앱 번들 압축본 |
| `yttStudio-v0.2.4-linux-x86_64.AppImage` | Linux x64 | 실행 권한을 준 뒤 실행 |
| `yttStudio-v0.2.4-linux-x64.tar.gz` | Linux x64 | 압축을 풀어 실행 |

## 영상 및 YouTube 런타임

기존에 사용자가 지정한 호환 런타임이 있으면 그것을 우선 사용합니다. 없을 때만 프로그램 내부 설치가 동작합니다.

- Windows x64 libmpv: `zhongfly/mpv-winbuild` `mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z`
- macOS arm64 / Linux x64 libmpv: `Shusek/KMediaMpv v0.2.9` `kmedia-mpv-0.2.9-runtime-desktop.jar`의 해당 플랫폼 네이티브 트리
- yt-dlp: 공식 `yt-dlp/yt-dlp 2026.08.19` 플랫폼별 standalone 자산
- Deno: 공식 `denoland/deno v2.9.6` 플랫폼별 `deno` ZIP 자산, yt-dlp 최소 요구 버전 2.3.0 이상

자동 설치되는 제3자 런타임은 yttStudio의 MIT 라이선스로 재라이선스되지 않으며 각각의 upstream 라이선스를 따릅니다. Deno는 MIT License입니다. 정확한 pin·해시·provenance와 라이선스 경계는 `docs/DEPENDENCIES.md`, `docs/THIRD-PARTY-NOTICES.md`, `docs/LEGAL-COMPLIANCE-AUDIT.md`를 참고하세요.

libmpv는 **도구 → 설정 → 영상**에서 재설치하거나 직접 경로를 지정할 수도 있습니다.

## 서명

코드 서명과 notarization은 아직 없습니다. 첫 실행에 운영체제 경고가 뜰 수 있습니다.

- Windows: SmartScreen에서 **추가 정보 → 실행**
- macOS: `yttStudio.app` 우클릭 → **열기**
