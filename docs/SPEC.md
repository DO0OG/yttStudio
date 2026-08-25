# YttStudio — 개발 명세서 (v1.1)

> **YouTube YTT(SRV3) 자막 전용 WYSIWYG 에디터**
> Aegisub의 텍스트 중심 워크플로 대신, 영상 위에서 직접 드래그·정렬·스타일링하고 `.ytt`를 바로 뽑는 데스크톱 앱.

**v1.1 변경 요약:** 2차 리뷰 24개 항목을 pin된 upstream 소스에 직접 대조해 검증했다. 결과는 `docs/SPEC-v1.1-CHANGELOG.md`, 근거는 `docs/YTT-VERIFICATION.md`. 리뷰가 P0로 지목한 head 순서·id=0 더미 2건은 **v1.0이 옳았고 리뷰가 틀렸음**을 소스로 확인해 유지했으며, 나머지 11건은 반영했다.

---

## 0. 이 문서를 읽는 AI 에이전트에게

- 이 문서는 **단일 진실 공급원(SSOT)** 이다. 구현 중 판단이 필요하면 이 문서를 먼저 따르고, 문서에 없으면 §17 규칙을 따른다.
- 작업 순서는 **§16 마일스톤**을 따른다. 각 마일스톤의 "완료 조건"을 만족해야 다음으로 넘어간다. 시작 시 `docs/PROGRESS.md`에 체크리스트를 만든다.
- **불확실하면 멈추고 물어라.**

### 0.1 §5(YTT 규범)를 다루는 방법

> **§5는 임의로 수정하지 않는다. 다만 pin된 YTSubConverter upstream 소스, 공식 기술 문서, 재현 가능한 YouTube 실험이 §5와 충돌하면 — 구현을 멈추고 `docs/YTT-VERIFICATION.md`에 근거를 기록한 뒤 §5 정정을 제안한다.**

"추측으로 고치지 마라"는 안전장치는 유지하되, 명세 오류를 영구 고정하지 않기 위한 절차다. 실제로 v1.0 → v1.1 과정에서 §5.9(전처리 파이프라인)의 구조 오류가 이 절차로 잡혔다.

### 0.2 근거 등급 표기

§5의 각 규칙에는 근거 등급이 붙는다. 등급이 없는 서술은 `[PRODUCT]`로 간주한다.

| 등급 | 의미 | 신뢰도 |
|---|---|---|
| `[UPSTREAM]` | pin된 YTSubConverter 소스·픽스처에서 직접 확인 | 높음 |
| `[EMPIRICAL]` | 실제 YouTube 업로드/플레이어에서 재현 확인 | 가장 높음 |
| `[API]` | Microsoft / mpv / Avalonia 공식 문서 | 높음 |
| `[HEURISTIC]` | 안전측 추정. 완전한 규칙 아님 | 낮음 — 근거 발견 시 승격 |
| `[PRODUCT]` | YttStudio 설계 선택. 포맷 제약 아님 | 변경 가능 |

`[UPSTREAM]`만 붙은 규칙은 "upstream이 그렇게 한다"는 뜻이지 "YouTube가 반드시 그렇다"는 증명이 아니다. `[EMPIRICAL]` 미확인 목록은 `docs/YTT-VERIFICATION.md` 말미에 있다.

### 0.3 절대 단독 근거로 쓰지 말 것

- `ytt.ytt` — **포맷 설명용 손글씨 문서다. converter 출력이 아니다.** 이걸 writer 동작 근거로 쓰다가 2차 리뷰가 두 번 틀렸다.
- DeepWiki, AI 요약, 오래된 블로그, 다른 converter의 추측
- 과거 특정 클라이언트에서만 재현된 workaround

규범 규칙은 항상 `upstream 소스 + 테스트 픽스처 + (가능하면) 실제 플레이어 검증`의 조합으로 세운다.

---

## 1. 프로젝트 개요

### 1.1 배경

유튜브 내장 자막 편집기는 스타일링을 전혀 지원하지 않는다. 하지만 유튜브 플레이어 자체는 **YTT(YouTube Timed Text, 내부 명칭 SRV3)** 라는 비공개 XML 포맷을 통해 색상·외곽선·글로우·위치 지정·가라오케 타이밍·루비·세로쓰기를 지원한다. 보컬로이드/우타이테 MV, 홀로라이브 뮤직비디오 등의 "화려한 자막"이 이 포맷으로 만들어진다.

현재 생태계에는 **변환기**(`YTSubConverter`, Aegisub `.ass` → `.ytt`)는 있지만, **영상 위에서 직접 배치하는 WYSIWYG 에디터가 없다.**

### 1.2 제품 한 줄 정의

> 로컬 영상을 띄워놓고, 자막을 마우스로 배치·정렬·스타일링하며, 유튜브 실제 렌더링에 근접한 미리보기를 보면서 `.ytt` / `.ass`를 출력하는 데스크톱 에디터.

### 1.3 대상 사용자

- MV/커버곡에 가라오케 자막을 넣으려는 개인 크리에이터
- Aegisub의 태그 문법을 외우고 싶지 않은 번역자
- 기존 `.ytt`를 받아서 수정·번역하려는 사람

---

## 2. 범위

### 2.1 반드시 되어야 하는 것

| # | 기능 |
|---|---|
| F1 | 로컬 영상 로드 + 프레임 정확 탐색 + 재생/일시정지/프레임 이동 |
| F2 | 영상 위 실시간 자막 미리보기 (유튜브 근사) |
| F3 | 자막 큐 드래그 이동, 앵커 포인트(ap) 9분할 지정 |
| F4 | **정렬**: 박스 내부 정렬(ju), 화면 기준 정렬, 다중 선택 정렬, 스냅 가이드 |
| F5 | 스타일 패널: 폰트/크기/굵기·기울임·밑줄/전경색·투명도/배경색·투명도/외곽선 종류·색 |
| F6 | 스타일 프리셋 정의 및 큐/섹션 단위 override |
| F7 | 타임라인: 큐 시작/길이 드래그 편집, 다중 트랙 |
| F8 | 가라오케 음절 분할 + 음절별 타이밍 탭 입력 |
| F9 | 효과: shake, chroma, fade, move, 색/크기 애니메이션, 가라오케 타입 |
| F10 | 루비 텍스트, 세로쓰기, 위/아래 첨자 |
| F11 | `.ytt` 직접 export / import |
| F12 | `.ass` export / import (Aegisub 왕복) |
| F13 | 검증기: 유튜브 제약 위반·모바일 미지원 항목 경고 |
| F14 | mitmproxy 연동으로 실제 유튜브 플레이어에서 확인 |
| F15 | 프로젝트 파일(`.yttproj`) 저장/열기, Undo/Redo |
| F16 | 플레이어 뷰포트 모드 전환 미리보기 (§7.8, M3) |

### 2.2 명시적으로 하지 않는 것

**포맷이 지원하지 않아서 불가능한 것 — UI에 넣지 말 것:**

- ❌ **회전** (텍스트 임의 각도). `ws.pd=3`의 90° CCW는 세로쓰기 모드지 자유 회전이 아니다. `[UPSTREAM]`
- ❌ **자유 스케일 변형** (X/Y 개별 배율, 기울이기). 폰트 크기 조절만 가능. `[UPSTREAM]`
- ❌ 그라데이션, 마스크, 클리핑, 블렌드 모드
- ❌ 이미지/도형 삽입
- ❌ 3D 변형, 모션 블러
- ❌ 하드섭 출력

> 회전 핸들을 UI에 만들면 안 된다. 사용자가 돌릴 수 있는데 결과물에 반영이 안 되는 게 최악이다. 대신 §9.4에 안내를 표시한다.

**범위 밖 (후순위 고려):** 음성 인식 자동 타이밍, 다국어 트랙 동시 관리, 정확한 JSON3 크기 estimator(§11).

---

## 3. 기술 스택

> **v1.1 변경:** .NET 8 → .NET 10. .NET 8과 .NET 9는 **2026-11-10 지원 종료**이며 .NET 10 LTS는 2028-11까지다 `[API]`. 지금 시작하는 프로젝트가 3개월 뒤 EOL인 런타임을 고를 이유가 없다.

| 항목 | 후보 | 근거 |
|---|---|---|
| 런타임 | **.NET 10 (LTS)** | `[API]` .NET 8/9 EOL 2026-11-10, 10은 2028-11까지 |
| 언어 | **C# 14**, nullable enable, `TreatWarningsAsErrors` | — |
| UI | **Avalonia 12.1.x** | `[API]` 12.0.0 stable 2026-04-07, 12.1.1 2026-07-29 |
| MVVM | CommunityToolkit.Mvvm | — |
| 렌더링 | **SkiaSharp** (Avalonia 12가 참조하는 버전에 정렬) | 버전 불일치 시 네이티브 심볼 충돌 |
| 자막 포맷 I/O | **YTSubConverter.Shared** (MIT, pin `b186a40b`) | `[UPSTREAM]` `netstandard2.0` → .NET 8/10 양쪽 호환 |
| 비디오 | **libmpv** Render API | §8 |
| 테스트 | xUnit + Verify | — |
| 로깅 | Serilog | — |

**이 표는 확정이 아니다.** 최신이라서 올리는 게 아니라 **지원 수명과 의존성 호환성**을 근거로 고른 후보이며, **M0 compatibility spike에서 실제 clean build로 최종 고정**한다. spike 실패 시 `docs/DEPENDENCIES.md`에 사유와 fallback(.NET 8 + Avalonia 11) 비용을 기록한다.

### 3.1 왜 Python이 아니라 C#인가

YTSubConverter가 C#/MIT이고 `YTSubConverter.Shared`가 UI에서 분리된 순수 라이브러리다. 프로젝트 참조로 끌어오면 즉시 확보되는 것:

- `.ass` 파서 + 오버라이드 태그 핸들러 전체
- `.ytt` 라이터 + 유튜브 버그 우회 파이프라인(§5.9) — 직접 재구현하면 수 주 소요, 미묘한 버그로 자막이 조용히 깨진다
- `\ytshake`, `\ytchroma`, `\ytkt(Fade/Glitch/Cursor)` 효과 생성기
- `.ytt` → `.ass` 역변환 (import이 사실상 공짜)

### 3.2 의존성 확보

1. `git submodule add https://github.com/arcusmaximus/YTSubConverter.git external/YTSubConverter`
2. **`b186a40bc21e58a8c9651cf616cbb5e80425dfc6`로 checkout — `master` 추종 금지.** `docs/DEPENDENCIES.md` 참조
3. 솔루션에 `external/YTSubConverter/YTSubConverter.Shared/YTSubConverter.Shared.csproj` 추가
4. `YttStudio.Core`에서 프로젝트 참조

> **`System.Drawing` 관련 (v1.1 검증 완료 — 크로스플랫폼 문제 없음) `[UPSTREAM]`**
>
> `Shared`가 실제로 사용하는 `System.Drawing` 타입을 전수 조사한 결과: `Color`(164), `PointF`(46), `SizeF`(22), `Size`(9), `Point`(6). **`Bitmap` / `Graphics` / `Brush` / `Pen` / `Image`는 0건이고, `.csproj`에 `PackageReference`도 0개다.**
>
> 사용 타입은 전부 **`System.Drawing.Primitives`** 소속이며 이건 공유 프레임워크의 일부로 모든 플랫폼에서 동작한다. Windows 전용인 것은 `System.Drawing.Common`(Bitmap/Graphics 계열)인데 `Shared`는 이를 참조하지 않는다. 따라서 **.NET 10 크로스플랫폼 전환에 걸림돌이 아니다.**
>
> 다만 `YttStudio.Core/Format/` 어댑터로 감싸는 것은 여전히 유지한다 — 크로스플랫폼 때문이 아니라 **도메인 모델을 외부 라이브러리 타입에서 분리**하기 위해서다(§6.4). upstream이 타입을 바꿔도 `Core`가 흔들리지 않게.

---

## 4. 솔루션 구조

```
YttStudio.sln
├── src/
│   ├── YttStudio.Core/            # 도메인 모델, 문서, Undo, 검증 — UI/렌더 의존 없음
│   │   ├── Model/                 # SubtitleProject, Cue, Section, StylePreset, Effect...
│   │   ├── Format/                # YTSubConverter.Shared 어댑터, import/export
│   │   ├── Project/               # .yttproj 직렬화 + 마이그레이션
│   │   ├── Validation/            # 유튜브 제약 린터
│   │   ├── Editing/               # DocumentEditor — 유일한 mutation 진입점 (§13)
│   │   └── Commands/              # IUndoableCommand 구현체
│   ├── YttStudio.Render/          # SkiaSharp 자막 렌더러 — Avalonia 의존 없음
│   │   ├── Layout/                # 텍스트 측정, 박스 계산, 앵커 배치
│   │   ├── Painting/              # pen → SKPaint 매핑, edge type
│   │   ├── Viewport/              # PlayerViewport 좌표계 (§7.8)
│   │   └── Effects/               # shake/chroma/fade/move 시각화
│   ├── YttStudio.Video/           # libmpv 래퍼 (§8)
│   └── YttStudio.App/             # Avalonia UI
│       ├── Views/ ViewModels/
│       ├── Controls/              # PreviewCanvas, Timeline, KaraokeEditor
│       ├── Preview/               # IExternalPlayerPreview, MitmproxyPreviewAdapter (§14)
│       └── Services/
├── tests/
│   ├── YttStudio.Core.Tests/
│   └── YttStudio.Render.Tests/    # layout / raster golden / smoke 3계층 (§15)
├── external/YTSubConverter/       # git submodule (pinned)
├── docs/
│   ├── SPEC.md                    # 이 문서
│   ├── SPEC-v1.1-CHANGELOG.md
│   ├── YTT-VERIFICATION.md
│   ├── DEPENDENCIES.md
│   ├── PROGRESS.md
│   └── MANUAL_QA.md
└── samples/
```

**의존 방향 (역행 금지):** `App → Render → Core`, `App → Video`, `App → Core`.
`Core`와 `Render`는 Avalonia를 참조하지 않는다. 렌더러는 헤드리스로 테스트 가능해야 한다.

---

## 5. YTT 포맷 레퍼런스 (규범)

> 취급 방법은 §0.1. 근거 등급은 §0.2. 상세 근거는 `docs/YTT-VERIFICATION.md`.

### 5.1 문서 구조 `[UPSTREAM]`

```xml
<?xml version="1.0" encoding="utf-8"?>
<timedtext format="3">
  <head>
    <wp id="0" .../>   <!-- 모든 wp 먼저, id=0은 더미 -->
    <ws id="0" .../>   <!-- 그 다음 모든 ws, id=0은 더미 -->
    <pen id="0" .../>  <!-- 마지막에 모든 pen, id=0은 더미 -->
  </head>
  <body>
    <p t="1000" d="2000" wp="1" ws="1" p="1">텍스트</p>
  </body>
</timedtext>
```

**하드 규칙:**

- 인코딩 UTF-8, **줄바꿈은 LF만**. CRLF는 iOS 유튜브 앱을 오작동시킨다. `[UPSTREAM]` (`XmlWriterSettings.NewLineChars = "\n"`)
- `<head>` 순서는 **모든 `<wp>` → 모든 `<ws>` → 모든 `<pen>`**. `[UPSTREAM]`
- 각 풀의 `id=0`은 **사용하지 않는 더미 항목**. `[UPSTREAM]`
- **각 풀 내에서** `id`는 엄격히 증가. `[UPSTREAM]` / 위반 결과는 `[EMPIRICAL]` 미확인

> **검증 기록 (v1.1):** 2차 리뷰가 위 두 규칙을 "upstream과 불일치"라며 삭제를 권고했으나, `YttDocument.cs:670-694` `WriteHead()`가 wp 루프 → ws 루프 → pen 루프 순으로 쓰고 세 풀 모두에 `Write*(writer, 0, ...)` 더미를 쓴다. 코드 주석 원문: *"The iOS app ignores the background color for the first pen and might have other, similar bugs too, so we write a dummy (unused) item for each of the lists."* 테스트 픽스처의 타입 등장 순서도 `['wp','ws','pen']`이다. **리뷰는 `ytt.ytt`(손글씨 문서)를 writer 출력으로 오인했다.** → `docs/YTT-VERIFICATION.md` V-01, V-02
>
> **ID 증가 규칙의 범위:** `ytt.ytt:4` 주석은 `<head>` 전체라고 읽히지만, 실제 출력은 `wp0..N, ws0..M, pen0..K`로 풀마다 0부터 재시작한다. 전역 증가가 아니다. 이 문서의 "각 풀 내에서"가 upstream 주석보다 정확하다.

**YttStudio의 방침:** 직렬화 순서와 ID 생성/더미 정책을 **자체적으로 재구성하지 않는다.** 규범은 pin된 YTSubConverter writer이며, 손으로 만드는 테스트 픽스처도 동일한 형태를 따라야 golden 비교가 성립한다. 도메인 모델은 외부 XML ID에 의존하지 않는다(§6).

### 5.2 `<wp>` — 창 위치 `[UPSTREAM]`

| 속성 | 타입 | 설명 |
|---|---|---|
| `id` | int | 고유 ID, 풀 내 증가 순서 |
| `ap` | int 0–8 | 앵커 포인트. 박스의 어느 지점을 `(ah, av)`에 고정할지 |
| `ah` | int 0–100 | 가로 위치 (%). 0=좌, 100=우. **서버는 정수만 허용** |
| `av` | int 0–100 | 세로 위치 (%). 0=상, 100=하 |

```
ap=0 ─────── ap=1 ─────── ap=2      (상단: 좌 / 중앙 / 우)
  │                          │
ap=3         ap=4         ap=5      (중단)
  │                          │
ap=6 ─────── ap=7 ─────── ap=8      (하단)
```

**⚠️ 좌표 변환 `[UPSTREAM]`:** 플레이어는 `effectiveCoord = specifiedCoord × 0.96 + 2` 를 적용한다.

```csharp
// YttDocument.cs:878-898
GetPixelCoord(youtubeCoord, maxValue) => (2 + youtubeCoord * 0.96f) / 100 * maxValue;
GetYouTubeCoord(pixelCoord, maxValue) => round(clamp((pixelCoord/maxValue*100 - 2) / 0.96f, 0, 100));
```

렌더러는 **반드시 이 변환을 적용해야** 미리보기가 실제와 맞는다. 내부 기준 프레임은 1280×720 (`ReferenceVideoDimensions`).

**⚠️ 극장 모드 `[UPSTREAM]`:** 극장 모드에서 `ah`는 좌우 검은 여백을 포함한 전체 폭 기준이 된다. 중앙에서 벗어난 자막이 화면 밖으로 밀릴 수 있다 → §7.8 뷰포트 모델 + §11 `W103`.

### 5.3 `<ws>` — 창 스타일 `[UPSTREAM]`

| 속성 | 타입 | 기본 | 설명 |
|---|---|---|---|
| `id` | int | — | 고유 ID |
| `ju` | int 0–2 | 2 | **박스 내부 텍스트 정렬**: 0=왼쪽, 1=오른쪽, 2=가운데 |
| `pd` | int 0–3 | 0 | 인쇄 방향 |
| `sd` | int 0–1 | 0 | 인쇄 방향 내 진행 방향 |
| `wfo` | int 0–254 | 0 | 창 채움 불투명도. **항상 명시적으로 0** (시청자가 W 키로 켤 수 있음) |

| `pd` | `sd` | 결과 |
|---|---|---|
| 0 | 0 | 가로쓰기 LTR (기본) |
| 1 | 0 | 가로쓰기 RTL (span 순서 역전) |
| 2 | 0 | 세로쓰기(글자 회전 없음), 열이 우→좌 |
| 2 | 1 | 세로쓰기(글자 회전 없음), 열이 좌→우 |
| 3 | 0 | 전체 90° 반시계 회전, 열이 좌→우 |
| 3 | 1 | 전체 90° 반시계 회전, 열이 우→좌 |

> **`ap`와 `ju`는 독립이다.** `ap`는 박스를 화면 어디에 못박을지, `ju`는 박스 안에서 여러 줄이 어떻게 정렬될지. UI에서 별도 컨트롤로 노출한다(§9.4).

### 5.4 `<pen>` — 텍스트 스타일 `[UPSTREAM]`

| 속성 | 타입 | 기본 | 비고 |
|---|---|---|---|
| `id` | int | — | 고유 ID |
| `fs` | int 0–7 | 0 | 폰트 스타일 ID. 0이면 생략 |
| `sz` | int ≥0 | 100 | 가상 폰트 크기 % (§5.6) |
| `b` / `i` / `u` | 0/1 | 0 | 굵게 / 기울임 / 밑줄. 0이면 생략 |
| `of` | 0/1/2 | 1 | 0=아래첨자, 1=보통, 2=위첨자 |
| `fc` | `#RRGGBB` | `#FFFFFF` | 전경색. **순백 금지 → `#FEFEFE`** |
| `fo` | int 0–254 | 254 | 전경 불투명도. **255 금지** |
| `bc` | `#RRGGBB` | `#080808` | 배경 박스 색. `bo=0`이면 생략 |
| `bo` | int 0–254 | 192 | 배경 박스 불투명도. **255 금지** |
| `et` | int 1–4 | 없음 | 엣지/그림자 종류 |
| `ec` | `#RRGGBB` | `#222222` | 엣지 색. `et` 있을 때만 유효 |
| `rb` | int | 0 | 루비 역할 |
| `hg` | 0/1 | 0 | 텍스트 패킹 |
| `te` | 1/2 | 없음 | 방점. **업로드 시 제거됨** — 로컬 서빙에서만 |

**`fs` 폰트 (이 8종이 전부. 임의 폰트 불가):**

| `fs` | 분류 | PC 렌더링 |
|---|---|---|
| 0 | 기본 (=4) | Roboto |
| 1 | 고정폭 세리프 | Courier New |
| 2 | 가변폭 세리프 | Times New Roman |
| 3 | 고정폭 산세리프 | Lucida Console |
| 4 | 가변폭 산세리프 | Roboto |
| 5 | 캐주얼 | Comic Sans MS |
| 6 | 필기체 | Monotype Corsiva |
| 7 | 스몰캡 | Arial (`font-variant: small-caps`) |

**`et` 엣지:** 1=하드 섀도, 2=베벨, 3=글로우/외곽선, 4=소프트 섀도.
**하나의 pen에는 `et` 하나만.** 겹치려면 줄 복제(`ExpandLineForMultiShadows`).

**`rb` 루비:** 0=아님, 1=기준 텍스트, 2=괄호 폴백, 4=기준 위, 5=기준 아래.

### 5.5 `<body>` `[UPSTREAM]`

**`<p>` (자막 큐):** `t`(시작 ms, **최솟값 1**), `d`(길이 ms), `wp`, `ws`, `p`(단일 섹션일 때만).

> `t="0"`이면 안드로이드 앱이 위치 지정을 무시하고 때때로 자막을 아예 표시하지 않는다. 최소 1로 clamp, 음수는 `t=1`로 밀고 duration을 줄인다.

**`<s>` (스팬):** `p`(pen 참조), `t`(**가라오케 활성화 오프셋**, 부모 `<p>`의 `t` 기준 상대 ms).

> **인접한 두 `<s>`가 같은 `t`를 가지면 안 된다.** 업로드 과정이 두 번째의 `t`를 제거해 가라오케가 깨진다. `AvoidZeroDurationKaraoke()`가 1ms를 더한다.

> **다중 섹션 워크어라운드:** `<s>`로 감싸이지 않은 맨 텍스트 노드가 없으면 업로드 서버가 **첫 `<s>`의 `p`를 제거한다.** 첫 `<s>` 뒤에 U+200B을 삽입해 우회.

### 5.6 폰트 크기 공식 `[UPSTREAM]`

```
real = 1 + (yttScale/100 - 1) / 4        // YttDocument.cs:904
ytt  = round(max(1 + (real - 1) * 4, 0) * 100)
```

| `sz` | 실제 배율 |
|---|---|
| 0 | 75% (**하한 — 포맷 제약**) |
| 100 | 100% |
| 200 | 125% |
| 300 | 150% |
| 500 | 200% |

> **렌더러는 반드시 이 공식을 적용해야 한다.** `sz`를 그대로 배율로 쓰면 미리보기가 4배 과장된다.
>
> **상한은 없다.** `GetYouTubeFontScale()`은 `Math.Max(..., 0)` 하한만 clamp하고 상한 clamp가 없다 `[UPSTREAM]`. UI의 200%는 **`[PRODUCT]` UX 선택**이지 포맷 한계가 아니다(§9.5).

### 5.7 색상 / 불투명도 제약 `[UPSTREAM]`

| 제약 | 이유 |
|---|---|
| `fo`, `bo` ≠ 255 | 255면 업로드가 속성을 제거하고 시청자 개인 설정이 스타일을 덮어쓴다 |
| `fc` ≠ `#FFFFFF` | 안드로이드가 순백을 무시하고 앞 섹션 색을 상속. `#FEFEFE` 사용 |
| `ec` 명시 시 그림자는 항상 완전 불투명 | `ec` 생략(기본 `#222222`)이어야만 그림자 불투명도가 `fo`를 따라감 |

### 5.8 플랫폼 호환성

> ⚠️ **이것은 영구 규격이 아니라 2026-08-24 시점의 관찰이다.** iOS/Android 유튜브 앱은 업데이트로 동작이 바뀔 수 있다. YTSubConverter pin 갱신 시 함께 재검증하고 날짜를 갱신한다. `[UPSTREAM]` (검증일 2026-08-24)

| 기능 | PC | iOS | Android |
|---|:--:|:--:|:--:|
| 전경색 `fc` | ✓ | ✓ | 색만 (투명도 무시) |
| 전경 투명도 `fo` | ✓ | ✓ | ✗ |
| 배경 `bc`/`bo` | ✓ | 접근성 "비디오 재정의" 필요 | ✗ |
| 엣지/그림자 `et`/`ec` | ✓ | ✗ | ✗ |
| 폰트 `fs` | ✓ | ✓ (다르게 보임) | ✗ |
| 폰트 크기 `sz` | ✓ | ✓ | ✗ |
| 위/아래첨자 `of` | ✓ | ✗ | ✗ |
| 루비 `rb` | ✓ | ✗ | ✗ |
| 텍스트 패킹 `hg` | ✓ | ✗ | ✗ |
| 세로쓰기 `pd`≥2 | ✓ | ✗ | ✗ |

**크기/효과 제약 `[UPSTREAM]` (README 서술, 정량 재현 미확인):**
- 브라우저: **JSON3로 변환된** YTT의 압축 크기(bit) ÷ 영상 길이(초) > 10240 이면 자막 미표시 → §11 `W101`
- 모바일: transform/fade/movement를 상당량 사용한 YTT는 **자막 선택지에 아예 나타나지 않는다.** 정량 기준 불명 → `[HEURISTIC]` 경고만
- 업로드 후 스튜디오 편집기에서 뭐라도 수정하면 **스타일 정보가 전부 소실**

### 5.9 저장 전처리 파이프라인 `[UPSTREAM]`

> **v1.1 정정:** v1.0은 DeepWiki 요약을 근거로 평면 12단계 목록을 기술했으나 실제 구조가 다르다.

`YttDocument.Save()` 실제 구성 (`YttDocument.cs:92-113`):

```csharp
CloseGaps();                          // ← base class(SubtitleDocument) 소속
MergeSimultaneousLines();             // ← base class 소속
MergeIdenticallyFormattedSections();
ApplyEnhancements();                  // ← 줄 단위 보정이 전부 이 안에
MergeIdenticallyFormattedSections();  // ← 두 번째 호출

positions    = ExtractAttributes(Lines, new LinePositionComparer(this));
windowStyles = ExtractAttributes(Lines, new LineAlignmentComparer());
pens         = ExtractAttributes(Lines.SelectMany(l => l.Sections),
                                 new NormalizedSectionFormatComparer());
// XmlWriterSettings { NewLineChars = "\n" }  ← iOS 우회
```

`ApplyEnhancements()` 안의 줄 단위 보정:

| 메서드 | 목적 |
|---|---|
| `AddItalicPrefetch()` | `t=5000ms`에 보이지 않는 이탤릭 자막 삽입 — PC 이탤릭 글리프 지연 로딩 회피 |
| `MakeInvisibleTextBlack()` | 완전 투명 텍스트를 `#000000`으로 — 안드로이드 검은 배경 융화 |
| `PreventShadowClipping()` | 그림자가 잘리지 않도록 인접 섹션의 공백 이동 |
| `HardenSpaces()` | 연속 공백을 non-breaking space로 |
| `LimitColors()` | `fo`/`bo` 254 상한, `#FFFFFF`→`#FEFEFE`, 그림자 불투명도 정규화 |
| `ExpandLineForMultiShadows()` | 그림자 종류별 줄 복제 |
| `ExpandLineForDarkText()` | 어두운 텍스트 위에 투명한 밝은 복제본 (안드로이드 가독성) |
| `ApplyManualLinePadding()` | 각 줄 앞뒤에 보이지 않는 패딩 `<s>` |
| `AvoidZeroDurationKaraoke()` | 인접 `<s>` 동일 오프셋에 1ms 추가 |

`ExtractAttributes()`는 파이프라인 단계가 아니라 write 직전 별도 호출로 중복 제거 + ID 부여를 한다.

### 5.10 YTT에 없는 개념 `[UPSTREAM]`

- **레이어 / z-order 없음.** `YttDocument.cs`에 `Layer` 참조가 0건이다. ASS의 `Layer`는 `.ass` 왕복에서만 보존되고 `.ytt`로 나갈 때 소실된다. 그리기 순서는 `<body>` 안 `<p>` 등장 순서로만 결정된다. → §6.3, §11 `I203`
- **회전 / 자유 스케일 없음.** §2.2

---

## 6. 도메인 모델 (`YttStudio.Core`)

편집용 모델이다. YTT XML과 1:1이 아니라 편집하기 좋은 형태로 두고 export 시 변환한다. **외부 XML의 ID에 의존하지 않는다.**

### 6.1 문서

```csharp
public sealed class SubtitleProject
{
    public string? VideoPath { get; internal set; }
    public VideoInfo? Video { get; internal set; }
    public StylePresetCollection Styles { get; }
    public CueCollection Cues { get; }              // §6.2
    public ProjectSettings Settings { get; internal set; }
}

/// Fps는 표시/추정용 metadata일 뿐이다. 실제 frame step·exact seek·
/// 타임라인 프레임 스냅은 decoder timestamp를 쓴다 (§8.5, VFR 대응).
public sealed record VideoInfo(int Width, int Height, TimeSpan Duration, double NominalFps);
```

### 6.2 `CueCollection` — 정렬 인덱스 + 활성 큐 조회

> **v1.1 변경:** v1.0의 `ObservableCollection<Cue> Cues // 시작 시각 정렬 유지` 주석은 지킬 수 없는 약속이었다. `ObservableCollection<T>`는 요소 프로퍼티가 바뀌어도 재정렬하지 않는다.

```csharp
public sealed class CueCollection : IReadOnlyCollection<Cue>, INotifyCollectionChanged
{
    // authoritative 저장소 — 순서 무의미
    private readonly Dictionary<Guid, Cue> _byId;
    // 시작 시각 정렬 인덱스 — DocumentEditor만 갱신
    private readonly List<Cue> _sortedByStart;

    public Cue? this[Guid id] { get; }

    /// 현재 시각에 보이는 큐. 정렬 인덱스에 이분 탐색 후 겹침 스캔.
    public IReadOnlyList<Cue> GetActiveAt(TimeSpan time);

    /// 재생 중 프레임마다 전체 순회를 피하기 위한 증분 조회.
    /// 직전 호출의 active set을 재사용하고 진입/이탈만 반영한다.
    public ActiveSetDelta AdvanceTo(TimeSpan time);

    internal void Add(Cue cue);       // DocumentEditor 전용
    internal void Remove(Guid id);
    internal void OnStartChanged(Cue cue);   // 정렬 인덱스 재배치
}
```

**요구:** 3분 곡의 가라오케면 큐가 수백 개다. 렌더러가 프레임마다 전체를 순회하면 안 된다.

### 6.3 큐

```csharp
public sealed class Cue
{
    public Guid Id { get; }
    public TimeSpan Start { get; internal set; }
    public TimeSpan End { get; internal set; }

    /// 편집 UI 조직용. 타임라인의 행. 렌더링·export에 영향 없음.
    public int Track { get; internal set; }

    /// YttStudio 내부 그리기 순서. 클수록 위.
    /// ⚠ YTT에 z-order 개념이 없다(§5.10). export 시 <p> 등장 순서로만 근사되며
    ///   왕복 보존되지 않는다. 겹치는 큐가 있으면 I203 경고.
    public int ZOrder { get; internal set; }

    public AnchorPoint Anchor { get; internal set; } = AnchorPoint.BottomCenter;
    public double PositionX { get; internal set; }   // 0..100 (%)
    public double PositionY { get; internal set; }

    public Justification Justify { get; internal set; } = Justification.Center;
    public TextDirection Direction { get; internal set; } = TextDirection.Horizontal;

    public Guid? StyleId { get; internal set; }      // §6.5 — 이름 아닌 ID 참조
    public IReadOnlyList<Section> Sections { get; }
    public IReadOnlyList<CueEffect> Effects { get; }
}

public enum AnchorPoint
{
    TopLeft = 0,    TopCenter = 1,    TopRight = 2,
    MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5,
    BottomLeft = 6, BottomCenter = 7, BottomRight = 8
}

public enum Justification { Left = 0, Right = 1, Center = 2 }

public enum TextDirection
{
    Horizontal, HorizontalRtl,
    VerticalRightToLeft, VerticalLeftToRight,
    RotatedLeftToRight, RotatedRightToLeft
}
```

> **`internal set`은 의도적이다.** 도메인 mutation은 `DocumentEditor`를 통해서만 가능하다(§13). `[InternalsVisibleTo]`는 테스트 어셈블리에만 부여한다.

### 6.4 섹션 + 스타일 해석

> **v1.1 변경:** v1.0은 `Section.Format`에 전체 값을 복사했다. 그러면 "스타일에서 상속된 100%"와 "사용자가 명시적으로 지정한 100%"를 구분할 수 없어, 스타일을 120%로 바꿔도 어느 섹션이 따라와야 하는지 알 수 없다.

```csharp
public sealed class Section
{
    public string Text { get; internal set; } = "";
    public TimeSpan? KaraokeOffset { get; internal set; }
    public SectionOverrides Overrides { get; internal set; } = new();
    public RubyRole Ruby { get; internal set; } = RubyRole.None;
    public string? RubyText { get; internal set; }
}

/// 모든 필드 nullable. null = "스타일에서 상속", 값 = "명시적 override".
public sealed class SectionOverrides
{
    public YtFont? Font { get; set; }
    public int? SizePercent { get; set; }
    public bool? Bold { get; set; }
    public bool? Italic { get; set; }
    public bool? Underline { get; set; }
    public ScriptOffset? Offset { get; set; }
    public RgbaColor? Foreground { get; set; }
    public RgbaColor? Background { get; set; }
    public RgbaColor? SecondaryColor { get; set; }   // 가라오케 미부른 부분
    public EdgeType? Edge { get; set; }
    public RgbaColor? EdgeColor { get; set; }
    public bool? Pack { get; set; }

    public bool IsEmpty { get; }
}

/// 완전히 채워진 값. 렌더러와 export가 쓰는 유일한 형태.
public sealed record ResolvedFormat(
    YtFont Font, int SizePercent, bool Bold, bool Italic, bool Underline,
    ScriptOffset Offset, RgbaColor Foreground, RgbaColor Background,
    RgbaColor SecondaryColor, EdgeType Edge, RgbaColor EdgeColor, bool Pack);

public static class FormatResolver
{
    public static ResolvedFormat Resolve(SectionFormat baseFormat, SectionOverrides o);
}

public enum YtFont { Default = 0, MonoSerif = 1, Serif = 2, MonoSans = 3,
                     Sans = 4, Casual = 5, Cursive = 6, SmallCaps = 7 }
public enum EdgeType { None = 0, HardShadow = 1, Bevel = 2, Glow = 3, SoftShadow = 4 }
public enum ScriptOffset { Subscript = 0, Regular = 1, Superscript = 2 }
public enum RubyRole { None, Base, Above, Below }
```

> **`SizePercent` 주의:** 모델에는 사용자가 이해하는 **실제 백분율**(75%~)을 저장한다. Export 시 `sz = round(max(1+(real/100-1)*4, 0) * 100)`, Import 시 `real = (1 + (sz/100-1)/4) * 100`. 왕복 오차 없음을 테스트로 보장(§15).

**`System.Drawing` 격리:** `RgbaColor`는 YttStudio 자체 타입이다. `YTSubConverter.Shared`의 `System.Drawing.Color`는 `Core/Format/` 어댑터 안에서만 등장하며 밖으로 새지 않는다(§3.2).

### 6.5 스타일 프리셋

```csharp
public sealed class StylePreset
{
    public Guid Id { get; init; }                 // 참조 키. rename에 영향 없음
    public string Name { get; internal set; } = "Default";
    public SectionFormat BaseFormat { get; internal set; } = new();
    public AnchorPoint DefaultAnchor { get; internal set; } = AnchorPoint.BottomCenter;
    public Justification DefaultJustify { get; internal set; } = Justification.Center;
    public EdgeType[] ExtraEdges { get; internal set; } = [];   // 줄 복제로 구현
}
```

**규칙:**

- 스타일 삭제 시 참조하는 큐는 `StyleId = null`(= Default 스타일)로 강등하고, 삭제 직전의 해석된 값을 각 섹션의 `Overrides`에 굳혀서 **외형이 변하지 않게** 한다. 이 동작을 사용자에게 확인 다이얼로그로 알린다.
- ASS `\r` (스타일 리셋) → 해당 지점부터 새 `Section`을 시작하고 `Overrides`를 비운다.
- ASS `\rStyleName` → 새 `Section`의 `Overrides`를 비우고, 큐 단위가 아닌 **섹션 단위 스타일 참조**가 필요하므로 `Section.StyleIdOverride`(nullable Guid)를 둔다.
- **`Default` 스타일 규칙 `[UPSTREAM]`:** YTSubConverter는 `Default` 스타일(없으면 첫 스타일)을 항상 유튜브 표준 크기 100%로 취급하고 나머지를 상대 계산한다 (`AssDocument.cs:78`, `:309` `section.Scale = style.LineHeight / DefaultStyle.LineHeight`). `.ass` export 시 이 규칙을 지켜야 왕복이 일치한다.

### 6.6 효과

```csharp
public abstract class CueEffect { }

public sealed class MoveEffect : CueEffect
{ public double FromX, FromY, ToX, ToY; public TimeSpan? StartTime, EndTime; }

public sealed class FadeEffect : CueEffect
{ public TimeSpan FadeIn, FadeOut;
  public int? Alpha1, Alpha2, Alpha3; public TimeSpan? T1, T2, T3, T4; }

public sealed class ShakeEffect : CueEffect
{ public double RadiusX = 20, RadiusY = 20; public TimeSpan? StartTime, EndTime; }

public sealed class ChromaEffect : CueEffect
{ public double OffsetX = 20, OffsetY = 0;
  public TimeSpan InTime = TimeSpan.FromMilliseconds(270);
  public TimeSpan OutTime = TimeSpan.FromMilliseconds(270);
  public List<RgbaColor>? CustomColors; }

public sealed class AnimateEffect : CueEffect          // ASS \t
{ public TimeSpan Start, End; public double Accel = 1.0;
  public RgbaColor? ToForeground, ToEdgeColor; public int? ToSizePercent; }

public enum KaraokeType { None, Simple, Fade, Glitch, Cursor, LeftCursor }

public sealed class KaraokeSettings : CueEffect
{ public KaraokeType Type = KaraokeType.Simple;
  public string? CursorText; public TimeSpan? CursorInterval; }
```

---

## 7. 렌더러 명세 (`YttStudio.Render`)

### 7.1 설계 원칙

> **픽셀 완전 일치를 목표로 하지 않는다.**
> 유튜브의 실제 자막 렌더러는 브라우저 DOM/CSS다. Skia로 재현하면 글로우 반경, 외곽선 두께, 줄바꿈 지점이 계속 미묘하게 어긋난다. 내장 미리보기는 **"편집용 고품질 근사치"** 로 선을 긋고, 최종 확인은 §14 실제 플레이어 프리뷰로 한다. 이 결정을 뒤집지 말 것.

**단, 축을 나눈다 (v1.1):**

| 축 | 기준 |
|---|---|
| **레이아웃 수치** (앵커 좌표, 박스 bounds, baseline, resolved font size) | **엄격.** tolerance 안에 들어와야 함. §15.1 |
| **래스터 외형** (글로우 반경, 안티앨리어싱, 폰트 힌팅) | **근사 허용.** 고정 환경 golden만. §15.2 |

이렇게 나누면 §16의 마일스톤 게이트를 수치화하면서도 §7.1 원칙과 충돌하지 않는다.

### 7.2 진입점

```csharp
public interface ISubtitleRenderer
{
    void Render(SKCanvas canvas, PlayerViewport viewport,
                SubtitleProject project, TimeSpan time, RenderOptions options);

    /// 에디터 히트 테스트용
    IReadOnlyList<CueHitBox> Measure(PlayerViewport viewport,
                                     SubtitleProject project, TimeSpan time);
}

public sealed record CueHitBox(Cue Cue, SKRect Bounds, SKPoint AnchorScreenPoint);

public sealed record RenderOptions
{
    public bool ShowSafeArea { get; init; }
    public bool ShowAnchorPoints { get; init; }
    public bool ApplyCoordinateTransform { get; init; } = true;   // §5.2
    public double FontScaleBase { get; init; } = 1.0;
}
```

### 7.3 기준 좌표계

- 내부 계산은 **1280×720 기준 프레임** (`ReferenceVideoDimensions`와 동일). 출력 시 실제 캔버스 크기로 스케일.
- 기본 폰트 크기: 720p 기준 **32px = 100%**. `[PRODUCT]` — 유튜브 실제 값과 완전 동일하진 않으나 편집 기준으로 충분. 설정에서 조정 가능.

### 7.4 레이아웃 알고리즘

```
1. 큐의 섹션을 이어 붙이고 명시적 개행(\n)으로 줄 분리
2. 각 줄을 SKFont로 측정 → (width, ascent, descent)
3. 박스 크기 = (max 줄 너비 + 좌우 패딩, 줄 높이 합 + 상하 패딩)
4. 앵커 좌표: ah_eff = ah * 0.96 + 2  (ApplyCoordinateTransform 시)
              ax = ah_eff / 100 * viewport.SubtitleSpace.Width
5. ap로 박스 원점: originX = ax - boxW * (ap % 3) / 2.0
                   originY = ay - boxH * (ap / 3) / 2.0
6. 각 줄의 x는 ju로: 0 → 좌, 1 → 우, 2 → 중앙
7. pd>=2면 글자 단위 세로 배치, pd=3이면 캔버스 90° CCW 회전 후 렌더
8. 겹치는 큐는 ZOrder 오름차순으로 그림 (§6.3)
```

**자동 줄바꿈은 하지 않는다.** 유튜브의 줄바꿈 규칙을 재현할 수 없으므로 사용자가 명시적으로 개행한다. 박스가 폭을 넘으면 `W105` 경고.

### 7.5 `pen` → `SKPaint` 매핑

| YTT | Skia 구현 |
|---|---|
| `fc` + `fo` | `SKColor(r,g,b, fo * 255 / 254)` |
| `bc` + `bo` | 박스 `SKRect`를 별도 paint로 먼저 채움. `bo=0`이면 생략 |
| `b` / `i` | `SKFontStyleWeight.Bold` / `SKFontStyleSlant.Italic` |
| `u` | 베이스라인 + descent/2에 두께 `fontSize/16` 선 |
| `sz` | `fontSize = baseSize * realPercent / 100` (§5.6 공식 적용 후) |
| `fs` | §5.4 매핑 → `SKTypeface.FromFamilyName`, 없으면 폴백 |
| `of=0/2` | 크기 × 0.65, 베이스라인 오프셋 ±`fontSize * 0.3` |
| `et=1` 하드 섀도 | `dx=dy=fontSize*0.06` 오프셋으로 `ec` 색 먼저 |
| `et=2` 베벨 | 밝은 색 (-1,-1) + 어두운 색 (+1,+1) |
| `et=3` 글로우 | `Style=Stroke, StrokeWidth=fontSize*0.08, StrokeJoin=Round` + `CreateBlur(Normal, fontSize*0.06)` |
| `et=4` 소프트 섀도 | `CreateBlur(Normal, fontSize*0.1)` + 오프셋 |
| `hg=1` | 런의 문자를 `SKTextBlob`로 묶어 전각 1칸에 균등 배치 |
| `rb=4/5` | 기준 런 위/아래에 `fontSize*0.5` 크기 중앙 정렬 |

그리는 순서: **배경 박스 → 엣지/그림자 → 본문 → 밑줄 → 루비**

### 7.5.1 ⚠️ 폰트 가용성 — 크로스플랫폼 최대 난점

§5.4의 8종은 **유튜브가 PC(브라우저)에서 쓰는 폰트**다. YttStudio가 미리보기에서 같은 글꼴을 그리려면 로컬에 그 폰트가 있어야 하는데, **OS마다 없다.**

| `fs` | 폰트 | Windows | macOS | Linux |
|---|---|:--:|:--:|:--:|
| 0/4 | Roboto | ✗ | ✗ | 배포판별 |
| 1 | Courier New | ✓ | ✓ | ✗ |
| 2 | Times New Roman | ✓ | ✓ | ✗ |
| 3 | Lucida Console | ✓ | ✗ | ✗ |
| 5 | Comic Sans MS | ✓ | ✓ | ✗ |
| 6 | Monotype Corsiva | Office 설치 시 | ✗ | ✗ |
| 7 | Arial (small-caps) | ✓ | ✓ | ✗ |

**결과:** 아무 대응 없이 두면 Linux 미리보기는 대부분 폴백 폰트로 렌더되고, macOS는 Roboto·Lucida Console·Monotype Corsiva가 빠진다. 크래시는 아니지만 **WYSIWYG 도구로서 치명적인 정확도 손실**이다.

**대응 (M1에서 구현):**

- **번들 가능한 것은 앱에 포함한다.** Roboto는 Apache-2.0이라 재배포 가능하다. 앱 리소스로 넣고 `SKTypeface.FromStream`으로 로드해 **모든 OS에서 동일하게** 그린다. 유튜브 기본 폰트라 가장 중요하다.
- **metric-compatible 대체를 쓴다.** Liberation Sans / Serif / Mono (SIL OFL)는 각각 Arial / Times New Roman / Courier New와 메트릭이 호환된다. 폭·줄높이가 같아 레이아웃 계산이 어긋나지 않는다.
- **대체 불가한 것은 명시한다.** Lucida Console, Comic Sans MS, Monotype Corsiva는 자유 재배포 대체본이 없다. 로컬에 없으면 폴백하되 **미리보기에 "이 폰트는 로컬에 없어 근사 표시 중 — 실제 유튜브와 다릅니다"** 배지를 띄운다. 조용히 다른 글꼴로 그리면 사용자가 속는다.
- 폰트 해석 결과(요청 → 실제 사용 typeface)를 `IFontResolver`로 추상화하고 로그에 남긴다.

**§15.2 raster golden 테스트에 미치는 영향:** golden PNG는 **번들 폰트만 사용하는 케이스로 한정**한다. 시스템 폰트에 의존하는 케이스는 layout test(수치)와 smoke test로만 검증한다. 그렇지 않으면 골든이 개발자 머신마다 깨진다.

### 7.6 효과 시각화

| 효과 | 미리보기 계산 |
|---|---|
| Move | 진행률로 선형 보간 |
| Fade | 진행률에 따라 전체 알파 곱 |
| Shake | `Random(seed: cueId ^ frameIndex)` 로 ±radius. **결정적이어야** 스크럽 시 재현됨 |
| Chroma | R/G/B(또는 커스텀) 복제본을 오프셋에 먼저 그리고 진행률로 수렴/발산 |
| Animate | `pow(progress, accel)` 보간 |
| Karaoke | `time - cue.Start` 로 각 섹션 `KaraokeOffset` 비교. 부른 부분 `Foreground`, 안 부른 부분 `SecondaryColor` |
| Karaoke.Glitch | 안 부른 섹션 글자를 같은 스크립트(라틴/한글/가나/한자)의 랜덤 문자로 치환 |

### 7.7 성능 요구

> **v1.1 정정:** v1.0의 "프레임당 8ms"가 자막 렌더인지 전체인지 모호했다.

- **자막 오버레이 렌더 시간만** 기준: 1080p 캔버스, 동시 표시 큐 10개에서 **≤ 8ms**.
- 영상 디코드/프레젠테이션 예산은 별도(§8.6).
- `SKTypeface`, `SKPaint`, `SKTextBlob`는 캐시한다. 캐시 키는 `ResolvedFormat`의 값 해시. 프레임마다 재생성 금지.

### 7.8 `PlayerViewport` — 유튜브 표시 모드 (M3)

> **v1.1 신설.** WYSIWYG 도구에서 극장 모드 위치 변화를 "경고만" 하는 건 약하다.

```csharp
public enum PreviewViewportMode
{
    VideoFrame,        // 영상 프레임 그대로 (기본, M1~M2)
    YouTubeDefault,
    YouTubeTheater,
    YouTubeFullscreen,
    MobilePortrait
}

public sealed record PlayerViewport(
    SKSize PlayerSize,        // 플레이어 전체 크기
    SKRect VideoContentRect,  // 그 안에서 실제 영상이 차지하는 사각형
    SKRect SubtitleSpace,     // ah/av가 기준으로 삼는 좌표 공간
    PreviewViewportMode Mode);
```

핵심은 **`VideoContentRect`와 `SubtitleSpace`가 다를 수 있다**는 것이다. 극장 모드에서 `ah`는 검은 여백을 포함한 폭 기준이므로 `SubtitleSpace` ⊃ `VideoContentRect`가 된다 `[UPSTREAM]`.

> ⚠️ **각 모드의 실제 좌표 동작은 `[EMPIRICAL]` 미측정이다** (`YTT-VERIFICATION.md` E-8). **추측으로 구현 금지.** M3 진입 전에 브라우저에서 기준 픽스처(알려진 `ah`/`av`의 자막을 각 모드에서 스크린샷 측정)를 만들고 그 수치로 구현한다. 측정 전에는 `VideoFrame` 모드만 활성화하고 나머지는 UI에 비활성 + "측정 대기 중" 표시.

UI: 미리보기 상단 탭 `[영상] [YouTube 일반] [극장] [전체화면] [모바일]`

---

## 8. 비디오 파이프라인 (`YttStudio.Video`)

### 8.1 ⚠️ 제약 1: airspace

LibVLCSharp `VideoView`나 mpv를 **네이티브 윈도우 핸들에 직접 붙이면** 그 표면이 별도 네이티브 창이 되어 Avalonia 컨트롤(선택 박스, 드래그 핸들, 정렬 가이드)이 **영상 위에 올라가지 않는다.** 이러면 F3/F4가 통째로 막힌다. → 콜백 렌더링 필수.

### 8.2 ⚠️ 제약 2: libmpv 스레딩 `[API]`

> **v1.1 신설.** v1.0은 이 규칙을 전혀 기술하지 않았다.

`libmpv/render.h` "Threading" 섹션의 요구사항:

- rendering을 일반 libmpv 사용 스레드와 **분리할 것을 권장**
- `mpv_render_*` 함수는 **동시에 하나만** 호출 가능
- `mpv_set_wakeup_callback()` / `mpv_render_context_set_update_callback()` 콜백 **내부에서 절대 호출 불가**
- update callback 안에서는 *"you must not call any mpv API"*
- **render thread → libmpv thread 방향의 lock/wait 의존이 있으면 deadlock**
- OpenGL 백엔드: GL context가 호출 스레드에서 current이고 render context 생성 시와 동일해야 함. 아니면 undefined behavior

**하드 규칙:**

```
mpv update callback
    ↓  signal only (이벤트 set / 채널 post — 그 외 아무것도 하지 않음)
RenderThread (전용)
    ↓  mpv_render_context_update()
    ↓  mpv_render_context_render()
    ↓  latest back buffer 갱신 + "frame ready" 신호
UI thread
    ↓  latest buffer를 WriteableBitmap으로 blit
```

- update callback 내부에서 render/libmpv API 실행 **금지**
- render thread와 command/control thread 역할 **분리**
- RenderThread에서 일반 libmpv handle API 호출 **금지**
- **frame backlog 길이 1** (latest-frame-wins)
- **SW renderer(`MPV_RENDER_API_TYPE_SW`)는 fallback/debug 전용.** 공식 문서가 "extremely simple (but slow)"로 기술하며 color conversion/scaling/OSD를 CPU 단일 스레드로 수행한다. production 기본 경로는 GPU Render API를 우선 검토하되, GL readback 비용이 있으므로 **M2 benchmark 후 결정**한다.

### 8.3 프레임 인터페이스

> **v1.1 전면 재설계.** v1.0의 `event EventHandler<VideoFrame>` + `readonly ref struct VideoFrame`은 **컴파일되지 않는다.** C# 13 이전에는 `ref struct`를 제네릭 타입 인자로 쓸 수 없고, C# 13의 `allows ref struct`는 제네릭 파라미터 선언 쪽에 필요한데 BCL의 `EventHandler<TEventArgs>`에는 없다 `[API]`. 추가로 `ReadOnlySpan<byte>`를 이벤트/디스패처 경계로 넘기면 수명 관리가 불가능하다.

**채택: 공유 백버퍼 + latest-frame-wins.** 편집기에서 뒤처진 프레임을 큐잉하는 건 무의미하다 — 스크럽 중 밀린 프레임은 폐기가 정답이다.

```csharp
public interface IVideoSource : IAsyncDisposable
{
    VideoInfo Info { get; }
    TimeSpan Position { get; }
    bool IsPlaying { get; }

    Task LoadAsync(string path, CancellationToken ct);
    void Play();
    void Pause();
    Task SeekAsync(TimeSpan position, bool exact = true, CancellationToken ct = default);
    void StepFrame(int delta);
    void SetSpeed(double speed);              // 0.25 ~ 2.0

    /// 새 프레임 준비 신호. 버퍼를 전달하지 않는다 — 소비자가 직접 잠근다.
    /// UI 스레드가 아닌 곳에서 발생할 수 있다. 핸들러는 짧아야 한다.
    event Action FrameReady;

    /// 최신 프레임을 잠그고 읽는다. using 스코프 안에서만 유효.
    /// 잠근 동안 producer는 다른 백버퍼에 쓴다(더블 버퍼).
    /// 반환 false = 아직 프레임 없음 또는 종료 중.
    bool TryLockLatestFrame(out VideoFrameLock frame);
}

/// 읽기 잠금. Dispose 전까지 Pixels가 유효하다.
public readonly struct VideoFrameLock : IDisposable
{
    public ReadOnlySpan<byte> Pixels { get; }   // BGRA8888 premultiplied
    public int Width { get; }
    public int Height { get; }
    public int Stride { get; }
    public TimeSpan Timestamp { get; }
    public long SequenceNumber { get; }

    public void Dispose();                       // 잠금 해제
}
```

> `VideoFrameLock`은 `ref struct`여도 되지만, **제네릭 인자나 이벤트 페이로드로 쓰지 않는다.** 오직 `TryLockLatestFrame`의 `out` 파라미터로만 등장한다. 이게 v1.0이 어긴 지점이다.

### 8.4 반드시 지킬 수명 규칙

| 항목 | 규칙 |
|---|---|
| 프레임 메모리 소유자 | `IVideoSource` 구현체. 소비자는 절대 소유하지 않는다 |
| 버퍼 수 | 더블 버퍼. producer가 back에 쓰는 동안 consumer가 front를 잠금 |
| lock 수명 | `VideoFrameLock` Dispose까지. **UI blit 안에서 즉시 해제.** 잠근 채로 await 금지 |
| backlog | 없음. 소비되지 않은 프레임은 다음 프레임이 덮어쓴다 |
| seek 중 stale 프레임 | seek 시작 시 `SeekEpoch` 증가. epoch가 다른 프레임은 폐기 |
| 순서 검증 | `SequenceNumber` 단조 증가. 역행 프레임은 무시 |
| 종료 race | `DisposeAsync`는 (1) update callback 해제 → (2) RenderThread join → (3) mpv 파괴 순. 이후 `TryLockLatestFrame`은 false |
| 할당 | 프레임마다 `new byte[]` 금지. 버퍼는 재사용, 크기 변경 시에만 재할당 |

### 8.5 시크 / 프레임 이동 / VFR

- 타임라인 드래그 중에는 `exact: false`로 반응성 확보, 놓으면 `exact: true` 재시크.
- **`VideoInfo.NominalFps`는 표시/추정용이다.** VFR 영상에서는 단일 `double`로 프레임 경계를 표현할 수 없다. 실제 frame step / 현재 프레임 PTS / exact seek / 타임라인 프레임 스냅은 **decoder timestamp 기준**으로 처리한다.
- 어떤 mpv property 조합(`frame-step`, `playback-time`, `estimated-vf-fps`, `container-fps`)이 안전한지는 **M2 spike의 산출물**로 확정하고 이 절에 기록한다.
- 오디오는 mpv에 맡긴다. 가라오케 탭 입력(§9.7)에 필수.
- **M1에서는 영상 없이 동작해야 한다** — 단색/체커보드 배경 렌더링 모드 제공.

### 8.6 M2 성능 spike

측정 해상도: 1920×1080@60, 2560×1440@60, 3840×2160@30.

확인 항목: 재생 중 frame drop / seek latency / CPU / GPU / memory bandwidth·copy cost / 자막 오버레이 10개 동시 / 0.5x·2x 재생 / 리사이즈 중 안정성.

예산 분리: **자막 렌더 ≤ 8ms** (§7.7) 와 **영상 디코드+프레젠테이션** 을 따로 측정해 기록한다.

---

## 9. 에디터 UI 명세 (`YttStudio.App`)

### 9.1 전체 레이아웃

```
┌──────────────────────────────────────────────────────────────────┐
│ 메뉴바  파일 편집 보기 자막 효과 도구 도움말                        │
├───────────────┬──────────────────────────┬───────────────────────┤
│ 스타일 목록    │  [영상][일반][극장][전체][모바일]  ← 뷰포트 모드(M3) │
│               │      미리보기 캔버스       │  속성 패널             │
│  [+] [편집]    │      (영상 + 자막 +       │  ├ 위치 / 정렬         │
│               │       에디터 오버레이)     │  ├ 텍스트 스타일       │
│               │                          │  ├ 엣지 / 그림자       │
│               │                          │  ├ 가라오케 / 효과      │
│               │                          │  └ 고급               │
├───────────────┴──────────────────────────┴───────────────────────┤
│ ◀◀ ▶ ▶▶   00:01:23.456 / 00:04:12.000   [0.5x ▾]                │
├──────────────────────────────────────────────────────────────────┤
│ 타임라인 (Track별 행, 큐 블록 드래그/리사이즈)                       │
├──────────────────────────────────────────────────────────────────┤
│ 자막 목록 그리드 + 가라오케 편집 탭                                 │
├──────────────────────────────────────────────────────────────────┤
│ 상태바: 검증 N건 | 크기 추정 | 모바일 호환성                        │
└──────────────────────────────────────────────────────────────────┘
```

### 9.2 미리보기 캔버스

레이어 순서(아래→위): 영상 프레임 → 자막 렌더 → 세이프 에어리어 → 선택 표시(점선 + 앵커 마커) → 스냅 가이드 → 좌표 툴팁.

| 동작 | 결과 |
|---|---|
| 큐 클릭 / Ctrl+클릭 | 선택 / 다중 선택 토글 |
| 빈 곳 드래그 | 범위 선택 |
| 큐 드래그 | 위치 이동 (`ah`/`av`) |
| Shift+드래그 | 한 축 고정 |
| 방향키 / Shift+방향키 | 1% / 0.1% 이동 (내부만, export 시 반올림) |
| 더블클릭 | 인라인 텍스트 편집 |
| 앵커 마커 클릭 | 앵커 변경 (**박스 화면 위치 유지**하며 `ah`/`av` 재계산) |
| 우클릭 | 정렬, 스타일 적용, 복제, 삭제 |

**⚠️ 회전 핸들 없음. 스케일 핸들 없음.** 크기는 속성 패널의 폰트 크기로만. 모서리를 끄는 UI는 포맷에 없는 자유 스케일을 암시한다.

### 9.3 스냅 & 가이드

드래그 중 스냅 (기본 임계 8px, Alt로 일시 해제): 화면 가로/세로 중앙, 1/3·2/3, 세이프 에어리어 경계(기본 5%), 하단 표준(`av=90`), **동시에 보이는 다른 큐의 앵커·박스 경계.** 스냅 시 해당 가이드선 강조.

### 9.4 정렬 UI ⭐

세 가지가 **서로 다른 개념**이므로 분리해서 노출한다.

**(1) 앵커 포인트 (`ap`) — 박스의 어느 점을 좌표에 못박는가**

```
┌───┬───┬───┐
│ ↖ │ ↑ │ ↗ │   ap 0 1 2
├───┼───┼───┤
│ ← │ ✛ │ → │   ap 3 4 5
├───┼───┼───┤
│ ↙ │ ↓ │ ↘ │   ap 6 7 8
└───┴───┴───┘
```

앵커 변경 시 **박스의 화면상 위치는 유지하고 `ah`/`av`만 재계산.**

**(2) 박스 내부 정렬 (`ju`)** — 3버튼 `왼쪽 / 가운데 / 오른쪽`, 기본 **가운데(2)**.
한 줄 자막에서는 시각적 차이가 없으므로 "여러 줄일 때 적용됨" 힌트 표시.

**(3) 화면 기준 정렬 명령** — 선택된 모든 큐에 적용

| 명령 | 동작 | 단축키 |
|---|---|---|
| 가로 가운데 | `ah = 50` | `Ctrl+Shift+H` |
| 세로 가운데 | `av = 50` | `Ctrl+Shift+V` |
| 화면 정중앙 | `ah=50, av=50, ap=4` | `Ctrl+Shift+C` |
| 좌/우 가장자리 | 세이프 에어리어 기준 | — |
| 하단 표준 | `ah=50, av=90, ap=7` | `Ctrl+Shift+B` |

**(4) 다중 선택 정렬** — 2개 이상 선택 시: 좌/중앙/우 맞춤, 상/중앙/하 맞춤, 가로·세로 균등 분배(3개 이상). **마지막 선택 큐가 기준**(Figma/Illustrator 관례).

**(5) 회전 안내** — 선택 컨텍스트 메뉴 하단에 비활성 항목으로 "회전 — YTT 포맷 미지원" + 도움말 링크. 사용자가 찾다가 헤매지 않게.

### 9.5 속성 패널

접이식. 다중 선택 시 공통값만 표시하고 다른 값은 "—", 편집하면 전체 적용.

```
▼ 위치 & 정렬
    앵커 포인트   [3×3 그리드]
    X (ah) [ 50 ]%  Y (av) [ 90 ]%   [슬라이더]
    내부 정렬     [◧][▣][◨]
    텍스트 방향   [가로쓰기 ▾]

▼ 텍스트
    스타일       [Default ▾]  [스타일로 저장]
    폰트         [기본(Roboto) ▾]        ← §5.4의 8종 고정
    크기         [ 100 ]%  슬라이더 75~200 / 숫자 입력은 그 이상 허용
    [B][I][U]  [위첨자][아래첨자]
    전경색 [■] 투명도 [====|--] 254
    배경색 [■] 투명도 [====|--] 192  [배경 없음 ☑]

▼ 엣지 / 그림자
    종류 (○없음 ○하드섀도 ○베벨 ●글로우 ○소프트섀도)   🖥️전용
    색상 [■]
    [+ 그림자 추가]   ⓘ 줄 복제로 구현, 파일 크기 증가

▼ 가라오케
    미부른 색 [■] 투명도 [==|-----]
    타입 [기본 ▾] (기본/페이드/글리치/커서/좌측커서)
    커서 텍스트 [ ▶ ]

▼ 효과
    [☐ 이동] [☐ 페이드] [☐ 흔들림] [☐ 색수차]
    [+ 애니메이션 추가]

▼ 고급
    루비 [☐] 위치 (●위 ○아래)   🖥️전용
    텍스트 패킹 [☐]              🖥️전용
```

**폰트 크기 범위 (v1.1 정정):**
- **75% 하한은 포맷 제약** `[UPSTREAM]` — 슬라이더·숫자 입력 모두 강제.
- **200% 상한은 UX 선택** `[PRODUCT]` — 슬라이더만 200%까지, 숫자 입력은 초과 허용. 초과 시 `W108` 안내(레이아웃/파일 크기 영향).

**호환성 배지:** 각 컨트롤 옆에 §5.8 기반 지원 여부 아이콘 (`🖥️ 전용` 등). §5.8이 "검증일 있는 관찰"이므로 툴팁에 검증일 표시.

### 9.6 타임라인

- 가로축 = 시간, 세로축 = **Track**(편집 조직용). 추가/삭제 가능.
- 큐 블록: 좌우 끝 드래그로 시작/끝, 몸통 드래그로 이동.
- 재생 헤드 표시 및 스크럽. 줌(Ctrl+휠, 현재 위치 중심 유지).
- 스냅: 다른 큐 경계, 재생 헤드, 프레임 경계(decoder timestamp 기준, §8.5).
- 파형 표시는 **M4**.
- **Track ≠ ZOrder.** 겹치는 큐의 그리기 순서는 별도 컨텍스트 메뉴("맨 앞으로 / 맨 뒤로")로 조정하며, `.ytt` 왕복 미보존을 `I203`으로 알린다.

### 9.7 가라오케 편집기

```
1. 큐 선택 → "가라오케 편집" 탭
2. 텍스트가 음절 칩으로 표시
   [사] [랑] [해] [ ] [영] [원] [히]
   기본 분할: 한글=글자, 일본어=가나(요음 っゃゅょ 결합), 라틴=단어
   칩 사이 클릭으로 분할점 추가/제거, 드래그로 병합
3. 타이밍 입력:
   (a) 탭 모드 — 재생 중 Space로 현재 칩 시작 시각 찍고 다음으로 이동
       0.5x 권장. Backspace로 직전 탭 취소
   (b) 수동 모드 — ms 단위 직접 입력
4. 하단 음절 타임라인 바에서 구간 드래그 미세 조정
5. 미리보기 즉시 반영
```

**제약 처리:** 인접 칩이 같은 오프셋이면 자동으로 1ms 추가(§5.5). UI에서 조용히 처리하되 로그는 남긴다.

### 9.8 자막 목록 그리드

컬럼: `#`, 시작, 끝, 길이, Track, 스타일, 텍스트. 인라인 편집. Enter=다음 줄, Shift+Enter=줄바꿈 삽입. 검색·치환(정규식). 행 선택 ↔ 캔버스 선택 양방향 동기화.

### 9.9 단축키

| 키 | 동작 |
|---|---|
| `Space` | 재생/일시정지 (가라오케 탭에서는 탭 입력) |
| `←` `→` | 1프레임 이동 |
| `Shift+←/→` | 1초 이동 |
| `Ctrl+Z` / `Ctrl+Shift+Z` | Undo / Redo |
| `Ctrl+S` / `Ctrl+Shift+S` | 저장 / 다른 이름으로 |
| `Ctrl+E` | Export |
| `Ctrl+D` / `Delete` | 복제 / 삭제 |
| `Ctrl+Enter` | 현재 시각에 새 큐 |
| `[` `]` | 선택 큐의 시작/끝을 현재 위치로 |
| `Ctrl+Shift+H/V/C/B` | §9.4 정렬 |
| `F5` | 실제 플레이어 프리뷰 갱신 |

---

## 10. Import / Export

### 10.1 지원 매트릭스

| 포맷 | Import | Export | 비고 |
|---|:--:|:--:|---|
| `.yttproj` | ✓ | ✓ | 네이티브 프로젝트 |
| `.ytt` / `.srv3` | ✓ | ✓ | 주 출력 포맷 |
| `.ass` | ✓ | ✓ | Aegisub 왕복 |
| `.srt` | ✓ | ✓ | 텍스트+타이밍만 |
| `.sbv` | ✓ | ✗ | 유튜브 편집기 다운로드 포맷 |
| `.ttml` | ✓ | ✓ | 낮은 우선순위 |

### 10.2 Export 파이프라인

```
SubtitleProject
   → FormatResolver로 모든 Section을 ResolvedFormat으로 해석
   → (어댑터) YTSubConverter.Shared의 SubtitleDocument / Line / Section
   → 효과를 ASS 오버라이드 태그로 (\move, \fad, \t, \ytshake, \ytchroma, \ytkt)
   → AssDocument 조립
   → YttDocument(assDoc).Save(path)      ← §5.9 전처리가 여기서 실행
```

> **`.ytt` XML을 직접 문자열로 조립하지 말 것.** `YttDocument.Save()`에 위임해야 버그 우회가 적용된다. 직접 쓰면 iOS에서 첫 pen 배경이 깨지고, 안드로이드에서 흰 글자가 사라지고, 가라오케 타이밍이 뭉개진다.
>
> **YttStudio는 `<head>` 직렬화 순서·ID 생성·더미 정책을 재구성하지 않는다.** 규범은 pin된 writer다(§5.1). 손으로 만드는 테스트 픽스처도 동일 형태를 따른다.

`.ass` export는 같은 어댑터 결과를 `AssDocument.Save()`로.

### 10.3 Import 파이프라인

```
.ytt / .srv3  → YttDocument(path)  → SubtitleDocument → (어댑터) → SubtitleProject
.ass          → AssDocument(path)  → SubtitleDocument → (어댑터) → SubtitleProject
```

**손실을 반드시 사용자에게 보고한다. 조용히 버리지 말 것.**

| 경로 | 손실 |
|---|---|
| `.ytt` → 프로젝트 | 효과가 이미 키프레임으로 펼쳐져 있어 `CueEffect`로 복원되지 않음. 있는 그대로 큐로 가져오고 "효과 정보 없이 가져옴" 표시 |
| `.ytt` → 프로젝트 | ZOrder/Track 개념 없음(§5.10). `<p>` 등장 순서로만 복원 |
| `.ass` → 프로젝트 | 미지원 태그(`\frz`, `\fscx`, `\fscy`, `\fax`, `\clip` 등)는 **무시하되 경고 목록에 태그명·줄 번호와 함께 표시** |
| `.ass` → 프로젝트 | ASS `Layer`는 `Track`으로 가져오고 `ZOrder`에도 복사 |

**Import는 Undo 스택을 만들지 않는다** (§13, undo-free mutation context).

### 10.4 지원 ASS 오버라이드 태그

`\b` `\i` `\u` `\fn` `\fs` `\c`/`\1c` `\2c` `\3c` `\4c` `\1a` `\2a` `\3a` `\4a` `\alpha` `\pos` `\an` `\k` `\r` `\fad` `\fade` `\move` `\t`
`\ytsub` `\ytsup` `\ytsur` `\ytruby` `\ytvert` `\ytdir` `\ytpack` `\ytshake` `\ytchroma` `\ytkt`

그 외는 무시 + 경고.

---

## 11. 검증기 (`YttStudio.Core/Validation`)

```csharp
public enum IssueSeverity { Info, Warning, Error }
public sealed record ValidationIssue(
    IssueSeverity Severity, string Code, string Message,
    Guid? CueId, bool HasAutoFix);
```

**Error:**

| 코드 | 조건 |
|---|---|
| `E001` | 시작 시각 < 1ms (자동 수정) |
| `E002` | 끝 ≤ 시작 |
| `E003` | 인접 가라오케 섹션 오프셋 동일 (자동 수정: +1ms) |
| `E004` | `fc = #FFFFFF` (자동 수정: `#FEFEFE`) |
| `E005` | 불투명도 = 255 (자동 수정: 254) |
| `E006` | 큐 시각이 영상 길이 초과 |

**Warning:**

| 코드 | 조건 |
|---|---|
| `W101_SIZE_RISK_ESTIMATE` | 크기 추정치가 안전 마진 초과 — **아래 주의 참조** |
| `W102` | 효과 사용량이 많아 모바일에서 자막 선택지에 안 뜰 가능성 `[HEURISTIC]` |
| `W103` | 세이프 에어리어 밖 — 극장 모드에서 화면 밖으로 밀릴 수 있음 |
| `W104` | 어두운 텍스트 — 안드로이드 검은 배경에서 판독 불가 (자동 우회 적용되나 iOS는 여전히 문제) |
| `W105` | 박스 너비가 자막 좌표 공간 폭 초과 — 줄바꿈 필요 |
| `W106` | pen 하나에 그림자 2종 이상 — 줄 복제로 파일 크기 증가 |
| `W107` | 크기 계산 결과가 75% 하한에 걸림 |
| `W108` | 폰트 크기가 UX 권장 상한(200%) 초과 — 레이아웃/크기 영향 |

**Info:** `I201` PC 전용 기능 사용 / `I202` 폰트가 안드로이드에서 무시됨 / `I203` 겹치는 큐의 ZOrder가 `.ytt` 왕복에 보존되지 않음.

### 11.1 ⚠️ W101 — 크기 추정의 한계 (v1.1 정정)

> upstream README는 한계를 **"JSON3로 변환된 YTT 파일의 압축 크기(bit) ÷ 영상 길이(초) > 10240"** 으로 기술한다 `[UPSTREAM]`. 그리고 **upstream 코드베이스에 이 계산을 구현한 estimator가 존재하지 않는다** (`grep -rln "Json3\|10240" --include=*.cs` → 0건).

따라서:

- YttStudio는 **gzip(YTT XML) × 8 ÷ 영상 길이(초)** 를 계산하되, 이것은 **근사치다.**
- 코드 `W101_SIZE_RISK_ESTIMATE`, 메시지에 반드시 명시: *"브라우저의 실제 JSON3 기준과 다른 근사치입니다. 실제 표시 여부는 업로드 후 확인하세요."*
- **안전 마진 70%** 에서 조기 경고 (`0.7 × 10240 = 7168` bit/s).
- "제한 미만이니 안전"이라고 **표현하지 않는다.** 경고 부재를 안전 신호로 오해하게 하면 안 된다.
- 정확한 JSON3 estimator 구현은 `DEFER` — 필요성이 실증되면 별도 항목으로 승격.

**계산 시점:** export를 실제 수행해 XML 생성 후 gzip. 편집 중 상시 계산은 비싸므로 **디바운스 2초** 또는 명시적 "검사" 버튼.

---

## 12. 프로젝트 파일 (`.yttproj`)

- ZIP 컨테이너: `project.json`(System.Text.Json) + `thumbnail.png` + `manifest.json`(스키마 버전).
- 영상은 **경로만 저장.** 깨지면 "영상 다시 찾기" 다이얼로그.
- 마이그레이션 코드는 `Core/Project/Migrations/`. 마이그레이션은 **undo-free context**로 실행.
- 자동 저장: 60초마다 `%TEMP%/YttStudio/autosave/`. 비정상 종료 후 복구 제안. **Undo 스택에 영향 없음.**

---

## 13. Mutation Boundary + Undo / Redo

> **v1.1 강화.** v1.0은 "ViewModel이 모델 직접 수정 시 리뷰 반려"라는 규율에만 의존했다. public setter + TwoWay 바인딩이면 규율은 언젠가 반드시 뚫린다. **컴파일러가 막게 한다.**

```
View
  ↓ binding / gesture
ViewModel
  ↓ intent (BeginDrag / SetForeground / ...)
DocumentEditor          ← Core의 유일한 mutation 진입점
  ↓ IUndoableCommand
Domain Model (internal setter)
```

**규칙:**

- 도메인 프로퍼티는 전부 `internal set`. 컬렉션 변경도 `internal`. `[InternalsVisibleTo]`는 **테스트 어셈블리에만.**
- `DocumentEditor`는 `Core`에 있고 `App`이 참조한다. App은 도메인 setter에 접근할 수 없다.
- **transaction:** drag 시작 → transient preview state, mouse up → 커맨드 1개 commit. 슬라이더도 동일(`BeginTransaction` / `EndTransaction`).
- 다중 선택 변경은 `CompositeCommand` 하나.
- **undo-free mutation context:** import / 프로젝트 로드 / 마이그레이션 / autosave는 스택을 만들지 않는다.
- 스택 깊이 200.

```csharp
public interface IUndoableCommand
{
    string Label { get; }                       // "자막 이동", "색상 변경"
    IReadOnlyCollection<Guid> AffectedCueIds { get; }   // 렌더러/UI invalidation 최적화
    void Execute();
    void Undo();
    bool TryMergeWith(IUndoableCommand previous);
}
```

---

## 14. 실제 유튜브 플레이어 프리뷰

내장 미리보기는 근사치이므로, **정답 확인 경로를 앱 안에 내장한다.** 이게 §7.1 결정을 성립시키는 안전장치다.

### 14.1 ⚠️ 두 가지 역할이 필요하다 (v1.1 정정)

upstream `mitmproxy_script.py`는 두 일을 한다 `[UPSTREAM]`:

1. **`ensure_subtitle_selector`** — HTML 응답의 `ytInitialPlayerResponse`(regex `var ytInitialPlayerResponse = (.+?);var meta = `)를 찾아 표시명 `"Preview"`의 더미 caption track을 주입한다. **자막 트랙이 없는 영상에서는 CC 버튼 자체가 안 뜨므로 이게 없으면 "아무 영상에서나 테스트"가 성립하지 않는다.**
2. **`apply_custom_subtitles`** — `https://www.youtube.com/api/timedtext` 요청을 로컬 파일로 응답.

v1.0은 2번만 기술했다. 어댑터는 **최소 두 기능 모두** 포함해야 한다.

### 14.2 아키텍처

```
YttStudio.App/Preview/
  ├─ IExternalPlayerPreview
  ├─ MitmproxyPreviewAdapter
  └─ Assets/mitmproxy_script.py    ← upstream과 동기화
```

**Core에 넣지 않는다.** YouTube HTML/JS 구조는 언제든 바뀐다.

### 14.3 요구사항

- upstream 스크립트를 가능한 한 직접 참고·동기화. pin 갱신 시 함께 확인(`DEPENDENCIES.md`).
- **DOM/regex가 깨지면 graceful failure** — "실제 프리뷰 사용 불가"로 표시하고 앱은 계속 동작.
- **프리뷰가 실패해도 편집·export는 정상 동작해야 한다.** 의존성을 만들지 말 것.
- **프록시/인증서를 앱이 몰래 변경하지 않는다.** 사용자에게 설정 방법을 안내하고, 종료 시 되돌려야 할 항목을 명확히 표시.
- mitmproxy 바이너리는 번들하지 않는다. 미설치 시 다운로드 링크 안내.
- Fiddler Classic(Windows)은 대안으로 안내만.

### 14.4 흐름

1. 도구 → "실제 플레이어에서 확인" 활성화
2. 현재 프로젝트를 임시 `.ytt`로 export
3. 스크립트 경로를 런타임 치환 후 `mitmdump -s script.py` 실행
4. 프록시 설정(127.0.0.1:8080) + 인증서 설치 안내 표시
5. 유튜브 영상에서 CC를 껐다 켜면 현재 자막 로드
6. `F5` 또는 편집 시 자동으로 임시 `.ytt` 재생성 → 브라우저에서 CC 토글만 하면 즉시 반영

---

## 15. 테스트 전략

### 15.1 Core 테스트

- 모델 → `.ytt` export golden 비교. `external/YTSubConverter/YTSubConverter.Tests/Ass/Files/*.ytt` 를 golden으로 재활용.
- `.ass` → 프로젝트 → `.ass` 왕복 무손실 (지원 태그 한정).
- `.ytt` → 프로젝트 → `.ytt` 왕복 동등성.
- 폰트 크기 왕복: `real → sz → real` 오차 없음.
- 좌표 왕복: `pixel → (ah,av) → pixel` 오차 ≤ 1px @ 1280×720.
- `FormatResolver`: 상속/override 조합 전수.
- 검증기 규칙별 유닛 테스트 (자동 수정 포함).
- `CueCollection.GetActiveAt` / `AdvanceTo`: 경계 시각, 겹침, 대량 큐.

### 15.2 Render 테스트 — 3계층 (v1.1 신설)

> **v1.0의 단일 PNG 스냅샷은 폰트 설치 상태·glyph fallback·rasterizer·OS별 metrics 차이로 흔들린다.**

**(1) Deterministic layout test — 전 플랫폼, 필수**

픽셀이 아니라 숫자를 tolerance로 검증:
- 앵커 스크린 좌표
- 각 줄의 bounds, 박스 bounds
- baseline 위치
- resolved font size
- 섹션 배치 좌표

케이스: `ap` 9종 × `ju` 3종, 다중 줄, 세로쓰기, 루비, 좌표 변환 on/off.

**(2) Raster golden test — 고정 환경에서만**

CI에서 OS / 폰트 패키지 / SkiaSharp 버전을 **고정**한 잡에서만 실행. `et` 4종, 가라오케 진행 3단계, 다국어(한/일/영/아랍어).

**(3) Cross-platform smoke test — Windows/macOS/Linux**

픽셀 일치가 아니라: 크래시 없음 / 글리프 누락 없음 / bounds가 합리적 범위 / export 결과 동일.

### 15.3 수동 QA

`docs/MANUAL_QA.md`에 유지. 최소: 실제 유튜브 업로드 후 PC/iOS/Android 확인, 극장·전체화면 위치, 파일 크기 한계 근처 케이스.

---

## 16. 마일스톤

### M0 — 스캐폴딩 + Compatibility Spike

- [ ] .NET 10 솔루션 생성, 의존 방향 검증
- [ ] YTSubConverter 서브모듈 추가 **+ `b186a40b`로 pin** (`master` 아님)
- [ ] `YTSubConverter.Shared` 프로젝트 참조로 **clean build**
- [ ] Windows / Linux / macOS restore 성공
- [ ] `System.Drawing` 의존 범위 확인 — 어댑터 밖으로 새는지
- [ ] Avalonia 12 + SkiaSharp 버전 정렬 확인
- [ ] libmpv 로드 가능 여부 (3개 OS, 기본 self-contained publish)
- [ ] 실패 시 fallback(.NET 8 + Avalonia 11) 비용 산정 후 `DEPENDENCIES.md`에 기록
- [ ] CI: 빌드 + 테스트
- [ ] `docs/PROGRESS.md` 생성

**완료 조건:** `dotnet build` / `dotnet test` 클린 통과. §3 스택이 `DEPENDENCIES.md`에 확정 기록됨.

---

### M1 — 렌더러 (영상 없이) ⭐ 최우선

> **영상 파이프라인을 먼저 붙이지 말 것.** 렌더러 단독 검증 후 영상을 얹어야 디버깅 대상이 분리된다.

- [ ] §6 도메인 모델 (`internal set` 포함), `CueCollection`, `FormatResolver`
- [ ] `YTSubConverter.Shared` 어댑터 (양방향, `System.Drawing` 격리)
- [ ] `.ytt` / `.ass` import·export (§10), 손실 보고 포함
- [ ] Skia 렌더러: 레이아웃(§7.4) + pen 매핑(§7.5)
- [ ] **`IFontResolver` + 폰트 번들링 (§7.5.1)** — Roboto 번들, Liberation 대체, 미해결 폰트 배지
- [ ] 좌표 변환, 폰트 크기 공식 적용
- [ ] 앵커 9종, 정렬 3종 정확 동작
- [ ] **기준 fixture 세트 작성 후 tolerance 확정** (아래 참조)
- [ ] Layout / raster / smoke 3계층 테스트(§15.2)
- [ ] 최소 Avalonia 창: `.ytt` 열기 → 단색 배경 위 렌더 → 시간 슬라이더 스크럽

**완료 조건 (수치 게이트):**

숫자를 임의로 정하지 않는다. **먼저 기준 fixture를 만들고, 실제 유튜브 플레이어 스크린샷과 대조해 관측된 편차를 기록한 뒤, 그 분포를 근거로 tolerance를 확정한다.** 확정된 값은 이 절에 기록한다.

- [ ] 9개 anchor fixture에서 앵커 스크린 좌표 오차 ≤ (확정 tolerance)
- [ ] 큐 bounding box 위치 오차 ≤ (확정 tolerance)
- [ ] multiline justify regression 통과
- [ ] 폰트 크기 공식 fixture 왕복 오차 0
- [ ] 좌표 왕복 오차 ≤ 1px @ 1280×720
- [ ] 실제 유튜브 스크린샷 비교를 `docs/render-comparison/`에 문서화

---

### M2 — 영상 + 편집 캔버스

- [ ] libmpv Render API 파이프라인 (§8.2 스레딩 하드 규칙 준수)
- [ ] `IVideoSource` + latest-frame-wins 백버퍼 (§8.3, §8.4)
- [ ] **성능 spike (§8.6)** — 3개 해상도 측정, 예산 분리 기록
- [ ] VFR / frame step용 mpv property 조합 확정 → §8.5에 기록
- [ ] SW vs GPU 백엔드 benchmark 후 기본 경로 결정
- [ ] 영상 위 자막 합성, 재생/시크/프레임 이동/속도 조절
- [ ] 큐 선택, 드래그, 다중 선택
- [ ] 앵커 UI, 정렬 UI 전체 (§9.4)
- [ ] 스냅 & 가이드 (§9.3)
- [ ] 속성 패널: 위치/정렬/텍스트/엣지
- [ ] 스타일 프리셋 CRUD + 삭제 시 override 굳히기 (§6.5)
- [ ] `DocumentEditor` + Undo/Redo (§13)
- [ ] 자막 목록 그리드, 타임라인 기본형 (Track / ZOrder 분리)

**완료 조건:** 영상을 열고 마우스만으로 배치·스타일링해서 `.ytt` 저장, 유튜브 업로드 시 의도대로 표시. 성능 spike 수치가 문서화됨.

---

### M3 — 효과 + 뷰포트 + 검증기

- [ ] Move / Fade / Shake / Chroma / Animate 모델 + 속성 UI
- [ ] 각 효과 미리보기 시각화 (§7.6)
- [ ] ASS 태그 생성 및 왕복 테스트
- [ ] 검증기 전체 (§11) + 문제 패널 UI + 자동 수정
- [ ] `W101` 근사치 표현 및 안전 마진 (§11.1)
- [ ] mitmproxy 프리뷰 — **두 역할 모두** (§14.1)
- [ ] **뷰포트 모드 (§7.8)** — 단, 브라우저 기준 fixture 측정 완료 후에만 활성화

**완료 조건:** shake/chroma 자막의 **시작/종료 시각, 이동 경로, 주요 색/크기 전환이 실제 플레이어와 실사용상 동등**하며, 알려진 근사 차이가 `docs/render-comparison/`에 문서화됨. 파일 크기·모바일 경고가 근사치임을 명시한 채로 동작.

---

### M4 — 가라오케

- [ ] 음절 자동 분할 (한글/가나/라틴/한자)
- [ ] 음절 칩 UI, 분할점 편집
- [ ] 탭 타이밍 입력 + 수동 편집
- [ ] 음절 타임라인 바
- [ ] 가라오케 타입 5종
- [ ] 오디오 파형 표시
- [ ] 미리보기 실시간 반영

**완료 조건:** 3분짜리 곡의 가라오케 자막을 앱만으로 처음부터 끝까지 제작 가능.

---

### M5 — 마감 + 배포

- [ ] 루비, 세로쓰기, 첨자, 패킹 UI
- [ ] `.yttproj` 저장/열기, 마이그레이션, 자동 저장/복구
- [ ] 검색·치환, 일괄 시간 이동
- [ ] 설정 화면 (단축키, 스냅, 기본 스타일)
- [ ] 한국어/영어 로컬라이제이션
- [ ] **§18 네이티브 배포 전략 확정 및 구현**
- [ ] 사용자 가이드

---

## 17. 에이전트 작업 규칙

### 17.1 코딩

- C# 14, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`
- `.editorconfig` 준수. 4스페이스. `var`는 우변에서 타입이 명확할 때만.
- public API에 XML 문서 주석.
- **§5의 제약을 구현하는 코드에는 반드시 SPEC 조항 번호와 근거 등급을 주석으로 적는다:**
  ```csharp
  // SPEC §5.7 [UPSTREAM]: fo=255는 업로드 시 속성이 제거되어 시청자 설정이 스타일을 덮어씀.
  // 근거: YttDocument.LimitColors(), docs/YTT-VERIFICATION.md
  opacity = Math.Min(opacity, 254);
  ```
  이유와 등급이 없으면 다음 사람이 "이상한 매직 넘버"라며 지운다. 실제로 2차 리뷰가 그럴 뻔했다.
- 매직 넘버는 `YttConstants`에 모은다.
- MVVM: View 코드비하인드에 로직 금지. 도메인 mutation은 `DocumentEditor`만(§13).

### 17.2 커밋 / PR

- Conventional Commits: `feat(render): implement glow edge type`
- 마일스톤 브랜치: `m0/spike`, `m1/renderer`, `m2/canvas`
- PR에 무엇을·왜. 렌더링 변경은 **before/after 스크린샷 필수**
- `docs/PROGRESS.md` 갱신

### 17.3 하지 말 것

- ❌ `.ytt` XML을 직접 문자열로 조립 → `YttDocument.Save()` 사용 (§10.2)
- ❌ `<head>` 순서 / ID / 더미 정책을 YttStudio가 재구성 (§5.1)
- ❌ 회전 / 자유 스케일 UI 추가 (§2.2)
- ❌ `Core`나 `Render`에 Avalonia 참조 추가
- ❌ 렌더러 픽셀 정확도를 위해 §7.1 결정을 뒤집기
- ❌ import 시 미지원 태그를 조용히 버리기 (§10.3)
- ❌ `ref struct`를 제네릭 인자·이벤트 페이로드로 사용 (§8.3)
- ❌ mpv update callback 안에서 mpv API 호출 (§8.2)
- ❌ 도메인 setter를 `public`으로 열기 (§13)
- ❌ 뷰포트 좌표 동작을 측정 없이 추측 구현 (§7.8)
- ❌ 서브모듈을 `master`로 두기 (`DEPENDENCIES.md`)
- ❌ `ytt.ytt` / DeepWiki / 블로그를 규범의 단독 근거로 사용 (§0.3)

### 17.4 구현 전 차단 조건

아래가 해결되지 않으면 **M1 구현 시작 금지**:

- [x] §5.1 head ordering — v1.1에서 소스 검증 완료
- [x] ID / 더미 정책 — v1.1에서 소스 검증 완료
- [x] YTSubConverter pin — `b186a40b`
- [x] VideoFrame ABI / lifetime — §8.3, §8.4
- [x] Style inheritance model — §6.4, §6.5
- [x] Undo mutation boundary — §13
- [ ] **M0 spike 통과** (§3, `DEPENDENCIES.md`) ← 유일하게 남은 항목

---

## 18. 네이티브 의존성 배포 (M5, feasibility는 M0)

libmpv는 네이티브 의존성이며 별도 배포 설계가 필요하다. M5에서 확정하되 아래 항목을 모두 결정한다.

- **지원 아키텍처:** Windows x64(1순위) / macOS arm64(2순위) / Linux x64(3순위). arm64 Windows, macOS x64(universal 여부), Linux arm64는 미정 — `DEPENDENCIES.md`에 확정 기록.
- **libmpv 제공 방식:** 앱 번들 / 시스템 설치 탐색 / 둘 다 — 결정 후 기록
- **native library probing** 순서와 실패 시 메시지
- **버전 compatibility check** — 최소 버전 미만이면 명확한 안내
- **라이선스 파일 포함** (libmpv는 LGPLv2.1+, 빌드 구성에 따라 GPL)
- self-contained .NET publish와 네이티브 라이브러리의 관계
- macOS codesign / notarization
- Linux AppImage에서 GPU / OpenGL / Wayland / X11 의존성
- Windows GPU 드라이버 / OpenGL 호환성 fallback
- **crash log에 libmpv 버전 기록** — 네이티브 크래시의 대부분이 버전/드라이버 문제

**M0에서 확인할 것:** 3개 OS에서 libmpv 로드가 되는가만.

---

## 19. 참고 자료

| 항목 | 링크 |
|---|---|
| YTSubConverter (MIT, pin `b186a40b`) | https://github.com/arcusmaximus/YTSubConverter |
| SRV3 포맷 문서 ⚠️ 손글씨 문서, 단독 근거 금지 | `external/YTSubConverter/ytt.ytt` |
| mitmproxy 프리뷰 스크립트 | `external/YTSubConverter/mitmproxy_script.py` |
| yttml (Rust 대안 구현, 포맷 연구 참고) | https://github.com/FyraLabs/yttml |
| Aegisub (wangqr 포크) | https://github.com/wangqr/Aegisub |
| ASS 태그 레퍼런스 | https://aegi.vmoe.info/docs/3.0/ASS_Tags/ |
| .NET 지원 정책 | https://learn.microsoft.com/dotnet/core/releases-and-support |
| C# `ref struct` 제약 | https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/ref-struct |
| mpv Render API | https://github.com/mpv-player/mpv/blob/master/include/mpv/render.h |
| Avalonia | https://docs.avaloniaui.net/ |
| SkiaSharp | https://learn.microsoft.com/dotnet/api/skiasharp |
| yt-dlp (`--write-subs --sub-format=srv3`) | https://github.com/yt-dlp/yt-dlp |

---

## 부록 A. 최소 유효 `.ytt` 예시 `[UPSTREAM]`

```xml
<?xml version="1.0" encoding="utf-8"?>
<timedtext format="3">
  <head>
    <wp id="0" ap="7" ah="50" av="90"/>
    <wp id="1" ap="7" ah="50" av="90"/>
    <ws id="0" ju="2" pd="0" sd="0" wfo="0"/>
    <ws id="1" ju="2" pd="0" sd="0" wfo="0"/>
    <pen id="0" fc="#FEFEFE" fo="254"/>
    <pen id="1" fc="#FEFEFE" fo="254" bo="0" ec="#000000" et="3" sz="200" b="1"/>
    <pen id="2" fc="#66CCFF" fo="254" bo="0" ec="#000000" et="3" sz="200" b="1"/>
  </head>
  <body>
    <p t="1000" d="3000" wp="1" ws="1">
      <s p="2" t="0">사랑</s>&#8203;<s p="1" t="500">해</s>
    </p>
  </body>
</timedtext>
```

- 순서 `wp → ws → pen`, 각 풀 `id=0`은 더미 — `WriteHead()` 실제 출력과 일치
- 첫 `<s>` 뒤의 `&#8203;`가 제로폭 공백 워크어라운드
- `sz="200"`은 실제 **125%** (§5.6). 200%가 아니다
- `t="1000"`은 1초, `<s t="500">`은 큐 시작 후 0.5초에 색 전환

> **손으로 만드는 테스트 픽스처는 반드시 이 형태를 따라야 golden 비교가 성립한다.**

## 부록 B. 앵커 포인트 ↔ ASS `\an`

| `ap` | 위치 | ASS `\an` |
|---|---|---|
| 0 | 좌상 | 7 |
| 1 | 중상 | 8 |
| 2 | 우상 | 9 |
| 3 | 좌중 | 4 |
| 4 | 정중앙 | 5 |
| 5 | 우중 | 6 |
| 6 | 좌하 | 1 |
| 7 | 중하 | 2 |
| 8 | 우하 | 3 |

> ASS `\an`은 세로 순서가 하단부터(1~3=하단), `ap`는 상단부터(0~2=상단)로 **반대**다. 변환 시 주의.

## 부록 C. v1.1 검증 요약

| 리뷰 항목 | 판정 | 근거 |
|---|---|---|
| head 순서 wp→ws→pen | **CONFIRMED_KEEP** (리뷰 기각) | `YttDocument.cs:670-694` + 픽스처 |
| 각 풀 id=0 더미 | **CONFIRMED_KEEP** (리뷰 기각) | 동일 위치 코드 주석 |
| `ref struct` + `EventHandler<T>` | CONFIRMED_FIX | C# 언어 제약 |
| .NET 10 / Avalonia 12 | CONFIRMED_FIX | EOL 2026-11-10 / 12.1.1 출시 |
| W101 JSON3 가정 | CONFIRMED_FIX | README + estimator 부재 |
| mitmproxy 2역할 | CONFIRMED_FIX | 스크립트 함수 목록 |
| libmpv 스레딩 | CONFIRMED_FIX | `render.h` Threading |
| StylePreset 상속 | CONFIRMED_FIX | 설계 결함 |
| Undo mutation boundary | CONFIRMED_FIX | 설계 결함 |
| `ObservableCollection` 정렬 | CONFIRMED_FIX | 지킬 수 없는 주석 |
| Layer → Track/ZOrder | CONFIRMED_FIX | `YttDocument`에 Layer 0건 |
| PlayerViewport | PRODUCT_DECISION | 측정 게이트 부여 |
| §5 불변 취급 | PRODUCT_DECISION | 정정 절차 도입 |
| 스냅샷 이식성 | CONFIRMED_FIX (부분) | 3계층 분리 |
| 마일스톤 수치화 | CONFIRMED_FIX (부분) | fixture 후 tolerance 확정 |
| 폰트 크기 상한 | CONFIRMED_FIX | 상한 clamp 부재 |
| 의존성 pin | CONFIRMED_FIX | `DEPENDENCIES.md` |
| VFR | DEFER → M2 | property 조합 미정 |
| 네이티브 배포 | DEFER → M5 | §18 항목화 |
| **§5.9 파이프라인 구조** (리뷰에 없음) | CONFIRMED_FIX | `Save()` 실제 코드 |

전체 근거: `docs/YTT-VERIFICATION.md` / 변경 내역: `docs/SPEC-v1.1-CHANGELOG.md`
