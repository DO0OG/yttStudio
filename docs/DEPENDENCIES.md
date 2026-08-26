# DEPENDENCIES.md

의존성 고정(pin) 기록. **`master` 추종 금지** — 비공개 포맷의 버그 우회를 외부 프로젝트에 의존하므로, pin이 없으면 포맷 규칙의 의미가 시간에 따라 조용히 바뀐다.

---

## YTSubConverter (핵심 의존성)

```
upstream:         https://github.com/arcusmaximus/YTSubConverter   (MIT)
fork (사용 중):   https://github.com/DO0OG/YTSubConverter
integration:      git submodule → external/YTSubConverter
branch:           yttstudio/independent-justification
project used:     YTSubConverter.Shared (netstandard2.0)
commit:           c460cca (2747267 + ReadLine 전달 수정)
upstream base:    b186a40bc21e58a8c9651cf616cbb5e80425dfc6  (2026-07-27, PR #144 from s0hv)
verified date:    2026-08-25
verified by:      포맷 규칙 검증 (docs/YTT-VERIFICATION.md) + M1 빌드·테스트
local patches:    1건 — 아래 참조
```

### 로컬 패치 1: `ap`와 독립적인 `ju`

**왜 필요한가:** YTT 포맷에서 `<wp ap>`와 `<ws ju>`는 독립이지만,
pin된 upstream은 `WriteWindowStyle()`이 `ju`를 `AnchorPoint`에서 파생시키고
`ReadWindowStyle()`은 `ju`를 아예 읽지 않는다. 그래서 `ap=7` + `ju=0` 같은 조합을
표현할 수도 왕복시킬 수도 없었다. 근거는 `docs/YTT-VERIFICATION.md` V-13.

편집기는 `ju`를 독립 컨트롤로 노출해야 하는데,
"사용자가 설정할 수 있는데 결과물에 반영이 안 되는 게 최악"이라는 원칙상
제한을 그냥 받아들일 수 없었다. head writer 복제가 금지되어 있으므로
우회 대신 upstream을 고쳤다.

**변경 내용** (`YTSubConverter.Shared`, 23 insertions / 3 deletions, 커밋 2개):

| 파일 | 변경 |
|---|---|
| `Line.cs` | nullable `int? Justification` 추가. `null`이면 기존 파생 동작 유지 |
| `Line.cs` | `Assign()`에서 보존 — 복사 생성자와 `Clone()`이 값을 유지 |
| `YttDocument.cs` | `ReadWindowStyle()`이 `ju` 속성을 읽음 |
| `YttDocument.cs` | `WriteWindowStyle()`이 `GetEffectiveJustificationId()` 사용 |
| `YttDocument.cs` | `LineAlignmentComparer`가 실효 `ju`를 동등성에 포함 — 없으면 `ap`가 같고 `ju`가 다른 줄이 하나의 `<ws>`로 합쳐짐 |
| `YttDocument.cs` | `ReadLine()`이 windowStyle의 `Justification`을 자막 Line으로 전달 — 없으면 import에서 값이 유실됨 |

**하위 호환:** `Justification`이 `null`이면 패치 전과 바이트 단위로 동일한 출력.
기존 호출자는 영향받지 않는다.

**upstream PR 제출 여부:** 미제출. 제출해서 병합되면 이 패치를 제거하고
upstream pin으로 되돌린다.

**pin 갱신 시 주의:** upstream을 올릴 때 이 패치를 새 base에 rebase해야 한다.
`ReadWindowStyle` / `WriteWindowStyle` / `LineAlignmentComparer`가 upstream에서
바뀌었으면 충돌 가능성이 높다.

### 검증된 픽스처 / 코드 경로

| 대상 | 확인 내용 |
|---|---|
| `YttDocument.cs:670-694` `WriteHead()` | head 순서 wp → ws → pen, 각 풀 id=0 더미 |
| `YttDocument.cs:92-113` `Save()` | 전처리 파이프라인 실제 구성, LF 강제 |
| `YttDocument.cs:878-898` | 좌표 변환 `(2 + coord × 0.96)` |
| `YttDocument.cs:900-914` | 폰트 스케일 `real = 1 + (ytt-1)/4`, 상한 clamp 없음 |
| `YTSubConverter.Tests/Ass/Files/*.ytt` | golden 비교용 픽스처, head 타입 순서 확인 |
| `mitmproxy_script.py` | `ensure_subtitle_selector` + `apply_custom_subtitles` |
| `ytt.ytt` | 포맷 설명 문서 — **writer 출력이 아님. 규범 근거로 단독 사용 금지** |
| `YttDocument.cs:710-720`, `:1160-1175` | 로컬 패치 지점 — `ju` 독립화. pin 갱신 시 rebase 필요 |

### 갱신 절차

upstream을 올릴 때 반드시 순서대로:

1. upstream 변경 로그 확인 (특히 `YttDocument.cs`, `mitmproxy_script.py`, `ytt.ytt`)
2. YTT golden test 전체 실행 → 출력 diff 검토
3. 실제 YouTube 업로드 수동 QA (PC / iOS / Android)
4. 포맷 규칙 변경점 검토 — 규칙이 바뀌었으면 `docs/YTT-VERIFICATION.md` 갱신
5. 이 파일의 commit / verified date 갱신
6. 위 5단계 결과를 PR 본문에 요약

> `Shared`가 사용하는 `Color`, `PointF`, `SizeF`, `Size`, `Point`는 크로스플랫폼 `System.Drawing.Primitives` 타입이다. Windows 전용 `System.Drawing.Common` API는 사용하지 않는다. `YttStudio.Core/Format/` 어댑터 경계는 플랫폼 우회가 아니라 외부 타입을 도메인 모델에서 격리하기 위해 유지하며, M0 스모크 테스트에서 경계 밖 public API 누수가 없음을 확인했다.

---

## 런타임 / 프레임워크

M0 compatibility spike와 실제 NuGet restore로 다음 버전을 **확정**했다.

| 항목 | 확정 버전 | 실제 해석 / 근거 |
|---|---|---|
| .NET SDK / 런타임 | **10.0.301 / net10.0** | Windows restore 및 Release build 통과 |
| C# | **14** | `LangVersion=14.0`, nullable 및 warnings-as-errors 적용 |
| Avalonia | **12.1.1** | `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent` 모두 12.1.1 |
| Avalonia.Skia | **12.1.1** (전이) | `Avalonia.Desktop`에서 실제 해석 |
| SkiaSharp | **3.119.4** | `Avalonia.Skia 12.1.1`의 전이 버전과 Render의 명시 버전 일치 |
| SkiaSharp.NativeAssets.Linux | **3.119.4** | Linux Render 테스트용 네이티브 자산, 관리형 SkiaSharp와 버전 일치 |
| SkiaSharp.NativeAssets.macOS | **3.119.4** | macOS Render 테스트용 네이티브 자산, 관리형 SkiaSharp와 버전 일치 |
| CommunityToolkit.Mvvm | **8.4.2** | 실제 restore 통과 |
| xUnit | **xunit.v3 4.0.0** | .NET 10 Microsoft.Testing.Platform runner로 테스트 3건 통과 |
| xUnit (App.Tests만) | **xunit.v3 3.2.2** | `Avalonia.Headless.XUnit 12.1.1`이 `xunit.v3.extensibility.core 3.2.2`에 의존해 `VersionOverride`로 맞춤. 나머지 테스트 프로젝트는 4.0.0 유지 |
| Avalonia.Headless.XUnit | **12.1.1** | 헤드리스 UI 테스트용. `PreviewCanvas` 히트 테스트 회귀 검증에 사용 |
| Microsoft.NET.Test.Sdk | **17.14.1** | 실제 restore 통과 |
| Serilog | **4.4.0** | 실제 restore 통과 |
| Serilog.Sinks.File | **7.0.0** | 실제 restore 통과 |

Verify는 M0에 추가하지 않았다. M1 raster golden test에서 도입한다.

### M0 spike 결과

| 항목 | 판정 | 근거 / 후속 조치 |
|---|---|---|
| .NET 10 + `YTSubConverter.Shared` | **PASS** | ProjectReference를 포함한 Release build 성공, 경고 0 / 오류 0 |
| `System.Drawing` 경계 | **PASS** | Core public API에서 `Core/Format/` 밖 `System.Drawing` 노출이 없음을 스모크 테스트로 확인 |
| Avalonia / SkiaSharp 정렬 | **PASS** | Avalonia.Skia 12.1.1이 SkiaSharp 3.119.4를 해석하며 Render의 명시 버전과 일치 |
| Windows libmpv 로드 | **N/A — 미설치, 확인 불가** | `NativeLibrary.TryLoad("mpv-2.dll")`와 `TryLoad("libmpv-2.dll")` 모두 false. 설치하지 않고 M2에서 재확인 |
| Linux / macOS restore | **PASS** | 3개 OS matrix CI에서 ubuntu-latest / macos-latest / windows-latest 모두 build + test 성공 (run 32809478109, 2026-08-25) |

FAIL 항목이 없으므로 .NET 8 + Avalonia 11 fallback 비용 산정은 필요하지 않다.

---

## libmpv (네이티브, M5 결정)

libmpv는 .NET self-contained 런타임에 자동으로 들어가는 관리 코드가 아니다. 따라서
배포 패키지의 앱 바이너리와 별도로 배치·서명·고지해야 한다. 아래 결정은 **배포
정책**이며, 현재 저장소에 각 OS용 libmpv 바이너리를 추가했다는 뜻이 아니다.

### 지원 아키텍처와 우선순위

| OS / RID | M5 우선순위 | 결정 | 현재 상태 |
|---|---:|---|---|
| Windows x64 (`win-x64`) | 1 | 첫 정식 배포 대상 | 관리 코드 CI만 확인, libmpv 패키징 미구현 |
| macOS arm64 (`osx-arm64`) | 2 | 두 번째 정식 배포 대상 | 관리 코드 CI만 확인, codesign/notarization 미구현 |
| Linux x64 (`linux-x64`) | 3 | AppImage 정식 배포 대상 | 관리 코드 CI만 확인, AppImage 미구현 |
| Windows arm64 | — | M5에서 지원하지 않음 | 별도 native fixture/CI 전까지 미지원 |
| macOS x64 / universal | — | M5에서 지원하지 않음 | universal 빌드 결정 및 검증 전까지 미지원 |
| Linux arm64 | — | M5에서 지원하지 않음 | 별도 native fixture/CI 전까지 미지원 |

지원하지 않는 아키텍처에서 시스템 libmpv가 우연히 발견되더라도 호환성을 보장하지
않는다. 릴리스 파일명과 설치 안내에는 위 세 RID만 노출한다.

### 제공 방식과 probing 순서

정식 패키지는 **앱 옆의 동적 sidecar 번들**을 기본으로 한다. 개발자와 고급 사용자는
환경 변수로 다른 빌드를 지정할 수 있고, 번들이 없는 개발 환경에서만 OS 표준 탐색을
마지막으로 사용한다. 최종 순서는 다음과 같다.

1. `YTTSTUDIO_MPV_PATH` — 라이브러리 파일 또는 디렉터리. 디렉터리면 해당 OS의 후보
   파일명을 순서대로 검사한다.
2. `AppContext.BaseDirectory` — 정식 패키지에 포함된 sidecar 위치.
3. OS 동적 로더의 표준 탐색 경로.

현재 `MpvNativeLibrary.EnumerateCandidates()`가 실제로 사용하는 파일명은 다음과 같다.

| OS | 후보 파일명(순서대로) |
|---|---|
| Windows | `libmpv-2.dll`, `mpv-2.dll` |
| macOS | `libmpv.2.dylib`, `libmpv.dylib` |
| Linux/기타 Unix | `libmpv.so.2`, `libmpv.so` |

현재 구현은 로드 실패 시 크래시하지 않고 `libmpv 없음 · 배경 모드`로 시작하며,
로그에는 `libmpv was not found. Probed: ...` 진단을 남긴다. **M5 사용자 메시지 계약**은
다음 문구와 troubleshooting 링크를 추가하는 것이며, 아직 UI에 반영되지 않았다.

> 영상을 재생할 수 없습니다. libmpv를 찾지 못했습니다. 앱을 다시 설치하거나
> `YTTSTUDIO_MPV_PATH`에 호환되는 libmpv 파일/폴더를 지정한 뒤 다시 시작하세요.

### 버전 및 client API 정책

현재 코드는 `mpv_create`부터 Render API 함수까지 필요한 native export를 조회하고,
초기화 후 `mpv-version` property를 우선 읽는다. property를 읽지 못하면
`mpv_client_api_version()`을 `client API major.minor` 문자열로 기록한다. **숫자형
최소 libmpv 버전 또는 client API floor를 강제하는 코드는 아직 없다.**

M5 배포 정책은 다음처럼 고정한다.

- 각 릴리스는 실제로 시험한 libmpv **빌드 식별자, `mpv-version`, client API 값, RID**를
  릴리스 노트와 notice payload에 기록한다.
- 현재처럼 필요한 export 누락 또는 초기화 실패는 호환되지 않는 라이브러리로 보고
  로드를 거부한다. `mpv-version`을 읽지 못했다는 이유만으로 호환이라고 판정하지
  않는다.
- 수치형 최소 버전은 M2 native matrix를 측정하기 전까지 **미정(TBD)** 으로 둔다.
  임의의 버전을 최소값이라고 문서화하거나, 현재 코드가 거부한다고 표현하지 않는다.

따라서 현 상태의 `LibraryVersion`은 진단용 값이지, 배포 호환성 보증값이 아니다.

### M2 렌더 결정과 OS별 fallback

- **libmpv 렌더 백엔드:** `MPV_RENDER_API_TYPE_SW`; `hwdec=no`. 근거는
  `docs/PERFORMANCE.md`이며, CPU readback이 필요한 airspace 경로에서 GPU
  readback보다 측정 비용이 낮았다.
- **Windows:** libmpv는 SW 경로를 사용하므로 libmpv 자체의 GPU/OpenGL 드라이버를
  요구하지 않는다. Avalonia/Skia의 GL 초기화는 호스트 드라이버에 의존한다. 현재
  앱에는 GL 초기화 실패를 감지해 별도 software UI backend로 전환하는 선택지가
  없으므로, “Windows GL/SW 자동 fallback”은 **정책만 확정된 미구현 항목**이다.
- **Linux AppImage (`linux-x64`):** AppImage에 vendor GPU 드라이버를 넣지 않는다.
  libmpv는 SW로 동작하지만 Avalonia 창은 호스트의 OpenGL/EGL 및 세션 런타임에
  의존할 수 있다. Wayland 세션은 Wayland client 런타임, X11 세션은 X11/XCB 런타임을
  확인해야 하며, 두 세션 모두에서 실행 검증한다. 현재 AppImage launcher의 GL 실패
  fallback과 의존성 사전 점검은 미구현이다.
- **macOS:** M5 정식 대상은 arm64이며, dylib와 앱을 함께 서명해야 한다. macOS x64
  또는 universal은 이 릴리스 범위가 아니다.

### self-contained publish와 sidecar

`dotnet publish --self-contained`는 .NET 런타임과 관리 어셈블리를 포함할 뿐,
임의의 libmpv를 자동 포함하지 않는다. 정식 패키지 빌더는 RID별로 다음을 하나의
배포 단위로 취급해야 한다.

```
yttStudio 실행 파일/앱 번들
libmpv sidecar (RID별 실제 시험 빌드)
THIRD-PARTY-NOTICES.txt 및 licenses/libmpv/*
```

현재 프로젝트에는 libmpv native asset 또는 패키징 대상이 없으므로 self-contained
아티팩트만으로 영상 재생이 된다고 안내하지 않는다.

### macOS codesign / notarization 정책

정식 `osx-arm64` 아티팩트는 sidecar dylib를 먼저 서명하고 앱 번들을 서명한 뒤
notarization한다. 배포 전 최소 게이트는 다음과 같다.

1. hardened runtime과 필요한 최소 entitlement로 `libmpv.2.dylib` 및 앱을 서명한다.
2. `codesign --verify --deep --strict`로 서명·봉입 경로를 검사한다.
3. notarization 제출 후 `xcrun notarytool` 결과를 확인하고, 최종 DMG/ZIP에 stapling한다.
4. 깨끗한 arm64 macOS에서 Gatekeeper 실행을 확인한다.

패키징 스크립트와 실제 인증서/팀 ID는 저장소에 없으며, 이 문서는 서명을 수행했다고
주장하지 않는다.

### crash metadata

현재 `Program`은 `%LOCALAPPDATA%/YttStudio/logs/yttstudio-YYYYMMDD.log`에
managed fatal을 기록하고, libmpv 초기화 시 버전과 로드 경로를 남긴다. 그러나 native
crash 전용 수집기는 없다. M5 crash report의 필수 metadata는 다음으로 고정한다.

- 앱 버전/commit 또는 package RID, OS 버전, process architecture
- libmpv 로드 경로, `mpv-version`, client API 값, probing diagnostic
- 렌더 경로(`MPV_RENDER_API_TYPE_SW`), Avalonia/Skia backend, GPU/OpenGL/Wayland/X11
  초기화 상태
- UTC 시각과 managed exception/stack trace(있는 경우)

영상 파일의 전체 경로와 자막 본문은 기본 crash payload에 넣지 않는다. 현재 로그에는
위 항목 중 libmpv 버전·경로와 managed fatal만 있으므로, 나머지 metadata 및 native
crash 수집은 **미구현**이다.

---

## 네이티브 배포 전략 (M5 확정)

### 지원 아키텍처

| OS | 아키텍처 | 우선순위 | 상태 |
|---|---|---|---|
| Windows | x64 | 1순위 | CI 빌드·테스트 통과 |
| macOS | arm64 | 2순위 | CI 빌드·테스트 통과 |
| Linux | x64 | 3순위 | CI 빌드·테스트 통과 |
| Windows / Linux arm64, macOS x64 | — | 미지원 | 요청 시 재검토 |

> CI는 빌드·테스트만 검증한다. **실제 배포 패키지 생성과 플랫폼 검증은 미수행이다.**

### libmpv 제공 방식: 시스템 탐색 (번들하지 않음)

**결정: 번들하지 않고 탐색만 한다.**

근거:
- libmpv는 **LGPLv2.1+ (빌드 구성에 따라 GPL)** 이다. 번들하면 배포 패키지 전체의
  라이선스 의무가 커지고, 정적 링크 여부에 따라 소스 공개 의무가 생길 수 있다
- 영상 재생은 **선택 기능**이다. libmpv가 없어도 자막 편집·검증·export가 전부 동작한다
- 사용자가 이미 mpv를 설치한 경우가 많고, 배포본이 그것과 충돌할 이유가 없다

### probing 순서

`MpvNativeLibrary.TryLoad`가 다음 순서로 시도한다:

1. 환경변수 `YTTSTUDIO_MPV_PATH` — 파일 경로 또는 디렉터리
2. 앱 실행 디렉터리
3. OS 표준 라이브러리 탐색 (`NativeLibrary.TryLoad`)

플랫폼별 파일명: Windows `libmpv-2.dll` / `mpv-2.dll`, Linux `libmpv.so.2`, macOS `libmpv.2.dylib`.

**실패 시**: 크래시하지 않고 영상 기능만 비활성화한 뒤 단색·체커보드 배경으로 폴백한다.
어떤 경로를 시도했는지 진단 메시지에 남긴다.

### 버전 게이트

- 최소 `mpv_client_api_version()` = **2.0** (mpv 0.35+). `MpvCompatibility.MinimumClientApiVersion`
- 이 프로젝트가 쓰는 render API 진입점이 안정화된 최초 릴리스가 기준이다
- 미달 시 render context를 만들기 전에 거부하고, **발견한 버전 · 요구 버전 · 로드 경로**를
  메시지에 담는다. 파이프라인 깊은 곳에서 크래시하는 것보다 낫다

### crash log

`MpvCompatibility.DescribeForCrashLog`가 한 줄로 기록한다:

```
libmpv client-api=2.0 path=... os=... arch=...
```

네이티브 크래시의 대부분이 버전·드라이버 문제이므로 이 줄이 없으면 원인 추적이 불가능하다.

### 라이선스 파일

배포 패키지에 포함한다:

| 대상 | 라이선스 |
|---|---|
| yttStudio | 저장소 `LICENSE` |
| YTSubConverter (fork) | MIT |
| Roboto | Apache-2.0 (`src/YttStudio.Render/Assets/Fonts/LICENSE-Roboto.txt`) |
| Liberation Sans/Serif/Mono | SIL OFL 1.1 (`LICENSE-Liberation.txt`) |
| libmpv | **번들하지 않으므로 미포함.** 시스템 설치본을 쓴다 |

### 미수행 — 실제 패키징

아래는 **문서로 전략만 확정**했고 구현·검증하지 않았다. 이 환경에서 불가능하다.

- self-contained publish 산출물 생성 및 검증
- macOS codesign / notarization
- Linux AppImage (GPU / OpenGL / Wayland / X11 의존성)
- Windows GPU 드라이버 / OpenGL fallback 실측
- 각 플랫폼 설치 관리자

---

## 외부 도구 (번들하지 않음)

| 도구 | 용도 | 정책 |
|---|---|---|
| mitmproxy | 실제 플레이어 프리뷰 | 번들 금지. 미설치 시 다운로드 안내만. 스크립트는 upstream `mitmproxy_script.py`와 동기화 |
| Fiddler Classic | 위의 Windows 대안 | 안내만 |
| Aegisub | `.ass` 편집 | 안내만 |
| yt-dlp | 기존 `.ytt` 수집 (`--write-subs --sub-format=srv3`) | 안내만 |

---

## 번들 폰트

미리보기 정확도를 위해 앱에 포함하는 폰트. 재배포 가능한 라이선스만.

| 폰트 | 라이선스 | 용도 |
|---|---|---|
| Roboto | Apache-2.0 | `fs=0/4` — 유튜브 기본. **모든 OS에서 번들 사용** |
| Liberation Sans | SIL OFL 1.1 | `fs=7` Arial metric-compatible 대체 |
| Liberation Serif | SIL OFL 1.1 | `fs=2` Times New Roman metric-compatible 대체 |
| Liberation Mono | SIL OFL 1.1 | `fs=1` Courier New metric-compatible 대체 |

**대체 불가 (번들 금지 — 재배포 불가):** Lucida Console(`fs=3`), Comic Sans MS(`fs=5`), Monotype Corsiva(`fs=6`).
로컬에 없으면 폴백하되 미리보기에 "근사 표시 중" 배지를 띄운다. 조용한 폴백 금지.

라이선스 파일은 배포 패키지에 포함한다.

---

## M5 배포 전 license gate

정식 패키지는 `docs/THIRD-PARTY-NOTICES.md`의 목록과 함께 **실제 번들에 사용한
libmpv 빌드의 원문 라이선스·저작권·소스 제공 정보**를 포함해야 한다. 현재 저장소에는
libmpv 바이너리나 특정 빌드의 license payload가 없으므로, 아래를 완료했다고
간주하지 않는다.

- libmpv가 LGPL-2.1-or-later 구성인지, GPL 구성 요소를 포함한 빌드인지 판정
- 해당 빌드의 LGPL/GPL 전문, 저작권 고지, 빌드/수정 고지, 소스 또는 서면 제공 경로를
  `licenses/libmpv/`에 복사
- 동적 sidecar 교체가 가능한 패키지 구조와 그 방법을 사용자에게 고지
- GPL 구성으로 배포할 경우 전체 배포물의 GPL 의무를 별도 법률 검토
- YTSubConverter(MIT), 번들 폰트(Apache-2.0/SIL OFL 1.1), NuGet 및 기타 런타임
  의존성의 고지를 같은 notice 파일에서 누락 없이 생성

라이선스 분류는 빌드 플래그만으로 추정하지 않고, **배포하는 정확한 libmpv 산출물의
upstream license 파일과 구성 정보**를 기준으로 한다. 자세한 payload 위치와 현재
미결정 상태는 [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md)에 기록한다.
