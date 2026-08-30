# yttStudio 배포 컴플라이언스 감사

검토 기준일: 2026-08-30

이 문서는 yttStudio의 제3자 구성 요소와 배포 방식을 추적하기 위한 기술적
컴플라이언스 기록이다. 법률 자문을 대신하지 않는다.

## 결론 요약

| 항목 | 상태 | 현재 조치 |
|---|---|---|
| yttStudio 자체 코드 | 낮은 위험 | MIT 유지, 루트 LICENSE를 릴리스에 포함 |
| YTSubConverter.Shared | 낮은 위험 | MIT 고지 보존 및 릴리스 payload 포함 |
| Roboto / Liberation 폰트 | 낮음~중간 | 각 라이선스를 최종 패키지에 포함 |
| yt-dlp standalone 재배포 | 기존 높은 위험 | yttStudio 릴리스 직접 번들 제거 |
| YouTube URL 기본 프리뷰 | 유지 | 필요 시 공식 yt-dlp 자산을 사용자 영역에 자동 설치 |
| libmpv / FFmpeg | 검토 필요 | 검증되지 않은 Shinchiro 자동 설치 UI 비활성화, 사용자 설치/경로 지정 유지 |
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
- 없으면 `YtDlpAutoInstaller`가 지원 RID에 맞는 공식 `yt-dlp/yt-dlp` release asset을
  직접 내려받는다.
- 버전과 SHA-256은 코드에 고정된다.
- 검증된 파일만 사용자 LocalApplicationData 아래에 설치하고
  `YTTSTUDIO_YTDLP_PATH`로 현재 프로세스에 전달한다.
- YouTube URL 프리뷰 UX는 기본 기능으로 유지한다.

현재 pin:

- yt-dlp: `2026.08.19`
- Windows x64: `yt-dlp.exe`
- macOS arm64 배포: `yt-dlp_macos`
- Linux x64: `yt-dlp_linux`

새 버전으로 올릴 때는 release asset과 SHA-256을 함께 다시 검증해야 한다.

## libmpv

`libmpv`라는 이름만으로 LGPL이라고 판단하지 않는다. 실제 mpv, FFmpeg 및 링크된
라이브러리의 빌드 옵션에 따라 GPL 구성이 될 수 있다.

검토 당시 Windows 자동 설치 대상으로 사용되던 Shinchiro build 계열은 FFmpeg의
GPL/version3 옵션을 사용하는 구성이 확인되어 MIT 애플리케이션의 공식 자동 설치
대상으로 계속 고정하기에는 불확실성이 남았다.

따라서 현재 정식 UI에서는 해당 자동 설치 delegate를 제공하지 않는다.

유지되는 기능:

- `YTTSTUDIO_MPV_PATH`
- 설정 창에서 libmpv 파일/디렉터리 선택
- 앱/OS loader의 표준 탐색
- macOS/Linux 패키지 관리자 설치 안내

향후 자동 설치를 복원하려면 정확한 binary provenance, mpv/FFmpeg commit,
build options, 포함 library 및 최종 GPL/LGPL 상태를 검증해야 한다.

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

또한 publish 디렉터리에 다음 파일명이 존재하면 릴리스를 실패시킨다.

```text
yt-dlp
yt-dlp.exe
yt-dlp_macos
yt-dlp_linux
```

따라서 향후 workflow가 변경되어도 yt-dlp standalone이 무심코 다시 릴리스에
포함되는 것을 방지한다.

## 브랜드 / 이름

`yttStudio` 이름은 현재 강제 변경하지 않는다.

현재 조건:

- 독립적인 로고 및 UI 사용
- YouTube 공식 제품이라고 표시하지 않음
- README에 비제휴 고지 포함
- YouTube 또는 Google LLC의 승인/후원을 암시하지 않음

향후 YouTube API Services, Google OAuth/API verification을 직접 사용하거나 공식
YouTube 제품으로 혼동되는 사례가 발생하면 이름과 브랜딩을 다시 검토한다.

## 후속 감사 항목

아래 항목은 릴리스 전 또는 dependency 변경 시 반복 확인한다.

1. `dotnet list package --include-transitive` 결과와 패키지별 라이선스
2. YTSubConverter submodule pin 및 원본 MIT notice
3. 폰트 파일 변경 여부와 OFL/Apache 조건
4. yt-dlp pin/asset/hash 변경 여부
5. libmpv 공식 제공 정책 변경 여부
6. 최종 ZIP/installer/DMG/AppImage 내부의 실제 라이선스 payload

## 1차 출처

- https://github.com/yt-dlp/yt-dlp
- https://github.com/yt-dlp/yt-dlp/blob/master/THIRD_PARTY_LICENSES.txt
- https://github.com/mpv-player/mpv/blob/master/Copyright
- https://ffmpeg.org/legal.html
- https://github.com/shinchiro/mpv-winbuild-cmake
- https://developers.google.com/youtube/terms/branding-guidelines
- https://about.google/brand-resource-center/
