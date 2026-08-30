<div align="center">

<img src="docs/assets/logo.png" width="112" alt="yttStudio" />

# yttStudio

**유튜브 YTT(SRV3) 자막 전용 WYSIWYG 에디터**

영상 위에서 직접 배치하고 스타일링해 `.ytt` 를 바로 뽑는 데스크톱 앱

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-8B44AC)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![Tests](https://img.shields.io/badge/tests-multi--platform%20CI-brightgreen)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/bd1f9a95330940aeab504f29f2e57d1a)](https://app.codacy.com/gh/DO0OG/yttStudio/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade)

**한국어** · [English](README.en.md) · [日本語](README.ja.md)

</div>

---

유튜브 자막 편집기는 스타일링을 지원하지 않습니다. 하지만 플레이어 자체는
**YTT(YouTube Timed Text, 내부 명칭 SRV3)** 라는 XML 포맷으로 색상·외곽선·글로우·
위치 지정·가라오케 타이밍·루비·세로쓰기를 이미 지원합니다. 기존 생태계에는
**변환기**는 있어도 **영상 위에서 직접 배치하는 편집기가 없었습니다.** yttStudio 는
그 자리를 채웁니다.

<div align="center">
<img src="docs/render-comparison/m2-canvas.png" width="880" alt="편집 캔버스" />
</div>

<div align="center"><sub>
프리뷰 위에서 자막을 그 자리에서 편집합니다. 왼쪽은 스타일 프리셋, 오른쪽은 속성
패널, 아래는 트랙 타임라인과 자막 목록입니다.
</sub></div>

## 주요 기능

| | |
|---|---|
| **영상 위 편집** | 재생하면서 자막을 드래그·다중 선택·스냅 정렬하고, 그 자리에서 텍스트를 고칩니다 |
| **유튜브 주소로 열기** | 주소만 붙여 넣으면 내려받지 않고 스트리밍으로 프리뷰가 뜹니다 |
| **타임라인** | 확대·좌우 팬, 블록 이동과 끝 트림. 패널 크기는 경계를 끌어 조절 |
| **포맷 규칙 강제** | 좌표 변환, 폰트 배율 압축, 불투명도 상한 등 YTT 제약을 코드에서 검사 |
| **가라오케** | 음절 자동 분할(한글·가나·라틴·한자), 재생 중 탭 입력, 5가지 진행 방식 |
| **효과** | 이동 · 페이드 · 흔들림 · 색수차 · 애니메이션. 흔들림은 결정적이라 되감아도 같은 결과 |
| **검증기** | 유튜브 제약 17개 규칙 검사와 되돌릴 수 있는 자동 수정 |
| **왕복 변환** | `.ytt` / `.srv3` / `.ass` 가져오기·내보내기, 손실은 경고로 명시 |
| **프로젝트** | `.yttproj` 패키지, 자동 저장(간격 조절 가능), 비정상 종료 복구 |
| **뷰포트 모드** | 일반 · 극장 · 전체화면 · 모바일. 유튜브 플레이어 실측값 기반 |
| **환경설정** | 언어 · 테마 · libmpv 경로 · 자동 저장을 창에서 설정하고 다음 실행까지 유지 |
| **3개 언어** | 한국어 · English · 日本語 실시간 전환 |

`Space` 로 재생·일시정지를 전환합니다. 창에 `.ytt` / `.srv3` / `.ass` 자막과
`.mp4` / `.mkv` / `.webm` / `.mov` / `.avi` / `.m4v` 영상을 드롭해 바로 열 수 있습니다.

조작법 전체는 [사용자 가이드](docs/USER_GUIDE.md)에 있습니다.

## 최근 변경점

| | |
|---|---|
| **v0.2.4 YouTube 재생 핫픽스** | 현재 yt-dlp의 YouTube JavaScript challenge 처리를 위해 Deno 2.3+를 사용합니다. 호환 Deno가 없으면 공식 Deno v2.9.6 자산을 검증해 사용자 영역에 자동 설치하고 yt-dlp와 libmpv 재생 경로가 함께 사용합니다 |
| **접근 거부 오류 분리** | HTTP 403·429·bot challenge를 네트워크 단절로 오인하지 않고 별도 접근 거부로 안내합니다 |
| **v0.2.3 영상 엔진 기본화** | libmpv가 없으면 첫 영상 열기에서 검증된 LGPL 런타임을 프로그램이 직접 설치합니다. YouTube 주소를 처음 열 때 yt-dlp가 없으면 공식 릴리스 자산을 검증해 설치합니다 |
| **재생 단축키 정리** | 창 어디에 포커스가 있어도 `Space` 가 재생·일시정지로 동작합니다. 프레임 이동 버튼은 `⏮` `⏭` 로 바꿨습니다 |
| **뷰포트 모드** | 유튜브 플레이어를 실측해 일반·극장·전체화면·모바일 비율을 재현합니다 |
| **설치 파일 배포** | Windows 설치 관리자, macOS `.dmg`, Linux AppImage |
| **환경설정 창과 테마** | 라이트·다크·시스템 기본을 재시작 없이 전환합니다 |

## 시작하기

[최신 릴리즈](https://github.com/DO0OG/yttStudio/releases/latest)에서 내려받으세요.
.NET 런타임이 함께 들어 있어 따로 설치할 필요가 없습니다.

| 플랫폼 | 파일 | 설치 방법 |
|---|---|---|
| Windows | `yttStudio-v*-win-x64-setup.exe` | 실행하면 시작 메뉴 등록, 제거 프로그램, 파일 연결까지 처리합니다 |
| Windows (설치 없이) | `yttStudio-v*-win-x64.zip` | 압축을 풀고 `YttStudio.App.exe` 실행 |
| macOS (Apple Silicon) | `yttStudio-v*-osx-arm64.dmg` | 열어서 `yttStudio.app` 을 `Applications` 로 끌어 놓기 |
| Linux | `yttStudio-v*-linux-x86_64.AppImage` | `chmod +x` 후 실행 |

> **코드 서명이 없는 배포물입니다.** 인증서가 없어 첫 실행에 경고가 뜹니다.
> Windows 는 SmartScreen 경고에서 **추가 정보 → 실행**, macOS 는 `yttStudio.app` 을
> 우클릭해 **열기** 를 고르세요.

### 영상 재생 런타임

영상 재생은 기본 기능입니다. 지원 플랫폼에서는 별도 수동 설치를 선행할 필요가 없습니다.

| 항목 | v0.2.4 동작 |
|---|---|
| libmpv 2.0 이상 | 로컬/YouTube 영상 재생에 필요합니다. 기존에 지정했거나 시스템에서 찾은 호환 라이브러리를 우선 사용하고, 없으면 첫 영상 열기에서 지원 플랫폼용 **검증된 LGPL 런타임**을 사용자 영역에 자동 설치합니다 |
| yt-dlp | YouTube 주소 해석에 필요합니다. 기존 설치본을 우선 사용하고, 없으면 첫 YouTube 주소 열기에서 공식 `yt-dlp/yt-dlp 2026.08.19` 자산을 SHA-256 검증 후 사용자 영역에 자동 설치합니다 |
| Deno 2.3 이상 | 현재 yt-dlp의 YouTube JavaScript challenge 해결에 필요합니다. 호환 설치본을 찾지 못하면 공식 `denoland/deno v2.9.6` 자산을 파일 크기와 SHA-256으로 검증해 자동 설치합니다 |

현재 내부 libmpv 설치 대상은 Windows x64의 `zhongfly/mpv-winbuild` LGPL 전용 빌드와 macOS arm64/Linux x64의 `Shusek/KMediaMpv` 검증 런타임입니다. yt-dlp와 Deno도 각각 공식 upstream 자산만 사용합니다. 이 외부 런타임들은 yttStudio 릴리스 ZIP/설치 파일/DMG/AppImage 안에는 직접 번들하지 않습니다. **도구 → 설정 → 영상**에서 libmpv를 재설치하거나 직접 경로를 지정할 수도 있습니다. 자세한 pin·해시·라이선스 경계는 [의존성 문서](docs/DEPENDENCIES.md)와 [제3자 고지](docs/THIRD-PARTY-NOTICES.md)에 있습니다.

### 소스에서 빌드

.NET 10 SDK 가 필요합니다.

```bash
git clone --recursive https://github.com/DO0OG/yttStudio.git
cd yttStudio
dotnet build -c Release
dotnet run --project src/YttStudio.App
```

자막 파일을 인자로 넘기면 바로 열립니다.

```bash
dotnet run --project src/YttStudio.App -- samples/showcase.ass
```

## 알아두면 좋은 것

- **미리보기는 편집용 근사치입니다.** 유튜브의 실제 렌더러는 브라우저 DOM/CSS
  기반이라 글로우 반경이나 줄바꿈 지점이 미세하게 다릅니다. 최종 확인은 실제
  업로드로 하세요.
- **작업 파일은 `.yttproj` 로 저장하세요.** `.ytt` 는 효과가 키프레임으로 펼쳐진
  결과물이라 되읽어도 효과로 복원되지 않습니다.
- **회전과 자유 스케일 핸들이 없는 것은 의도된 설계입니다.** YTT 포맷에 회전이나
  임의의 박스 스케일이 없습니다. 리사이즈 핸들은 드래그를 `SizePercent` 폰트
  배율로 변환합니다.
- **전체화면과 모바일 세로 뷰포트는 아직 실측하지 못했습니다.** 일반 모드의
  비례식을 그대로 씁니다. 측정 기록은 [뷰포트 모드](docs/viewport-modes.md)에 있습니다.

## 문서

| 문서 | 내용 |
|---|---|
| [사용자 가이드](docs/USER_GUIDE.md) | 기능별 사용법과 제약 |
| [의존성](docs/DEPENDENCIES.md) | 고정 버전, 배포 전략, 로컬 패치 |
| [성능](docs/PERFORMANCE.md) | 해상도별 실측치와 백엔드 결정 근거 |
| [포맷 검증 기록](docs/YTT-VERIFICATION.md) | YTT 규칙의 근거와 확실성 등급 |
| [수동 QA](docs/MANUAL_QA.md) | 자동화할 수 없는 검증 항목 |
| [제3자 고지](docs/THIRD-PARTY-NOTICES.md) | 포함된 폰트와 라이브러리 |

## 기술 스택

.NET 10 · C# 14 · Avalonia 12 · SkiaSharp · libmpv · xUnit

자막 포맷 입출력은 [YTSubConverter](https://github.com/arcusmaximus/YTSubConverter)(MIT)를
사용합니다. `ap` 와 `ju` 를 독립적으로 다루기 위한 로컬 패치가 하나 적용되어 있습니다.

## 라이선스

yttStudio 자체 소스 코드는 별도 표기가 없는 한 [MIT License](LICENSE)로 배포됩니다.
번들 폰트와 외부 라이브러리·도구는 각각의 라이선스를 따르며, 자세한 고지는
[제3자 고지](docs/THIRD-PARTY-NOTICES.md)에 있습니다.

## 상표 및 비제휴 고지

yttStudio는 독립적인 오픈소스 프로젝트이며 YouTube 또는 Google LLC와 제휴하거나,
그들의 승인·후원으로 제공되는 공식 제품이 아닙니다. YouTube는 Google LLC의 상표입니다.

## 기여자

- [DO0OG](https://github.com/DO0OG)
