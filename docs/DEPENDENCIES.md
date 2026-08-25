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

> `Shared`는 `netstandard2.0`이므로 `System.Drawing` 계열 타입(`Color`, `PointF`, `Size`)을 사용한다. .NET 10에서 `System.Drawing.Common`은 Windows 전용이므로, `YttStudio.Core/Format/` 어댑터가 이 타입들을 자체 타입으로 감싸 경계 밖으로 새어나가지 않게 한다. M0 spike에서 3개 OS 빌드로 확인할 것.

---

## 런타임 / 프레임워크

M0 compatibility spike에서 최종 확정. 아래는 **기본 후보**이며, spike 실패 시 이 파일에 사유와 대안을 기록한다.

| 항목 | 후보 버전 | 근거 / 주의 |
|---|---|---|
| .NET | **10 (LTS)** | .NET 8/9 모두 2026-11-10 EOL. 10은 2028-11까지 |
| C# | **14** | .NET 10 기본 |
| Avalonia | **12.1.x** | 12.0.0 stable 2026-04-07, 12.1.1 2026-07-29. v11 → v12 breaking changes 문서 확인 필요 |
| SkiaSharp | Avalonia 12가 참조하는 버전에 정렬 | 버전 불일치 시 네이티브 심볼 충돌 |
| CommunityToolkit.Mvvm | 최신 stable | |
| xUnit / Verify | 최신 stable | raster golden test는 SkiaSharp 버전 고정 필요 |
| Serilog | 최신 stable | |

### M0 spike 체크리스트

- [ ] .NET 10 솔루션 생성, `YTSubConverter.Shared` 프로젝트 참조로 clean build
- [ ] Windows / Linux / macOS restore 성공
- [ ] `System.Drawing` 의존 범위 확인 — 어댑터 밖으로 새는지
- [ ] Avalonia 12 + SkiaSharp 통합, 버전 정렬 확인
- [ ] libmpv 로드 가능 여부 (3개 OS, 기본 self-contained publish)
- [ ] 실패 항목이 있으면 .NET 8 + Avalonia 11 fallback 비용 산정 후 이 파일에 기록

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
