## 설치

해당 OS의 압축 파일을 받아 풀고 `YttStudio.App` 을 실행하세요.
.NET 런타임을 따로 설치할 필요는 없습니다 (self-contained).

| 파일 | 대상 |
|---|---|
| `*-win-x64.zip` | Windows x64 |
| `*-osx-arm64.tar.gz` | macOS Apple Silicon |
| `*-linux-x64.tar.gz` | Linux x64 |

## libmpv 는 포함되어 있지 않습니다

영상 미리보기에는 **libmpv 가 따로 필요합니다.** 배포 정책상 번들하지 않고 시스템에서
탐색합니다 (`docs/DEPENDENCIES.md` 참고). 앱의 영상 열기에서 자동 설치를 안내하거나,
`YTTSTUDIO_MPV_PATH` 환경 변수로 직접 지정할 수 있습니다. libmpv 없이도 자막 편집과
`.ytt` 내보내기는 동작합니다.

## 서명

Windows 코드 서명과 macOS notarization 은 아직 적용되지 않았습니다. 첫 실행 시 OS
경고가 나타날 수 있습니다.
