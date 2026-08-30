# DEPENDENCIES.md

> **문서 기준:** v0.2.3 (2026-08-30)

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

## libmpv 및 외부 재생 도구 (v0.2.3 현재 정책)

영상 재생은 yttStudio의 기본 기능이다. 다만 yttStudio 자체 MIT 배포물에 제3자 네이티브/실행 바이너리를 직접 합쳐 넣지 않고, **기존 설치본 우선 → 없으면 프로그램 내부에서 고정·검증 설치** 순서를 사용한다.

### libmpv

지원 대상과 내부 설치 pin:

| OS / RID | upstream 자산 | SHA-256 | 설치 방식 |
|---|---|---|---|
| Windows x64 | `zhongfly/mpv-winbuild` `mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z` | `78260166265fbc09b3bee75ee3464eb0f6bbaa8ecd172786e33c22bbf8a3cb47` | LGPL 전용 개발 빌드를 사용자 LocalApplicationData에 설치 |
| macOS arm64 | `Shusek/KMediaMpv` `kmedia-mpv-0.2.9-runtime-desktop.jar`의 `macos-aarch64` 네이티브 트리 | `4250b47144de085c7963f4bdbe99e995b9b2b0374e32a14ebe9d27fd38a67bef` | 검증 런타임의 해당 플랫폼 네이티브 트리만 사용자 영역에 설치 |
| Linux x64 | 같은 KMediaMpv 자산의 `linux-x86_64` 네이티브 트리 | 같은 SHA-256 | 검증 런타임의 해당 플랫폼 네이티브 트리만 사용자 영역에 설치 |

`MpvAutoInstaller`는 코드에 고정된 HTTPS URL, 파일 길이, SHA-256을 모두 확인한다. 최종 리디렉션 호스트는 `github.com`, `objects.githubusercontent.com`, `release-assets.githubusercontent.com`만 허용한다. 압축 항목 수와 총 해제 크기를 제한하고 절대 경로/상위 디렉터리 탈출을 차단한 뒤, staging 디렉터리를 원자적으로 교체한다. 설치 디렉터리에는 upstream과 corresponding-source 위치를 적은 `YTTSTUDIO-RUNTIME-SOURCE.txt`를 남긴다.

기존 `YTTSTUDIO_MPV_PATH` 또는 사용자가 설정에서 지정한 경로는 계속 우선한다. 호환 libmpv가 없다면 첫 로컬 영상/YouTube 영상 열기에서 `AutoInstallingVideoSource`가 설치를 수행한 뒤 같은 요청을 원래 `MpvVideoSource`에 이어서 전달한다. 설정 창에서도 같은 검증 설치기를 이용해 재설치할 수 있다.

이 정책은 과거 Shinchiro 기본 GPL 계열 빌드를 자동 설치하던 기록과, libmpv를 전혀 자동 설치하지 않는다고 적었던 과도기 문서를 대체한다. Shinchiro 빌드는 현재 자동 설치 대상이 아니다.

### yt-dlp

YouTube URL을 열 때는 기존 `YTTSTUDIO_YTDLP_PATH`, 앱 경로, `PATH`의 실행 파일을 먼저 사용한다. 없으면 `YtDlpAutoInstaller`가 코드에 고정한 공식 `yt-dlp/yt-dlp` 릴리스 자산을 직접 받아 파일 길이와 SHA-256을 확인한 뒤 사용자 LocalApplicationData에 설치한다. yttStudio의 ZIP/installer/DMG/AppImage에는 yt-dlp standalone 실행 파일을 직접 넣지 않는다.

현재 pin은 `2026.08.19`이며 Windows x64 `yt-dlp.exe`, macOS arm64 `yt-dlp_macos`, Linux x64 `yt-dlp_linux`를 사용한다. 새 버전으로 올릴 때는 자산 이름·길이·해시를 함께 갱신하고 CI와 수동 QA를 다시 수행한다.

`yt-dlp`는 영상을 파일로 저장하기 위한 다운로드 명령으로 호출하지 않는다. URL을 검증하고 libmpv `ytdl_hook`이 스트림을 열 수 있도록 실행 파일 경로를 전달한다. 공개 VOD가 기본 지원 범위이며, 생방송·연령 제한·비공개·지역 차단은 별도 실패 사유로 처리될 수 있다.

### 라이선스 경계

- yttStudio 본체는 MIT를 유지한다.
- 자동 설치되는 libmpv/yt-dlp는 각각 upstream 라이선스를 그대로 따른다. yttStudio가 MIT로 재라이선스하지 않는다.
- yttStudio 릴리스 산출물 자체에는 이 네이티브/standalone 바이너리를 직접 포함하지 않는다.
- libmpv는 교체 가능한 동적 라이브러리로 로드한다. 정확한 upstream, hash, corresponding-source 위치는 설치 provenance와 이 문서에 기록한다.
- 제3자 라이선스 판단은 이름만 보고 추정하지 않고 **실제로 고정한 산출물** 기준으로 다시 확인한다.

## 외부 도구

| 도구 | 용도 | 정책 |
|---|---|---|
| mitmproxy | 실제 플레이어 프리뷰 | 번들 금지. 미설치 시 안내만. 스크립트는 upstream과 동기화 |
| Fiddler Classic | 위의 Windows 대안 | 안내만 |
| Aegisub | `.ass` 편집 | 안내만 |
| yt-dlp | YouTube URL 스트림 해석 | 기존 설치본 우선, 없으면 공식 pin을 프로그램 내부에서 검증 설치. yttStudio 릴리스에는 직접 번들하지 않음 |

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

## v0.2.3 릴리스 license gate

정식 yttStudio 릴리스 ZIP/installer/DMG/AppImage에는 `yt-dlp` standalone 실행 파일과 `libmpv`/KMediaMpv 네이티브 런타임을 직접 포함하지 않는다. 릴리스 워크플로는 해당 파일들이 publish 디렉터리에 존재하면 실패한다.

반드시 포함하는 것은 yttStudio의 `LICENSE`, `THIRD-PARTY-NOTICES.txt`, YTSubConverter MIT 고지, Roboto Apache-2.0 및 Liberation OFL 고지다. 자동 설치되는 libmpv/yt-dlp는 yttStudio 패키지 밖의 사용자 영역에 설치되며 각각 upstream 라이선스를 그대로 따른다.

릴리스 직전에는 코드에 pin한 libmpv/yt-dlp 자산을 실제로 다시 내려받아 SHA-256, 파일 크기, 필요한 libmpv 아카이브 경로와 API 심볼을 확인한다. pin을 바꾸려면 코드 상수, 이 문서, 제3자 고지, QA 기록을 함께 갱신해야 한다.

KMediaMpv v0.2.9에서 yttStudio가 사용하는 실제 라이브러리 파일명은 macOS arm64의 `libkmediampv_mpv.dylib`, Linux x64의 `libkmediampv_mpv.so`다. KMediaMpv 런타임은 corresponding-source URL을 provenance에 기록하고, Windows LGPL 빌드는 정확한 upstream 릴리스와 빌드 저장소를 provenance에 기록한다. 사용자 지정 libmpv는 해당 배포자가 제공한 라이선스와 build configuration을 별도로 따른다.
