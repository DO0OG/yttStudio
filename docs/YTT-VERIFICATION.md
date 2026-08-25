# YTT-VERIFICATION.md

YTT/SRV3 비공개 포맷 규칙의 근거 기록.

- **검증일:** 2026-08-24
- **검증 방법:** `git clone --depth 1 https://github.com/arcusmaximus/YTSubConverter.git` 후 소스·픽스처 직접 대조
- **pin된 commit:** `b186a40bc21e58a8c9651cf616cbb5e80425dfc6` (2026-07-27, "Merge pull request #144 from s0hv/patch-1")

## 근거 수준 정의

| 등급 | 의미 |
|---|---|
| `[UPSTREAM]` | pin된 YTSubConverter 소스코드·테스트 픽스처·`ytt.ytt`에서 직접 확인 |
| `[EMPIRICAL]` | 실제 YouTube 업로드 또는 실제 플레이어에서 재현 확인 (**아직 미수행 항목 있음 — 표시함**) |
| `[API]` | Microsoft / mpv / Avalonia 공식 문서로 확인 |
| `[HEURISTIC]` | 완전한 규칙이 아니라 안전측 추정 |
| `[PRODUCT]` | YttStudio 자체 설계 선택. 포맷 제약 아님 |

---

## V-01. `<head>` 직렬화 순서 = wp → ws → pen

**결론: `[UPSTREAM]` 확정. SPEC v1.0이 옳았고, 2차 리뷰의 지적은 틀렸다.**

`YTSubConverter.Shared/Formats/YttDocument.cs:670-694`:

```csharp
private void WriteHead(XmlWriter writer, ...)
{
    writer.WriteStartElement("head");

    // The iOS app ignores the background color for the first pen and might have other,
    // similar bugs too, so we write a dummy (unused) item for each of the lists.
    WriteWindowPosition(writer, 0, new Line(TimeBase, TimeBase) { Position = new PointF() });
    foreach (KeyValuePair<Line, int> position in positions)
        WriteWindowPosition(writer, position.Value, position.Key);

    WriteWindowStyle(writer, 0, new Line(TimeBase, TimeBase));
    foreach (KeyValuePair<Line, int> windowStyle in windowStyles)
        WriteWindowStyle(writer, windowStyle.Value, windowStyle.Key);

    WritePen(writer, 0, new Section());
    foreach (KeyValuePair<Section, int> pen in pens)
        WritePen(writer, pen.Value, pen.Key);

    writer.WriteEndElement();
}
```

테스트 픽스처 `YTSubConverter.Tests/Ass/Files/Karaoke.ytt`의 실제 요소 타입 등장 순서:

```
['wp', 'ws', 'pen']
첫 6개: wp0 wp1 wp2 wp3 wp4 wp5 ... 마지막 3개: pen371 pen372 pen373
```

**리뷰가 왜 틀렸는가:** 리뷰는 `ytt.ytt`의 순서(pen → ws → wp)를 writer 출력이라고 오인했다. `ytt.ytt`는 **포맷 기능을 설명하기 위해 손으로 쓴 문서 파일**이지 converter 출력이 아니다. 파일 상단이 `<!-- Text styles -->` 주석으로 시작하는 교육용 구성이다. DeepWiki의 산문 설명(wp→ws→pen)이 우연히 writer와 일치했고, 리뷰는 그 산문을 의심하면서 정작 검증 대상을 잘못 골랐다.

**ID 증가 규칙의 정확한 범위:** `ytt.ytt:4-5` 주석은 이렇게 말한다.

> The elements in the `<head>` *must* appear in order of increasing ID's or YouTube will renumber them, causing them to be assigned to the wrong `<body>` elements.

문자 그대로 읽으면 `<head>` 전체에서 ID가 증가해야 하지만, 실제 writer 출력은 `wp0..wpN, ws0..wsM, pen0..penK`로 **각 풀마다 0부터 다시 시작**한다. 즉 전역 증가가 아니다. 따라서 이 규칙은 **타입별 풀 내부 규칙**으로 읽어야 하며, v1.0 SPEC의 기술("각 풀 내에서 id는 엄격히 증가")이 upstream 주석보다 정확하다.

**미검증:** 순서를 어겼을 때 실제로 YouTube가 재번호를 매기는지는 `[EMPIRICAL]` 미확인. upstream 주석에만 근거한다. 그러나 YttStudio는 writer에 위임하므로 실무상 위험 없음.

---

## V-02. 각 풀의 `id=0` 더미 항목

**결론: `[UPSTREAM]` 확정. SPEC v1.0이 옳았고, 2차 리뷰의 지적은 틀렸다.**

V-01에 인용한 코드 주석이 명시적이다: *"we write a dummy (unused) item for **each of the lists**"*. `WriteWindowPosition(writer, 0, ...)`, `WriteWindowStyle(writer, 0, ...)`, `WritePen(writer, 0, ...)` 세 번 모두 호출된다.

**리뷰가 왜 틀렸는가:** V-01과 동일한 원인. `ytt.ytt`에서 pen이 id=1부터, ws가 id=1부터 시작하는 것을 보고 "pool마다 dummy가 아니다"라고 결론냈으나, 그건 문서 파일이지 writer 출력이 아니다.

**부작용 주의:** 이 규칙은 YttStudio가 지킬 필요가 없다. `YttDocument.Save()`가 알아서 한다. 다만 **테스트에서 손으로 만든 `.ytt` 픽스처**를 쓸 때는 동일한 형태여야 golden 비교가 성립한다.

---

## V-03. 좌표 변환 `effectiveCoord = specified × 0.96 + 2`

**결론: `[UPSTREAM]` 확정.**

`YttDocument.cs:878-898`:

```csharp
private static int GetYouTubeCoord(float pixelCoord, float maxValue)
{
    float percentage = pixelCoord / maxValue * 100;
    percentage = (percentage - 2) / 0.96f;      // 역변환
    percentage = Math.Max(percentage, 0);
    percentage = Math.Min(percentage, 100);
    return (int)Math.Round(percentage);
}

private static float GetPixelCoord(int youtubeCoord, float maxValue)
{
    return (2 + youtubeCoord * 0.96f) / 100 * maxValue;   // 정변환
}
```

`ReferenceVideoDimensions = new(1280, 720)` (`YttDocument.cs:18`) 도 확인.

---

## V-04. 폰트 크기 압축 공식

**결론: `[UPSTREAM]` 확정.**

`YttDocument.cs:900-914`, 주석 포함:

```csharp
// Similar to positions, YouTube refuses to simply take the specified font scale percentage and apply it.
// Instead, they do realScale = 1 + (yttScale - 1) / 4, meaning that specifying a 200% scale
// results in only 125% and that you can't go lower than an actual scale of 75% (yttScale = 0).
private static float GetRealFontScale(int yttScale)
    => 1 + ((yttScale / 100f) - 1) / 4;

private static int GetYouTubeFontScale(float realScale)
    => (int)Math.Round(Math.Max(1 + (realScale - 1) * 4, 0) * 100);
```

**상한 없음:** `GetYouTubeFontScale`은 하한만 `Math.Max(..., 0)`으로 clamp하고 상한 clamp가 없다. 따라서 **200%는 포맷 한계가 아니라 UX 선택**이다. 2차 리뷰 항목 17의 지적은 옳다.

---

## V-05. `Save()` 전처리 파이프라인 실제 구성

**결론: `[UPSTREAM]` 확정. SPEC v1.0의 목록은 항목은 맞았으나 구조가 부정확했다.**

`YttDocument.cs:92-113` 실제 코드:

```csharp
public override void Save(TextWriter textWriter)
{
    CloseGaps();
    MergeSimultaneousLines();
    MergeIdenticallyFormattedSections();
    ApplyEnhancements();                    // ← 줄 단위 보정들이 이 안에 묶여 있음
    MergeIdenticallyFormattedSections();    // ← 두 번째 호출

    Dictionary<Line, int> positions   = ExtractAttributes(Lines, new LinePositionComparer(this));
    Dictionary<Line, int> windowStyles = ExtractAttributes(Lines, new LineAlignmentComparer());
    Dictionary<Section, int> pens      = ExtractAttributes(Lines.SelectMany(l => l.Sections),
                                                           new NormalizedSectionFormatComparer());

    // Use LF instead of CRLF as the latter reportedly causes the iOS app to bug out
    using XmlWriter writer = XmlWriter.Create(textWriter,
        new XmlWriterSettings { NewLineChars = "\n", CloseOutput = false });
    ...
}
```

정정 사항:
- `CloseGaps()`와 `MergeSimultaneousLines()`는 **base class(`SubtitleDocument`) 소속**이며 `YttDocument`의 private 메서드가 아니다.
- 줄 단위 보정(`AddItalicPrefetch`, `MakeInvisibleTextBlack`, `PreventShadowClipping`, `HardenSpaces`, `LimitColors`, `ExpandLineForMultiShadows`, `ExpandLineForDarkText`, `ApplyManualLinePadding`, `AvoidZeroDurationKaraoke`)은 모두 `ApplyEnhancements()` 안에서 실행된다 — v1.0처럼 평평한 12단계 목록이 아니다.
- `MergeIdenticallyFormattedSections()`는 **두 번** 호출된다.
- `ExtractAttributes()`는 파이프라인 단계가 아니라 write 직전 별도 호출이다.
- LF 강제는 `XmlWriterSettings.NewLineChars = "\n"`로 확인.

---

## V-06. `MergeSimultaneousLines()`와 ASS Layer

**결론: `[UPSTREAM]` — YTT에 layer/z-order 개념이 없음.**

`grep -rn "Layer" YTSubConverter.Shared/` 결과:

- `Ass/AssDialogue.cs` — ASS Dialogue 줄의 `Layer` 필드 읽기
- `Ass/AssDocument.cs:222` — import 시 `Layer = dialogue.Layer` 보존, `:678` — export 시 다시 씀
- `Ass/VisualizingAssDocument.cs` — `--visual` 변환에서 그림자/배경 복제본에 layer를 새로 할당(`AssignLayer`)

**`YttDocument.cs`에는 `Layer` 참조가 단 한 곳도 없다.** 즉 Layer는 ASS 왕복에서만 살아남고, `.ytt`로 나갈 때 소실된다. YTT의 그리기 순서는 `<body>` 안 `<p>` 등장 순서로만 결정된다.

→ SPEC의 `Cue.Layer`를 "Track(편집 조직)"과 "ZOrder(그리기 순서)"로 분리해야 한다는 리뷰 항목 11의 지적은 **옳다**.

---

## V-07. 파일 크기 한계 (W101)

**결론: `[UPSTREAM]` — 한계는 실재하나, upstream에 estimator 구현은 없다.**

`README.md:194`:

> On browsers, YTT subtitles will not display if the compressed file size **in bits of the JSON3 converted YTT file** divided by the total seconds runtime of the video is larger than 10240.

`grep -rln "Json3\|json3\|10240" --include=*.cs .` → **`.cs` 파일 히트 0건.** README에만 존재한다.

따라서:
- 측정 대상은 **JSON3로 변환된 파일의 압축 크기**이지 YTT XML의 gzip 크기가 아니다.
- upstream에 재사용할 수 있는 estimator 코드가 없다.
- SPEC v1.0의 "gzip(XML) × 8 ÷ 초"는 **정확한 측정이 아니라 근사치**로 표현해야 한다. 리뷰 항목 5의 지적은 **옳다**.

---

## V-08. mitmproxy 프리뷰 스크립트의 두 가지 역할

**결론: `[UPSTREAM]` — 리뷰 항목 6의 지적은 옳다.**

`mitmproxy_script.py` 함수 목록:

```
line  50: def read_subtitle_file() -> bytes
line  66: def generate_dummy_captions(video_id: str) -> dict[str, Any]
line  74:     "text": "Preview"
line  92: def ensure_subtitle_selector(flow) -> None
line  97:     match = re.search(r"var ytInitialPlayerResponse = (.+?);var meta = ", html)
line 111: def apply_custom_subtitles(flow) -> None
line 112:     if not flow.request.url.startswith("https://www.youtube.com/api/timedtext"):
line 127: def response(flow) -> None
line 130: def request(flow) -> None
```

두 역할:
1. `ensure_subtitle_selector` — 자막 트랙이 없는 영상의 HTML 응답에서 `ytInitialPlayerResponse`를 찾아 `"Preview"`라는 이름의 더미 caption track을 주입한다. 이게 없으면 자막 없는 영상에서는 CC 버튼 자체가 안 뜬다.
2. `apply_custom_subtitles` — `/api/timedtext` 요청을 로컬 파일로 응답한다.

SPEC v1.0 §14는 2번만 기술했다. **정정 필요.**

`ytInitialPlayerResponse` regex는 YouTube의 HTML/JS 구조에 의존하므로 언제든 깨질 수 있다 → graceful failure 필수.

---

## V-09. YTSubConverter.Shared의 target framework

**결론: `[UPSTREAM]` — .NET 10 전환에 걸림돌 없음.**

```
YTSubConverter.Shared/YTSubConverter.Shared.csproj  →  netstandard2.0
YTSubConverter.UI.Linux/*.csproj                    →  net10.0
YTSubConverter.UI.Mac/*.csproj                      →  net10.0-macos
YTSubConverter.Tests/*.csproj                       →  v4.8 (레거시 .NET Framework)
```

핵심 두 가지:
1. `Shared`가 `netstandard2.0`이므로 .NET 8이든 10이든 **양쪽 다 참조 가능**하다. 리뷰가 우려한 호환성 리스크는 이 의존성에는 없다.
2. **upstream UI 프로젝트는 이미 `net10.0`으로 이주했다.** YttStudio가 .NET 8을 고르면 오히려 upstream보다 뒤처진다.

### System.Drawing 사용 실태 (2026-08-24 추가 검증)

`Shared`가 실제로 참조하는 `System.Drawing` 타입 전수 조사:

```
164 Color   46 PointF   22 SizeF   9 Size   6 Point
Bitmap / Graphics / Brush / Pen / Image / Icon  →  0건
.csproj PackageReference                        →  0개
```

사용 타입은 전부 **`System.Drawing.Primitives`** 소속이며 이는 공유 프레임워크의 일부로 모든 플랫폼에서 동작한다. Windows 전용인 것은 `System.Drawing.Common`(Bitmap/Graphics 계열)인데 `Shared`는 참조하지 않는다.

**결론: .NET 10 크로스플랫폼 전환에 `System.Drawing` 관련 걸림돌 없음.** SPEC v1.1 초안의 경고는 과했으며 정정했다.

어댑터 레이어(§6.4)는 유지한다 — 크로스플랫폼이 아니라 도메인 모델을 외부 라이브러리 타입에서 분리하기 위해서다.

---

## V-10. `ref struct`의 제네릭 인자 제한

**결론: `[API]` 확정. 리뷰 항목 3의 지적은 옳다. SPEC v1.0은 컴파일되지 않는 코드였다.**

- C# 13 이전: `ref struct`는 제네릭 타입 인자로 사용 불가.
- C# 13 이후: 제네릭 파라미터 쪽에 `allows ref struct` anti-constraint가 선언된 경우에만 가능.
- BCL의 `public delegate void EventHandler<TEventArgs>(object? sender, TEventArgs e);` 에는 `allows ref struct`가 **없다.**

따라서 `event EventHandler<VideoFrame> FrameReady;` (VideoFrame이 `ref struct`)는 어떤 C# 버전에서도 컴파일되지 않는다. 추가로 `ReadOnlySpan<byte>`를 이벤트 경계·UI 디스패처 경계로 넘기는 것은 수명 관리가 불가능하다.

---

## V-11. libmpv render API 스레딩

**결론: `[API]` 확정. 리뷰 항목 7의 지적은 옳다.**

`libmpv/render.h` "Threading" 섹션 원문 요지:

- rendering을 일반 libmpv 사용 스레드와 **분리할 것을 권장**
- `mpv_render_*` 함수는 어느 스레드에서든 호출 가능하나, **동시에 하나만**, 그리고 `mpv_set_wakeup_callback()` / `mpv_render_context_set_update_callback()` 콜백 **내부에서는 절대 호출 불가**
- update callback에 대해: *"you must not call any mpv API from the callback"*
- *"there must be no lock or wait dependency from the render thread to a thread using other libmpv functions"* — 위반 시 deadlock
- OpenGL 백엔드: GL context가 호출 스레드에서 current여야 하고 render context 생성 시와 동일해야 함. 아니면 undefined behavior

SPEC v1.0 §8은 이 규칙을 전혀 기술하지 않았다. **정정 필요.**

---

## V-12. 플랫폼 호환성 표의 지위

**결론: `[UPSTREAM]` + 유효기간 있음.**

§5.8 호환성 표는 `ytt.ytt:42-67`, `:88-93` 주석과 README에 근거한다. 그러나 iOS/Android 유튜브 앱은 업데이트로 동작이 바뀔 수 있으므로 **영구 규격이 아니라 "2026-08-24 시점의 관찰"** 로 취급한다. 리뷰 §24의 권고를 수용한다.

재검증이 필요한 시점:
- YttStudio가 호환성 경고를 근거로 사용자 선택을 제약할 때
- 사용자로부터 "경고와 실제가 다르다"는 리포트가 올 때
- YTSubConverter pin을 갱신할 때

---

## V-13. `<wp ap>`와 `<ws ju>` 독립성의 upstream 모델 손실

**결론: 해결됨. 포크 `DO0OG/YTSubConverter`의 `yttstudio/independent-justification` 브랜치 커밋 `c460cca`에서 SPEC §5.3의 독립 조합을 보존하도록 수정했다.**

- `Line`에 nullable `int? Justification`을 추가했다. 값이 `null`이면 이전처럼 `AnchorPoint`에서 실효 justification을 파생한다.
- `Line.Assign()`이 `Justification`을 보존하므로 복사 생성자와 `Clone()` 경로에서도 값이 유지된다.
- `YttDocument.ReadWindowStyle()`은 `ju`를 읽어 `Line.Justification`에 저장한다.
- `YttDocument.ReadLine()`은 window style의 `Justification`을 실제 subtitle line으로 전달한다.
- `YttDocument.WriteWindowStyle()`은 `GetEffectiveJustificationId(style)`로 명시된 `Justification`을 우선 기록한다.
- `LineAlignmentComparer`는 실효 justification을 동등성과 해시 계산에 포함한다.

M1 adapter는 export 시 도메인 `Cue.Justify`를 `Line.Justification`에 설정하고, import 시 별도 XML 보조 파싱 없이 해당 값을 직접 사용한다. 값이 `null`인 기존 입력은 `AnchorPoint`에서 정렬을 파생해 이전 동작을 유지한다.

**실제 조치:** pin을 커밋 `c460cca`로 교체하고, `ap=7`(하단 중앙)과 `ju=0`(왼쪽 정렬)을 저장한 뒤 다시 읽어 두 값이 독립적으로 보존되는 왕복 테스트를 추가했다. export는 계속 `YttDocument.Save()`에 위임한다.

---

## 미검증 항목 (`[EMPIRICAL]` 필요)

아래는 upstream 주석·README에만 근거하며 실제 재현 테스트를 하지 않았다. M1~M3 진행 중 실제 업로드로 확인하고 이 문서를 갱신할 것.

| # | 항목 | 근거 현황 |
|---|---|---|
| E-1 | head ID 순서 위반 시 실제 재번호 발생 | upstream 주석만 |
| E-2 | 각 풀 id=0 더미가 iOS에서 실제로 필요한지 | upstream 주석만 |
| E-3 | `fo`/`bo` = 255일 때 실제로 속성이 제거되는지 | upstream 주석만 |
| E-4 | `fc="#FFFFFF"`의 Android 색 상속 | upstream 주석만 |
| E-5 | 제로폭 공백 없을 때 첫 `<s>`의 `p` 제거 | upstream 코드 우회 존재 |
| E-6 | JSON3 압축 10240 bit/s 한계의 정확한 측정법 | README 서술만, 코드 없음 |
| E-7 | 모바일에서 효과 과다 사용 시 자막 선택지 미표시 조건 | README 서술만, 정량 기준 없음 |
| E-8 | 일반/극장/전체화면 모드의 실제 좌표 동작 | 미측정 — §7.8 PlayerViewport 구현 전 필수 |
