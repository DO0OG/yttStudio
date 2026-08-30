# THIRD-PARTY-NOTICES

> **문서 기준:** v0.2.3 (2026-08-30)

yttStudio 자체 소스 코드는 별도 표기가 없는 한 MIT License로 배포된다. 제3자
라이브러리, 폰트, 실행 파일 및 선택적 네이티브 구성 요소는 각각의 원래 라이선스를
따르며 yttStudio의 MIT License로 재라이선스되지 않는다.

## 릴리스 패키지에 포함되는 고지

릴리스 워크플로는 최종 패키지에 다음 payload를 넣고, 하나라도 누락되면 릴리스를
실패시킨다.

```text
LICENSE
THIRD-PARTY-NOTICES.txt
licenses/
  fonts/
    LICENSE-Roboto.txt
    LICENSE-Liberation.txt
  YTSubConverter/
    LICENSE.txt
```

현재 yttStudio 릴리스 패키지에는 `yt-dlp` 또는 `libmpv` 바이너리를 직접 포함하지
않는 것을 원칙으로 한다.

## 구성 요소

| 구성 요소 | 사용 방식 | 라이선스/상태 | 배포 정책 |
|---|---|---|---|
| yttStudio | 본체 | MIT | 루트 `LICENSE` 포함 |
| YTSubConverter.Shared | git submodule / 소스 참조 | MIT | 원본 MIT 고지 포함 |
| Roboto | 번들 폰트 | Apache-2.0 | 폰트 라이선스 포함 |
| Liberation Sans/Serif/Mono | 번들 폰트 | SIL Open Font License 1.1 | 폰트 라이선스 포함 |
| yt-dlp | YouTube URL 프리뷰용 외부 실행 파일 | 소스 프로젝트와 standalone 실행 파일의 라이선스 조건이 다를 수 있음 | yttStudio 릴리스에는 번들하지 않음. 필요 시 앱이 고정된 공식 upstream release asset을 직접 내려받아 SHA-256 검증 후 사용자 로컬 영역에 설치 |
| libmpv | 선택적 네이티브 동적 라이브러리 | 빌드 구성에 따라 LGPL/GPL 여부가 달라짐 | yttStudio 패키지에는 직접 번들하지 않음. 사용자가 설치/지정한 빌드를 우선 사용 |
| .NET/Avalonia/SkiaSharp/Serilog 등 | NuGet/runtime | 패키지별 라이선스 | resolved dependency graph를 기준으로 별도 감사 대상 |

## yt-dlp 정책

YouTube URL 프리뷰는 yttStudio의 기본 기능으로 유지한다. 사용자가 별도로 yt-dlp를
설치하지 않았더라도 기능을 사용할 수 있도록 앱은 지원 플랫폼에서 고정된 공식
`yt-dlp/yt-dlp` release asset을 내려받을 수 있다.

현재 구현 원칙:

1. 사용자가 설치한 `yt-dlp` (`YTTSTUDIO_YTDLP_PATH`, 앱 경로, `PATH`)를 먼저 찾는다.
2. 찾지 못하면 `YtDlpAutoInstaller`가 코드에 고정된 버전과 asset을 사용한다.
3. 다운로드 후 코드에 고정된 SHA-256과 일치하는지 확인한다.
4. 검증된 파일만 사용자 로컬 application-data 영역에 설치한다.
5. yttStudio GitHub Release ZIP, installer, DMG, AppImage에는 yt-dlp 실행 파일을 넣지 않는다.
6. 자동 설치된 yt-dlp는 yt-dlp 및 포함된 제3자 구성 요소의 원래 라이선스를 따른다.

이 구조의 목적은 YouTube URL 프리뷰의 기본 UX를 유지하면서 yttStudio가 standalone
`yt-dlp` 실행 파일을 자체 릴리스 자산으로 재배포하는 관계를 피하는 것이다.

참고:

- https://github.com/yt-dlp/yt-dlp
- https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt

## libmpv 정책

`libmpv = LGPL`이라고 일반화하지 않고 **자동 설치 대상으로 고정한 정확한 산출물**을 기준으로 판단한다. v0.2.3은 과거 Shinchiro 기본 빌드를 사용하지 않는다.

현재 프로그램 내부 설치 대상:

- Windows x64: `zhongfly/mpv-winbuild`의 `mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z`
- macOS arm64 / Linux x64: `Shusek/KMediaMpv v0.2.9`의 `kmedia-mpv-0.2.9-runtime-desktop.jar` 내 해당 플랫폼 네이티브 런타임

두 자산은 버전·파일 길이·SHA-256을 코드에 고정하고 검증한 뒤 사용자 로컬 application-data 영역에 설치한다. KMediaMpv 설치 provenance에는 exact corresponding-source 릴리스 URL을 남긴다. Windows 빌드 역시 upstream 저장소와 고정 릴리스 위치를 기록한다. yttStudio 릴리스 ZIP/installer/DMG/AppImage 자체에는 libmpv를 직접 포함하지 않는다.

사용자가 `YTTSTUDIO_MPV_PATH` 또는 설정 화면에서 다른 libmpv를 지정하면 그 선택을 우선한다. 그 외부 빌드의 라이선스 적합성은 해당 배포자가 제공한 라이선스와 build configuration을 기준으로 별도 판단해야 한다.

참고:

- https://github.com/mpv-player/mpv/blob/master/Copyright
- https://github.com/zhongfly/mpv-winbuild
- https://github.com/Shusek/KMediaMpv
- https://ffmpeg.org/legal.html

## NuGet / 런타임 감사

정식 릴리스 전에 다음을 주기적으로 확인한다.

```bash
dotnet list package --include-transitive
```

직접 및 전이 dependency의 버전, upstream, license expression/file을 확인하고 Unknown,
Custom, NoAssert 또는 강한 copyleft 조건이 새로 들어오면 별도 검토한다.

## 브랜드/상표

yttStudio는 독립적인 오픈소스 프로젝트이며 YouTube 또는 Google LLC와 제휴하거나,
그들의 승인 또는 후원으로 제공되는 공식 제품이 아니다. YouTube는 Google LLC의
상표이다.

프로젝트 이름 `yttStudio`는 현재 강제 변경하지 않는다. 다만 YouTube 공식 로고,
YouTube Studio UI/아이콘/trade dress를 모방하지 않고, 향후 YouTube API Services 또는
Google OAuth/API verification을 직접 도입할 경우 이름과 브랜딩을 다시 검토한다.

## 검증 원칙

저장소에 라이선스 문서가 존재하는 것만으로 배포 의무를 충족했다고 판단하지 않는다.
최종 사용자가 받는 ZIP/installer/DMG/AppImage 안의 실제 payload를 기준으로 확인한다.
