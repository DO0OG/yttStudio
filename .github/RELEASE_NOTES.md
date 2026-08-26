## 설치

.NET 런타임이 함께 들어 있어 따로 설치할 필요는 없습니다 (self-contained).

| 파일 | 대상 | 설치 방법 |
|---|---|---|
| `*-win-x64-setup.exe` | Windows x64 | 실행하면 시작 메뉴 등록, 제거 프로그램, `.ytt` · `.srv3` · `.yttproj` 파일 연결까지 처리합니다 |
| `*-win-x64.zip` | Windows x64 (설치 없이) | 압축을 풀고 `YttStudio.App.exe` 실행 |
| `*-osx-arm64.dmg` | macOS Apple Silicon | 열어서 `yttStudio.app` 을 `Applications` 로 끌어 놓기 |
| `*-osx-arm64.tar.gz` | macOS Apple Silicon (압축) | 풀면 나오는 `yttStudio.app` 을 `Applications` 로 옮기기 |
| `*-linux-x86_64.AppImage` | Linux x64 | `chmod +x` 후 실행 |
| `*-linux-x64.tar.gz` | Linux x64 (압축) | 풀고 `YttStudio.App` 실행 |

## libmpv 는 포함되어 있지 않습니다

영상 미리보기에는 **libmpv 가 따로 필요합니다.** 배포 정책상 번들하지 않고 시스템에서
탐색합니다 (`docs/DEPENDENCIES.md` 참고). 앱의 영상 열기에서 자동 설치를 안내하거나,
`YTTSTUDIO_MPV_PATH` 환경 변수로 직접 지정할 수 있습니다. libmpv 없이도 자막 편집과
`.ytt` 내보내기는 동작합니다.

## 서명

Windows 코드 서명과 macOS notarization 은 아직 적용되지 않았습니다. 첫 실행 시 OS
경고가 나타납니다.

- Windows: SmartScreen 경고에서 **추가 정보 → 실행**
- macOS: `yttStudio.app` 우클릭 → **열기** → 대화상자에서 다시 **열기**.
  그래도 막히면 `xattr -dr com.apple.quarantine /Applications/yttStudio.app`
