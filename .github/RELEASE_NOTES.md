# yttStudio v0.2.5

## 이번 변경

- **YouTube 주소 재생 실패 수정 (Deno 설치 파일 잠금)** — Deno 런타임을 내려받은 뒤 SHA-256 검증을 위해 같은 파일을 다시 열 때, 아직 닫히지 않은 다운로드 스트림과 충돌해 `다른 프로세스가 파일을 사용 중입니다` 오류로 항상 실패하던 문제를 고쳤습니다. 다운로드 스트림을 검증 전에 확실히 닫도록 변경했습니다. 같은 결함이 있던 libmpv 자동 설치 경로도 함께 수정했습니다.
- **오래된 yt-dlp 자동 대체** — 기존에는 시스템에 설치된 yt-dlp를 버전 확인 없이 그대로 사용해, `--js-runtimes` 옵션을 모르는 구버전에서 YouTube 사전 확인이 즉시 실패했습니다. 이제 이미 설치된 yt-dlp가 해당 옵션을 지원할 때만 재사용하고, 지원하지 않으면 공식 `yt-dlp 2026.08.19` 자산을 검증해 사용자 영역에 설치합니다.
- **중단된 설치 임시 폴더 정리** — 이전 실패로 남은 `.deno-install-*` 임시 폴더를 다음 설치 시작 시 정리합니다. 기존에는 실패할 때마다 수십 MB가 쌓였습니다.

## 설치

.NET 런타임은 각 배포물에 포함됩니다. 지원 플랫폼에서는 영상 재생을 위해 사용자가 libmpv, yt-dlp 또는 Deno를 미리 설치할 필요가 없습니다.

| 파일 | 대상 | 설치 방법 |
|---|---|---|
| `yttStudio-v0.2.5-win-x64-setup.exe` | Windows x64 | 실행하면 시작 메뉴 등록과 파일 연결까지 처리합니다 |
| `yttStudio-v0.2.5-win-x64.zip` | Windows x64 (포터블) | 압축을 풀고 `YttStudio.App.exe` 실행 |
| `yttStudio-v0.2.5-osx-arm64.dmg` | macOS Apple Silicon | 열어서 `yttStudio.app`을 `Applications`로 이동 |
| `yttStudio-v0.2.5-osx-arm64.tar.gz` | macOS Apple Silicon | 앱 번들 압축본 |
| `yttStudio-v0.2.5-linux-x86_64.AppImage` | Linux x64 | 실행 권한을 준 뒤 실행 |
| `yttStudio-v0.2.5-linux-x64.tar.gz` | Linux x64 | 압축을 풀어 실행 |

## 영상 및 YouTube 런타임

기존에 사용자가 지정한 호환 런타임이 있으면 그것을 우선 사용합니다. 호환되지 않거나 없을 때만 프로그램 내부 설치가 동작합니다.

- Windows x64 libmpv: `zhongfly/mpv-winbuild` `mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z`
- macOS arm64 / Linux x64 libmpv: `Shusek/KMediaMpv v0.2.9` `kmedia-mpv-0.2.9-runtime-desktop.jar`의 해당 플랫폼 네이티브 트리
- yt-dlp: 공식 `yt-dlp/yt-dlp 2026.08.19` 플랫폼별 standalone 자산, `--js-runtimes` 지원 필요
- Deno: 공식 `denoland/deno v2.9.6` 플랫폼별 `deno` ZIP 자산, yt-dlp 최소 요구 버전 2.3.0 이상

자동 설치되는 제3자 런타임은 yttStudio의 MIT 라이선스로 재라이선스되지 않으며 각각의 upstream 라이선스를 따릅니다. Deno는 MIT License입니다. 정확한 pin·해시·provenance와 라이선스 경계는 `docs/DEPENDENCIES.md`, `docs/THIRD-PARTY-NOTICES.md`, `docs/LEGAL-COMPLIANCE-AUDIT.md`를 참고하세요.

libmpv는 **도구 → 설정 → 영상**에서 재설치하거나 직접 경로를 지정할 수도 있습니다.

## 서명

코드 서명과 notarization은 아직 없습니다. 첫 실행에 운영체제 경고가 뜰 수 있습니다.

- Windows: SmartScreen에서 **추가 정보 → 실행**
- macOS: `yttStudio.app` 우클릭 → **열기**
