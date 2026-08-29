## 이번 변경

- **유튜브 주소로 프리뷰** — 주소를 붙여 넣으면 내려받지 않고 스트리밍으로 재생하고,
  그 위에서 자막을 그대로 편집합니다. 주소 해석에는 `yt-dlp` 가 필요합니다.
- **스페이스바 재생 · 정지** — 창 어디에 포커스가 있어도 걸립니다. 글자를 입력하는
  중과 타임라인은 예외입니다.
- **재생 줄 아이콘** — 이전 · 재생 · 다음을 같은 도형으로 그려 통일했습니다.
- **자막 색 선택기** — 전경 · 배경 · 테두리 색을 팔레트에서 고를 수 있습니다.

## 설치

.NET 런타임이 들어 있어 따로 설치할 것은 없습니다.

| 파일 | 대상 | 설치 방법 |
|---|---|---|
| `*-win-x64-setup.exe` | Windows x64 | 실행하면 시작 메뉴 등록과 파일 연결까지 처리합니다 |
| `*-win-x64.zip` | Windows x64 (설치 없이) | 압축을 풀고 `YttStudio.App.exe` 실행 |
| `*-osx-arm64.dmg` | macOS Apple Silicon | 열어서 `yttStudio.app` 을 `Applications` 로 끌어 놓기 |
| `*-linux-x86_64.AppImage` | Linux x64 | `chmod +x` 후 실행 |

`.tar.gz` 압축본도 함께 올라갑니다.

## 따로 필요한 것

| 도구 | 언제 | 없으면 |
|---|---|---|
| libmpv 2.0+ | 영상 재생 | 자막 편집 · 검증 · 저장은 그대로 동작합니다 |
| yt-dlp | 유튜브 주소로 열기 | 파일로 연 영상은 그대로 동작합니다 |

둘 다 번들하지 않습니다. libmpv 는 앱의 **도구 → 설정 → 영상** 에서 내려받을 수 있고,
yt-dlp 는 앱 폴더나 `PATH` 에 두면 찾습니다. 자세한 사정은 `docs/DEPENDENCIES.md` 에
있습니다.

## 서명

코드 서명과 notarization 은 아직 없습니다. 첫 실행에 OS 경고가 뜹니다.

- Windows: SmartScreen 에서 **추가 정보 → 실행**
- macOS: `yttStudio.app` 우클릭 → **열기**. 그래도 막히면
  `xattr -dr com.apple.quarantine /Applications/yttStudio.app`
