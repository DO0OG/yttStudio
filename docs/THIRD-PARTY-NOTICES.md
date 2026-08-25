# THIRD-PARTY-NOTICES

이 문서는 YttStudio 배포물에 포함해야 할 third-party 고지와 라이선스 payload의
출발점이다. **현재 저장소에는 libmpv 바이너리가 없고 특정 libmpv 빌드의 원문
라이선스 파일도 아직 없다.** 따라서 이 파일만으로 배포 의무를 충족했다고 보지
않으며, 릴리스 빌드가 선택한 정확한 산출물의 고지·라이선스·소스 제공 정보를
`licenses/` payload로 채워야 한다.

## 배포 시 필수 payload

최종 패키지 루트에 이 문서의 생성본과 다음 파일을 둔다.

```
THIRD-PARTY-NOTICES.txt
licenses/
  libmpv/
    LICENSE (실제 빌드가 제공한 LGPL 또는 GPL 전문)
    COPYRIGHT (실제 빌드의 저작권 고지)
    SOURCE-OFFER.txt (소스 또는 서면 제공 경로)
  fonts/
    LICENSE-Roboto.txt
    LICENSE-Liberation.txt
```

libmpv는 앱과 정적으로 합쳐졌다고 가정하지 않는다. 정식 패키지는 교체 가능한
동적 sidecar를 제공하고, 해당 sidecar의 빌드 식별자와 source correspondence를
같이 고지한다. 정확한 파일명과 위치는 [`DEPENDENCIES.md`](DEPENDENCIES.md) §18을
따른다.

## 고지 목록

| 구성 요소 | 저장소/버전 근거 | 라이선스 상태 | 배포 시 조치 |
|---|---|---|---|
| libmpv sidecar | 릴리스가 실제 선택한 upstream/build | **미정** — 일반적으로 LGPL-2.1-or-later, 빌드 구성에 따라 GPL 구성 가능 | 정확한 빌드의 LICENSE/COPYRIGHT/source offer를 포함하고, GPL 구성 여부를 확인 |
| YTSubConverter.Shared | `external/YTSubConverter`, fork `DO0OG/YTSubConverter`, pin `c460cca` | MIT (upstream 표기) | MIT 고지와 저작권을 포함 |
| Roboto | `src/YttStudio.Render/Assets/Fonts/Roboto-Regular.ttf` | Apache-2.0 | `src/YttStudio.Render/Assets/Fonts/LICENSE-Roboto.txt`를 포함 |
| Liberation Sans/Serif/Mono | `src/YttStudio.Render/Assets/Fonts/` | SIL Open Font License 1.1 | `LICENSE-Liberation.txt`와 폰트 저작권 고지를 포함 |
| .NET/Avalonia/SkiaSharp/Serilog 등 | `Directory.Packages.props`, 각 NuGet package metadata | 패키지별 license | 릴리스 시 resolved dependency graph에서 notice를 생성해 누락 여부 확인 |

## libmpv license 결정 규칙

libmpv의 “LGPL” 표기만으로 실제 배포물의 의무를 단정하지 않는다.

1. 릴리스에 넣는 파일의 `mpv-version`, commit/build 식별자와 구성 옵션을 기록한다.
2. 그 산출물이 LGPL-2.1-or-later인지 GPL 구성 요소를 포함하는지 upstream 제공
   파일과 함께 확인한다.
3. LGPL 구성이라면 LGPL 전문, 저작권, 동적 링크/교체 방법, 소스 또는 서면 제공
   경로를 배포물에 넣는다. 수정된 라이브러리라면 수정 고지도 추가한다.
4. GPL 구성이라면 GPL의 전체 저작물 배포 조건이 적용될 수 있으므로, 배포 전에
   라이선스 호환성 및 법률 검토를 완료한다.

참고 링크:

- [mpv project](https://github.com/mpv-player/mpv)
- [GNU LGPL 2.1](https://www.gnu.org/licenses/old-licenses/lgpl-2.1.html)
- [GNU GPL 2.0](https://www.gnu.org/licenses/old-licenses/gpl-2.0.html)

## 현재 미결정/미구현

- 저장소에 libmpv binary가 없어 정확한 버전, commit, configure 옵션, license
  payload를 확정할 수 없다.
- `licenses/libmpv/`와 자동 notice 생성 단계는 아직 없다.
- 정식 패키징, macOS 서명/notarization, Linux AppImage 생성은 아직 없다.

이 미결정 상태를 해결하기 전에는 개발 환경에서 `YTTSTUDIO_MPV_PATH`로 찾은
libmpv를 정식 배포물과 동일한 것으로 안내하지 않는다.
