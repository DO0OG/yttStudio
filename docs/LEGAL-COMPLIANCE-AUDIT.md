# yttStudio 배포 컴플라이언스 감사

> **문서 기준:** v0.3.0 (2026-09-01)

검토 기준일: 2026-08-30

이 문서는 yttStudio의 제3자 구성 요소와 배포 방식을 추적하기 위한 기술적
컴플라이언스 기록이다. 법률 자문을 대신하지 않는다.

## 결론 요약

| 항목 | 상태 | 현재 조치 |
|---|---|---|
| yttStudio 자체 코드 | 낮은 위험 | MIT 유지, 루트 LICENSE를 릴리스에 포함 |
| YTSubConverter.Shared | 낮은 위험 | MIT 고지 보존 및 릴리스 payload 포함 |
| Roboto / Liberation 폰트 | 낮음~중간 | 각 라이선스를 최종 패키지에 포함 |
| yt-dlp standalone 재배포 | 기존 높은 위험 완화 | yttStudio 릴리스 직접 번들 제거, 필요 시 공식 upstream 고정 자산을 사용자 영역에 검증 설치 |
| Deno 자동 설치 | 낮은 위험 | MIT 라이선스의 공식 v2.9.6 자산을 실행 시 upstream에서 직접 받아 크기·SHA-256 검증 후 사용자 영역에 설치 |
| YouTube URL 기본 프리뷰 | 유지 | 공개 VOD 프리뷰를 기본 기능으로 유지하고 yt-dlp는 URL 해석/스트림 연동에만 사용 |
| libmpv / FFmpeg | 고정 산출물 검증 | Shinchiro 기본 빌드 제거, Windows LGPL 전용 빌드와 macOS/Linux KMediaMpv 검증 런타임만 내부 설치 대상으로 pin |
| 프로젝트 이름 `yttStudio` | 상표 검토 항목 | 현재 이름 유지, 비제휴 고지 추가 |

## yt-dlp

### 과거 구조

릴리스 워크플로가 Windows/macOS/Linux용 공식 yt-dlp standalone 실행 파일을
다운로드한 뒤 yttStudio ZIP/installer/DMG/AppImage에 직접 포함했다.

standalone 실행 파일은 yt-dlp 소스 저장소 자체의 라이선스만 보고 판단할 수 없으며,
PyInstaller 및 포함된 제3자 구성 요소의 라이선스 조건을 함께 고려해야 한다.

### 현재 구조

- yttStudio GitHub Release에는 yt-dlp 실행 파일을 포함하지 않는다.
- 사용자가 이미 설치한 yt-dlp가 있으면 이를 우선 사용한다.
- 없으면 `YtDlpAutoInstaller`가 지원 RID에 맞는 공식 `yt-dlp/yt-dlp` release asset을 직접 내려받는다.
- 버전·자산명·SHA-256은 코드에 고정한다.
- 검증된 파일만 사용자 LocalApplicationData 아래에 설치하고 `YTTSTUDIO_YTDLP_PATH`로 현재 프로세스에 전달한다.
- YouTube URL 프리뷰 UX는 기본 기능으로 유지한다.

현재 pin:

- yt-dlp: `2026.08.19`
- Windows x64: `yt-dlp.exe`
- macOS arm64: `yt-dlp_macos`
- Linux x64: `yt-dlp_linux`

새 버전으로 올릴 때는 공식 release asset과 SHA-256을 함께 다시 검증해야 한다.


## Deno

v0.2.4부터 현재 yt-dlp의 YouTube JavaScript challenge 요구사항을 충족하기 위해 Deno를 별도 런타임으로 관리한다.

- 최소 호환 버전: Deno 2.3.0
- 내부 설치 pin: 공식 `denoland/deno v2.9.6`
- 기존 `YTTSTUDIO_DENO_PATH`/`PATH` 호환 설치본을 우선 사용
- 없으면 플랫폼별 공식 `deno` ZIP을 사용자 LocalApplicationData에 설치
- 다운로드 파일 길이와 SHA-256 고정 검증
- ZIP에서 예상 실행 파일 하나만 추출하고 설치 후 `deno --version` 재검증
- 설치 경로를 현재 프로세스 환경에 등록해 직접 yt-dlp와 libmpv `ytdl_hook` 양쪽에서 사용
- yttStudio Release에는 Deno 실행 파일을 직접 번들하지 않음

Deno v2.9.6은 MIT License이다. v0.2.4 사전검증에서는 Windows/macOS/Linux 세 환경 모두 공식 Deno와 yt-dlp 자산의 SHA-256을 확인하고 `--js-runtimes deno:<path>`를 사용한 공개 YouTube VOD 메타데이터 추출을 실제로 통과했다.

## libmpv

과거 Windows 자동 설치 대상으로 검토하던 Shinchiro 기본 빌드는 FFmpeg GPL/version3 구성이 확인되어 v0.2.3 자동 설치 대상에서 제거했다. 대신 **라이선스 목적이 명시되거나 검증 증빙이 제공되는 정확한 런타임 산출물**만 pin한다.

현재 자동 설치 대상:

- Windows x64: `zhongfly/mpv-winbuild`의 `mpv-dev-lgpl-x86_64-20260829-git-e8673660ab.7z`
- macOS arm64 / Linux x64: `Shusek/KMediaMpv v0.2.9`의 `kmedia-mpv-0.2.9-runtime-desktop.jar` 중 해당 플랫폼 네이티브 트리

다운로드 URL·파일 길이·SHA-256을 코드에 고정하고, 허용된 HTTPS GitHub 호스트에서 받은 결과만 압축 해제한다. 설치 후 provenance 파일에 upstream과 corresponding-source 위치를 남긴다.

KMediaMpv v0.2.9의 실제 mpv 라이브러리 파일명은 다음과 같다.

- macOS arm64: `libkmediampv_mpv.dylib`
- Linux x64: `libkmediampv_mpv.so`

Linux 고정 자산에서 yttStudio가 사용하는 mpv client/render API 심볼(`mpv_create`, `mpv_initialize`, `mpv_render_context_create`, `mpv_render_context_render` 등) 전체와 `$ORIGIN` RUNPATH를 확인했다. macOS 고정 자산에서도 동일하게 yttStudio가 사용하는 mpv client/render API 심볼 전체를 실제 `libkmediampv_mpv.dylib`에서 확인했다.

영상 열기는 libmpv 부재 상태에서도 비활성화되지 않는다. 지원 플랫폼에서는 첫 영상 열기가 내부 설치의 진입점이며, 설치가 끝나면 원래 영상 열기 요청을 계속 수행한다. 사용자가 지정한 `YTTSTUDIO_MPV_PATH` 또는 설정 경로는 계속 우선한다.

yttStudio 릴리스 산출물에는 libmpv/KMediaMpv 바이너리를 직접 번들하지 않는다. 자동 설치된 런타임은 upstream 라이선스를 그대로 따르며 yttStudio의 MIT 라이선스로 재라이선스되지 않는다.

## 릴리스 라이선스 gate

`.github/workflows/release.yml`은 publish 디렉터리에 다음을 복사한다.

```text
LICENSE
THIRD-PARTY-NOTICES.txt
licenses/fonts/LICENSE-Roboto.txt
licenses/fonts/LICENSE-Liberation.txt
licenses/YTSubConverter/LICENSE.txt
```

필수 파일 하나라도 없으면 release build를 실패시킨다.

또한 최종 publish 디렉터리에 다음 범주의 파일이 존재하면 릴리스를 실패시킨다.

```text
yt-dlp / yt-dlp.exe / yt-dlp_macos / yt-dlp_linux
deno / deno.exe
libmpv*.dll / mpv-2.dll
libmpv*.dylib
libmpv*.so*
libkmediampv_*
kmedia.jar / kmedia-mpv-*-runtime-desktop.jar
```

따라서 향후 workflow가 변경되어도 yt-dlp standalone이나 자동 설치용 libmpv/KMediaMpv 런타임이 yttStudio 자체 릴리스에 무심코 포함되는 것을 방지한다.

## 최종 사전검증 기록

2026-08-30 v0.2.4 릴리스 준비에서 v0.2.3 검증 항목에 더해 다음을 확인했다.

- 고정 Windows LGPL libmpv 자산의 URL, 파일 크기, SHA-256, `libmpv-2.dll` 존재 여부
- KMediaMpv v0.2.9 데스크톱 JAR의 파일 크기와 SHA-256, macOS/Linux 실제 라이브러리 경로
- KMediaMpv corresponding-source 자산 접근 가능 여부
- Linux 및 macOS 고정 라이브러리의 yttStudio 필요 mpv client/render API 심볼
- Linux 런타임의 `$ORIGIN` RUNPATH
- Windows/macOS/Linux용 yt-dlp 2026.08.19 공식 자산의 SHA-256
- Deno v2.9.6 공식 자산의 Windows/macOS/Linux 파일 크기와 SHA-256
- 세 플랫폼에서 `--js-runtimes deno:<path>`를 사용한 실제 YouTube VOD 메타데이터 추출
- Release 구성 빌드, 전체 테스트, 전이 NuGet 패키지 목록, 문서/배포 정책 모순 및 `git diff --check`
- 같은 기준 커밋의 Windows/macOS/Ubuntu 정규 CI build + test

사전검증 과정에서 실수로 저장소에 들어간 `kmedia.jar`는 제거했으며, 최종 릴리스 워크플로에도 yt-dlp, Deno와 libmpv/KMediaMpv 런타임 비번들 검사를 넣었다.

2026-08-30 v0.2.5는 자동 설치 대상 자산·pin·해시·라이선스 경계를 바꾸지 않는다. 변경은 다운로드 파일 핸들 처리와 기존 yt-dlp 재사용 조건뿐이며, 비번들 정책과 제3자 고지 범위는 v0.2.4와 동일하다. Windows x64에서 고정 Deno·yt-dlp 자산의 실제 다운로드와 SHA-256 검증, 공개 YouTube VOD 메타데이터 추출을 다시 확인했다.

## 브랜드 / 이름

`yttStudio` 이름은 현재 강제 변경하지 않는다.

현재 조건:

- 독립적인 로고 및 UI 사용
- YouTube 공식 제품이라고 표시하지 않음
- README에 비제휴 고지 포함
- YouTube 또는 Google LLC의 승인/후원을 암시하지 않음

향후 YouTube API Services, Google OAuth/API verification을 직접 사용하거나 공식 YouTube 제품으로 혼동되는 사례가 발생하면 이름과 브랜딩을 다시 검토한다.

## 후속 감사 항목

아래 항목은 릴리스 전 또는 dependency 변경 시 반복 확인한다.

1. `dotnet list package --include-transitive` 결과와 패키지별 라이선스
2. YTSubConverter submodule pin 및 원본 MIT notice
3. 폰트 파일 변경 여부와 OFL/Apache 조건
4. yt-dlp pin/asset/hash 변경 여부
5. libmpv/KMediaMpv pin, 실제 라이브러리 파일명, API 심볼 및 corresponding-source 변경 여부
6. 최종 ZIP/installer/DMG/AppImage 내부의 실제 라이선스 payload와 외부 런타임 비포함 여부

## 1차 출처

- https://github.com/yt-dlp/yt-dlp
- https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt
- https://github.com/mpv-player/mpv/blob/master/Copyright
- https://ffmpeg.org/legal.html
- https://github.com/shinchiro/mpv-winbuild-cmake
- https://github.com/zhongfly/mpv-winbuild
- https://github.com/Shusek/KMediaMpv
- https://developers.google.com/youtube/terms/branding-guidelines
- https://about.google/brand-resource-center/
