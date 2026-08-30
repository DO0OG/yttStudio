# yttStudio v0.2.3

## 이번 변경

- **영상 재생을 기본 기능으로 정리** — libmpv가 없는 첫 실행에서도 로컬 영상과 YouTube 주소 열기 명령을 사용할 수 있습니다. 호환 런타임이 없으면 첫 영상 열기에서 지원 플랫폼용 검증 런타임을 설치한 뒤 원래 열기 요청을 계속합니다.
- **검증된 libmpv 내부 설치** — Windows x64는 `zhongfly/mpv-winbuild`의 고정 LGPL 전용 빌드를 사용하고, macOS arm64/Linux x64는 `Shusek/KMediaMpv v0.2.9`의 검증 런타임을 사용합니다. 다운로드 URL·파일 크기·SHA-256을 고정하고 검증합니다.
- **macOS/Linux KMediaMpv 파일명 수정** — 실제 런타임의 `libkmediampv_mpv.dylib` / `libkmediampv_mpv.so`를 설치 대상으로 사용하도록 수정하고 회귀 테스트를 추가했습니다.
- **yt-dlp 직접 번들 제거** — YouTube URL 프리뷰는 계속 기본 기능으로 제공하지만 yttStudio 릴리스 파일 안에는 yt-dlp를 넣지 않습니다. 기존 설치본이 없으면 첫 YouTube 주소 열기에서 공식 `yt-dlp/yt-dlp 2026.08.19` 자산을 SHA-256 검증 후 사용자 영역에 설치합니다.
- **배포 라이선스 게이트 강화** — 최종 ZIP/installer/DMG/AppImage에는 yt-dlp와 libmpv 런타임이 직접 포함되지 않도록 검사하고, yttStudio/YTSubConverter/번들 폰트의 필수 라이선스 고지가 누락되면 릴리스 빌드를 실패시킵니다.
- **문서 전면 동기화** — 한국어·영어·일본어 README와 의존성·QA·컴플라이언스 문서를 v0.2.3의 실제 설치·재생·배포 정책에 맞췄습니다.

기존 YouTube 주소 스트리밍 프리뷰, 스페이스바 재생/일시정지, 자막 색 선택기, 뷰포트 모드와 편집 기능은 그대로 유지됩니다.

## 설치

.NET 런타임은 각 배포물에 포함됩니다. 영상 재생을 위해 사용자가 libmpv나 yt-dlp를 미리 설치할 필요는 없습니다.

| 파일 | 대상 | 설치 방법 |
|---|---|---|
| `yttStudio-v0.2.3-win-x64-setup.exe` | Windows x64 | 실행하면 시작 메뉴 등록과 파일 연결까지 처리합니다 |
| `yttStudio-v0.2.3-win-x64.zip` | Windows x64 (포터블) | 압축을 풀고 `YttStudio.App.exe` 실행 |
| `yttStudio-v0.2.3-osx-arm64.dmg` | macOS Apple Silicon | 열어서 `yttStudio.app`을 `Applications`로 이동 |
| `yttStudio-v0.2.3-osx-arm64.tar.gz` | macOS Apple Silicon | 앱 번들 압축본 |
| `yttStudio-v0.2.3-linux-x64.AppImage` | Linux x64 | 실행 권한을 준 뒤 실행 |
| `yttStudio-v0.2.3-linux-x64.tar.gz` | Linux x64 | 압축을 풀어 실행 |

## 영상 런타임

기존에 사용자가 지정한 호환 libmpv/yt-dlp가 있으면 그것을 우선 사용합니다. 없을 때만 프로그램 내부 설치가 동작합니다.

- Windows x64 libmpv: `zhongfly/mpv-winbuild` `mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z`
- macOS arm64 / Linux x64 libmpv: `Shusek/KMediaMpv v0.2.9` `kmedia-mpv-0.2.9-runtime-desktop.jar`의 해당 플랫폼 네이티브 트리
- yt-dlp: 공식 `yt-dlp/yt-dlp 2026.08.19` 플랫폼별 standalone 자산

자동 설치되는 제3자 런타임은 yttStudio의 MIT 라이선스로 재라이선스되지 않으며 각각의 upstream 라이선스를 따릅니다. 정확한 pin·해시·provenance와 라이선스 경계는 `docs/DEPENDENCIES.md`, `docs/THIRD-PARTY-NOTICES.md`, `docs/LEGAL-COMPLIANCE-AUDIT.md`를 참고하세요.

libmpv는 **도구 → 설정 → 영상**에서 재설치하거나 직접 경로를 지정할 수도 있습니다.

## 서명

코드 서명과 notarization은 아직 없습니다. 첫 실행에 운영체제 경고가 뜰 수 있습니다.

- Windows: SmartScreen에서 **추가 정보 → 실행**
- macOS: `yttStudio.app` 우클릭 → **열기**
