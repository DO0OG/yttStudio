# yttStudio v0.3.0

## 이번 변경

- **다중 키프레임 위치 이동** — 위치 이동이 시작점과 끝점을 잇는 두 점 보간에서 여러 키프레임을 잇는 경로로 넓어졌습니다. 프리뷰 화면에 경로와 키프레임 마커가 그려지고 마커를 끌어 위치를 옮길 수 있으며, 타임라인에서 선택한 큐의 키프레임 시각을 확인합니다. 편집은 실행취소와 다시실행이 됩니다. 키프레임은 구간별 move 세그먼트로 기록해 기존 변환 경로와 호환하며, 프로젝트 스키마 3판 마이그레이션으로 이전 프로젝트 파일과 move 태그가 하나인 기존 자막을 그대로 엽니다.
- **릴리스 자동 업데이트** — 새 버전이 올라오면 알리고, 업데이트를 고르면 현재 운영체제에 맞는 자산을 내려받아 설치까지 진행합니다. Windows 설치본과 포터블, macOS 디스크 이미지, Linux AppImage와 tarball을 각각 처리하며 설치할 수 없는 상황에서는 받은 파일의 위치를 여는 방식으로 되돌아갑니다. 진행률을 보여주고 도중에 취소할 수 있으며, 시작할 때 확인할지 여부는 **도구 → 설정**에서 끌 수 있고 특정 버전을 건너뛸 수도 있습니다.
- **여러 줄 자막 편집** — 큐 목록이 자막 줄 수에 맞춰 행 높이를 늘려 여러 줄로 작성된 자막을 그대로 보여주고 편집합니다. 한 번에 보여줄 최대 줄 수는 설정에서 1~10줄 범위로 정하며 기본값은 5줄입니다. 큐가 수천 개인 문서에서도 목록 가상화를 유지합니다.
- **내부 구조 정리** — 자막 파일 서비스와 주 창 뷰모델과 업데이트 서비스를 책임 단위로 나눠 파일 크기 상한을 지킵니다. 공개 동작은 바뀌지 않았습니다.

## 설치

.NET 런타임은 각 배포물에 포함됩니다. 지원 플랫폼에서는 영상 재생을 위해 사용자가 libmpv, yt-dlp 또는 Deno를 미리 설치할 필요가 없습니다.

| 파일 | 대상 | 설치 방법 |
|---|---|---|
| `yttStudio-v0.3.0-win-x64-setup.exe` | Windows x64 | 실행하면 시작 메뉴 등록과 파일 연결까지 처리합니다 |
| `yttStudio-v0.3.0-win-x64.zip` | Windows x64 (포터블) | 압축을 풀고 `YttStudio.App.exe` 실행 |
| `yttStudio-v0.3.0-osx-arm64.dmg` | macOS Apple Silicon | 열어서 `yttStudio.app`을 `Applications`로 이동 |
| `yttStudio-v0.3.0-osx-arm64.tar.gz` | macOS Apple Silicon | 앱 번들 압축본 |
| `yttStudio-v0.3.0-linux-x86_64.AppImage` | Linux x64 | 실행 권한을 준 뒤 실행 |
| `yttStudio-v0.3.0-linux-x64.tar.gz` | Linux x64 | 압축을 풀어 실행 |

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
