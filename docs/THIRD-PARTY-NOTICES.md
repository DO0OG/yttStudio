# THIRD-PARTY-NOTICES

> **문서 기준:** v0.2.5 (2026-08-30)

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

현재 yttStudio 릴리스 패키지에는 `yt-dlp`, Deno, `libmpv` 또는 KMediaMpv 런타임 바이너리를 직접 포함하지 않는다.

## 구성 요소

| 구성 요소 | 사용 방식 | 라이선스/상태 | 배포 정책 |
|---|---|---|---|
| yttStudio | 본체 | MIT | 루트 `LICENSE` 포함 |
| YTSubConverter.Shared | git submodule / 소스 참조 | MIT | 원본 MIT 고지 포함 |
| Roboto | 번들 폰트 | Apache-2.0 | 폰트 라이선스 포함 |
| Liberation Sans/Serif/Mono | 번들 폰트 | SIL Open Font License 1.1 | 폰트 라이선스 포함 |
| yt-dlp | YouTube URL 프리뷰용 외부 실행 파일 | 소스 프로젝트와 standalone 실행 파일의 라이선스 조건이 다를 수 있음 | yttStudio 릴리스에는 번들하지 않음. 필요 시 앱이 고정된 공식 upstream release asset을 직접 내려받아 SHA-256 검증 후 사용자 로컬 영역에 설치 |
| Deno | yt-dlp YouTube JavaScript challenge 실행용 외부 런타임 | MIT | yttStudio 릴리스에는 번들하지 않음. 호환 설치본이 없으면 앱이 공식 `denoland/deno` release asset을 직접 내려받아 크기·SHA-256 검증 후 사용자 로컬 영역에 설치 |
| libmpv / KMediaMpv 런타임 | 영상 재생용 교체 가능한 네이티브 동적 라이브러리 | 자동 설치 pin은 정확한 고정 산출물과 upstream 증빙 기준으로 판단 | yttStudio 패키지에는 직접 번들하지 않음. 사용자가 설치/지정한 빌드를 우선하고, 없으면 지원 플랫폼에서 검증 런타임을 사용자 영역에 내부 설치 |
| .NET/Avalonia/SkiaSharp/Serilog 등 | NuGet/runtime | 패키지별 라이선스 | resolved dependency graph를 기준으로 별도 감사 대상 |

## yt-dlp 정책

YouTube URL 프리뷰는 yttStudio의 기본 기능으로 유지한다. 사용자가 별도로 yt-dlp를 설치하지 않았더라도 기능을 사용할 수 있도록 앱은 지원 플랫폼에서 고정된 공식 `yt-dlp/yt-dlp` release asset을 내려받을 수 있다.

현재 구현 원칙:

1. 사용자가 설치한 `yt-dlp` (`YTTSTUDIO_YTDLP_PATH`, 앱 경로, `PATH`)를 먼저 찾는다.
2. 찾지 못하면 `YtDlpAutoInstaller`가 코드에 고정된 버전과 asset을 사용한다.
3. 다운로드 후 코드에 고정된 SHA-256과 일치하는지 확인한다.
4. 검증된 파일만 사용자 로컬 application-data 영역에 설치한다.
5. yttStudio GitHub Release ZIP, installer, DMG, AppImage에는 yt-dlp 실행 파일을 넣지 않는다.
6. 자동 설치된 yt-dlp는 yt-dlp 및 포함된 제3자 구성 요소의 원래 라이선스를 따른다.

이 구조의 목적은 YouTube URL 프리뷰의 기본 UX를 유지하면서 yttStudio가 standalone `yt-dlp` 실행 파일을 자체 릴리스 자산으로 재배포하는 관계를 피하는 것이다.

현재 pin:

- 버전: `2026.08.19`
- Windows x64: `yt-dlp.exe`
- macOS arm64: `yt-dlp_macos`
- Linux x64: `yt-dlp_linux`

현재 yt-dlp의 YouTube EJS 경로는 외부 JavaScript 런타임을 필요로 한다. yttStudio v0.2.5는 아래 Deno 정책으로 해당 요구를 충족하고, 직접 사전 확인에는 `--js-runtimes deno:<path>`를 명시한다. 이 옵션을 지원하지 않는 기존 yt-dlp는 재사용하지 않고 위 고정 자산으로 대체한다.

참고:

- https://github.com/yt-dlp/yt-dlp
- https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt
- https://github.com/yt-dlp/yt-dlp/wiki/EJS

## Deno 정책

v0.2.4부터 YouTube URL 재생을 준비할 때 `DenoAutoInstaller`가 Deno 2.3 이상을 확인한다. `YTTSTUDIO_DENO_PATH` 또는 `PATH`에서 호환 실행 파일을 찾으면 그대로 사용하고, 없으면 공식 `denoland/deno` v2.9.6의 플랫폼별 `deno` ZIP 자산을 설치한다. `denort` 자산은 사용하지 않는다.

고정 자산과 SHA-256:

- Windows x64 `deno-x86_64-pc-windows-msvc.zip`: `15e5300b0ba3c3695a7621d90160a746ec9e710228cee639afa9d580f6e3cd11`
- macOS arm64 `deno-aarch64-apple-darwin.zip`: `213a2f304f04d3c9cb5220669afad138f60a5aab1fe80962abdeb8f35807a472`
- Linux x64 `deno-x86_64-unknown-linux-gnu.zip`: `394f07f4da2bebe6ce6f1e7ce0fa16429b29b08c35e3fac3fe25972676dff4b2`

다운로드는 HTTPS와 허용된 GitHub release 호스트로 제한하고, 정확한 파일 길이와 SHA-256을 검증한다. ZIP에서는 예상한 `deno`/`deno.exe` 한 파일만 추출하며 사용자 로컬 application-data 영역에 설치한다. 설치된 Deno는 현재 프로세스 `PATH`에 추가해 libmpv `ytdl_hook`이 실행하는 yt-dlp에서도 사용할 수 있게 한다.

Deno v2.9.6 자체 라이선스는 MIT이다. yttStudio는 Deno 실행 파일을 GitHub Release 산출물에 포함하지 않고 실행 시 upstream에서 직접 받는다.

참고:

- https://github.com/denoland/deno
- https://github.com/denoland/deno/releases/tag/v2.9.6
- https://github.com/denoland/deno/blob/v2.9.6/LICENSE.md

## libmpv 정책

`libmpv = LGPL`이라고 일반화하지 않고 **자동 설치 대상으로 고정한 정확한 산출물**을 기준으로 판단한다. v0.2.5는 과거 Shinchiro 기본 빌드를 자동 설치 대상으로 사용하지 않는다.

현재 프로그램 내부 설치 대상:

- Windows x64: `zhongfly/mpv-winbuild`의 `mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z`
- macOS arm64 / Linux x64: `Shusek/KMediaMpv v0.2.9`의 `kmedia-mpv-0.2.9-runtime-desktop.jar` 내 해당 플랫폼 네이티브 런타임

현재 고정 SHA-256:

- Windows LGPL libmpv 자산: `78260166265fbc09b3bee75ee3464eb0f6bbaa8ecd172786e33c22bbf8a3cb47`
- KMediaMpv 데스크톱 런타임 JAR: `4250b47144de085c7963f4bdbe99e995b9b2b0374e32a14ebe9d27fd38a67bef`

KMediaMpv v0.2.9의 yttStudio 대상 라이브러리 파일명:

- macOS arm64: `libkmediampv_mpv.dylib`
- Linux x64: `libkmediampv_mpv.so`

두 자산은 버전·파일 길이·SHA-256을 코드에 고정하고 검증한 뒤 사용자 로컬 application-data 영역에 설치한다. KMediaMpv 설치 provenance에는 exact corresponding-source 릴리스 URL을 남긴다. Windows 빌드 역시 upstream 저장소와 고정 릴리스 위치를 기록한다. yttStudio 릴리스 ZIP/installer/DMG/AppImage 자체에는 libmpv/KMediaMpv 런타임을 직접 포함하지 않는다.

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

직접 및 전이 dependency의 버전, upstream, license expression/file을 확인하고 Unknown, Custom, NoAssert 또는 강한 copyleft 조건이 새로 들어오면 별도 검토한다.

## 브랜드/상표

yttStudio는 독립적인 오픈소스 프로젝트이며 YouTube 또는 Google LLC와 제휴하거나, 그들의 승인 또는 후원으로 제공되는 공식 제품이 아니다. YouTube는 Google LLC의 상표이다.

프로젝트 이름 `yttStudio`는 현재 강제 변경하지 않는다. 다만 YouTube 공식 로고, YouTube Studio UI/아이콘/trade dress를 모방하지 않고, 향후 YouTube API Services 또는 Google OAuth/API verification을 직접 도입할 경우 이름과 브랜딩을 다시 검토한다.

## 검증 원칙

저장소에 라이선스 문서가 존재하는 것만으로 배포 의무를 충족했다고 판단하지 않는다. 최종 사용자가 받는 ZIP/installer/DMG/AppImage 안의 실제 payload를 기준으로 확인한다.

릴리스 워크플로는 최종 publish 디렉터리에서 yt-dlp standalone, `deno`/`deno.exe`, `libmpv*`, `libkmediampv_*`, KMediaMpv runtime JAR이 발견되면 실패하도록 구성한다.
