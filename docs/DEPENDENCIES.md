# DEPENDENCIES.md

의존성 고정(pin) 기록. **`master` 추종 금지** — 비공개 포맷의 버그 우회를 외부 프로젝트에 의존하므로, pin이 없으면 SPEC §5의 의미가 시간에 따라 조용히 바뀐다.

---

## YTSubConverter (핵심 의존성)

```
repository:       https://github.com/arcusmaximus/YTSubConverter
license:          MIT
integration:      git submodule → external/YTSubConverter
project used:     YTSubConverter.Shared (netstandard2.0)
commit:           b186a40bc21e58a8c9651cf616cbb5e80425dfc6
commit date:      2026-07-27
commit subject:   Merge pull request #144 from s0hv/patch-1
verified date:    2026-08-24
verified by:      SPEC v1.1 검증 (docs/YTT-VERIFICATION.md)
local patches:    없음
```

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

### 갱신 절차

upstream을 올릴 때 반드시 순서대로:

1. upstream 변경 로그 확인 (특히 `YttDocument.cs`, `mitmproxy_script.py`, `ytt.ytt`)
2. YTT golden test 전체 실행 → 출력 diff 검토
3. 실제 YouTube 업로드 수동 QA (PC / iOS / Android)
4. SPEC §5 변경점 검토 — 규칙이 바뀌었으면 `docs/YTT-VERIFICATION.md` 갱신
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
| CommunityToolkit.Mvvm | **8.4.2** | 실제 restore 통과 |
| xUnit | **xunit.v3 4.0.0** | .NET 10 Microsoft.Testing.Platform runner로 테스트 3건 통과 |
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

## libmpv (네이티브)

```
provisioning:     M5에서 확정 (번들 / 시스템 탐색 / 둘 다)
minimum version:  M2 spike에서 결정
license:          LGPLv2.1+ (빌드 구성에 따라 GPL) — 배포 시 라이선스 파일 포함 필수
```

**M0에서 확인할 것:** 3개 OS에서 로드가 되는가만. 버전 정책·probing·codesign은 M5.

**crash log에 libmpv 버전을 반드시 기록한다** — 네이티브 크래시의 대부분이 버전/드라이버 문제다.

지원 아키텍처는 M5에서 확정하되, 후보:

| OS | 아키텍처 | 우선순위 |
|---|---|---|
| Windows | x64 | 1순위 |
| Windows | arm64 | 미정 |
| macOS | arm64 | 2순위 |
| macOS | x64 | 미정 (universal 여부 포함) |
| Linux | x64 | 3순위 |
| Linux | arm64 | 미정 |

---

## 외부 도구 (번들하지 않음)

| 도구 | 용도 | 정책 |
|---|---|---|
| mitmproxy | 실제 플레이어 프리뷰 (§14) | 번들 금지. 미설치 시 다운로드 안내만. 스크립트는 upstream `mitmproxy_script.py`와 동기화 |
| Fiddler Classic | 위의 Windows 대안 | 안내만 |
| Aegisub | `.ass` 편집 | 안내만 |
| yt-dlp | 기존 `.ytt` 수집 (`--write-subs --sub-format=srv3`) | 안내만 |

---

## 번들 폰트 (§7.5.1)

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
