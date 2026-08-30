from pathlib import Path


def replace_required(text: str, old: str, new: str, path: Path) -> str:
    if old not in text:
        raise SystemExit(f"필수 문구를 찾지 못함: {path}: {old!r}")
    return text.replace(old, new, 1)


def update_dependencies() -> None:
    path = Path("docs/DEPENDENCIES.md")
    text = path.read_text(encoding="utf-8")
    text = replace_required(
        text,
        "> **문서 기준:** v0.2.3 (2026-08-30)",
        "> **문서 기준:** v0.2.4 (2026-08-30)",
        path,
    )
    text = replace_required(
        text,
        "## libmpv 및 외부 재생 도구 (v0.2.3 현재 정책)",
        "## libmpv 및 외부 재생 도구 (v0.2.4 현재 정책)",
        path,
    )
    if "### Deno" not in text:
        start = text.index("### yt-dlp")
        end = text.index("\n---\n", start)
        deno = """

### Deno

현재 yt-dlp의 YouTube EJS 추출은 외부 JavaScript 런타임을 사용한다. v0.2.4는 yt-dlp가 권장하는 Deno 경로를 기본으로 사용하며 최소 지원 버전은 2.3.0이다. `DenoAutoInstaller`는 `YTTSTUDIO_DENO_PATH`와 `PATH`에서 호환 Deno를 먼저 찾고, 없으면 공식 `denoland/deno v2.9.6`의 `deno` ZIP 자산을 사용자 LocalApplicationData에 설치한다. `denort` 자산은 사용하지 않는다.

| OS / RID | 공식 자산 | SHA-256 |
|---|---|---|
| Windows x64 | `deno-x86_64-pc-windows-msvc.zip` | `15e5300b0ba3c3695a7621d90160a746ec9e710228cee639afa9d580f6e3cd11` |
| macOS arm64 | `deno-aarch64-apple-darwin.zip` | `213a2f304f04d3c9cb5220669afad138f60a5aab1fe80962abdeb8f35807a472` |
| Linux x64 | `deno-x86_64-unknown-linux-gnu.zip` | `394f07f4da2bebe6ce6f1e7ce0fa16429b29b08c35e3fac3fe25972676dff4b2` |

다운로드 URL·정확한 파일 길이·SHA-256을 고정하고 HTTPS GitHub release 호스트만 허용한다. ZIP에서 예상 `deno`/`deno.exe` 한 파일만 추출하며 설치 후 `--version`으로 실제 런타임이 2.3.0 이상인지 확인한다. 설치 경로를 `YTTSTUDIO_DENO_PATH`에 기록하고 현재 프로세스 `PATH` 앞에 추가해, 직접 yt-dlp 사전 확인과 libmpv `ytdl_hook`이 실행하는 yt-dlp가 동일한 Deno를 사용하게 한다.

직접 사전 확인은 `--js-runtimes deno:<path>`를 명시한다. v0.2.4 사전검증에서는 Windows/macOS/Linux 모두 고정 Deno와 yt-dlp 공식 자산을 실제 다운로드·해시 검증한 뒤 공개 YouTube VOD의 JSON 메타데이터 추출까지 성공했다.

Deno v2.9.6은 MIT License이며 yttStudio 릴리스 산출물에는 직접 번들하지 않는다.
"""
        text = text[:end] + deno + text[end:]
    path.write_text(text, encoding="utf-8")


def update_legal_audit() -> None:
    path = Path("docs/LEGAL-COMPLIANCE-AUDIT.md")
    text = path.read_text(encoding="utf-8")
    text = replace_required(
        text,
        "> **문서 기준:** v0.2.3 (2026-08-30)",
        "> **문서 기준:** v0.2.4 (2026-08-30)",
        path,
    )
    if "| Deno 자동 설치 |" not in text:
        marker = "| yt-dlp standalone 재배포 | 기존 높은 위험 완화 | yttStudio 릴리스 직접 번들 제거, 필요 시 공식 upstream 고정 자산을 사용자 영역에 검증 설치 |"
        text = replace_required(
            text,
            marker,
            marker + "\n| Deno 자동 설치 | 낮은 위험 | MIT 라이선스의 공식 v2.9.6 자산을 실행 시 upstream에서 직접 받아 크기·SHA-256 검증 후 사용자 영역에 설치 |",
            path,
        )
    if "## Deno" not in text:
        marker = "\n## libmpv\n"
        deno = """

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
"""
        text = replace_required(text, marker, deno + marker, path)
    text = text.replace(
        "yt-dlp / yt-dlp.exe / yt-dlp_macos / yt-dlp_linux\nlibmpv*.dll / mpv-2.dll",
        "yt-dlp / yt-dlp.exe / yt-dlp_macos / yt-dlp_linux\ndeno / deno.exe\nlibmpv*.dll / mpv-2.dll",
    )
    text = text.replace(
        "2026-08-30 v0.2.3 릴리스 준비에서 다음을 새로 검증했다.",
        "2026-08-30 v0.2.4 릴리스 준비에서 v0.2.3 검증 항목에 더해 다음을 확인했다.",
    )
    if "Deno v2.9.6 공식 자산의 Windows/macOS/Linux 파일 크기와 SHA-256" not in text:
        marker = "- Windows/macOS/Linux용 yt-dlp 2026.08.19 공식 자산의 SHA-256"
        text = replace_required(
            text,
            marker,
            marker + "\n- Deno v2.9.6 공식 자산의 Windows/macOS/Linux 파일 크기와 SHA-256\n- 세 플랫폼에서 `--js-runtimes deno:<path>`를 사용한 실제 YouTube VOD 메타데이터 추출",
            path,
        )
    text = text.replace(
        "yt-dlp와 libmpv/KMediaMpv 런타임 비번들 검사를 넣었다.",
        "yt-dlp, Deno와 libmpv/KMediaMpv 런타임 비번들 검사를 넣었다.",
    )
    path.write_text(text, encoding="utf-8")


def update_user_guide() -> None:
    path = Path("docs/USER_GUIDE.md")
    text = path.read_text(encoding="utf-8")
    text = replace_required(
        text,
        "> **문서 기준:** v0.2.3 (2026-08-30)",
        "> **문서 기준:** v0.2.4 (2026-08-30)",
        path,
    )
    text = replace_required(
        text,
        "## v0.2.3 영상 재생 첫 사용 흐름",
        "## v0.2.4 영상 재생 첫 사용 흐름",
        path,
    )
    old = "로컬 영상 또는 YouTube 주소 열기는 libmpv가 아직 없는 첫 실행에서도 사용할 수 있다. 기존 호환 libmpv를 찾지 못하면 지원 플랫폼에서 프로그램이 검증된 LGPL 런타임을 사용자 영역에 설치하고, 완료 후 사용자가 처음 요청한 영상을 그대로 연다. YouTube 주소에서 yt-dlp가 없을 때도 같은 방식으로 공식 고정 자산을 검증 설치한다. 설정의 **영상** 페이지에서는 libmpv 재설치와 수동 경로 지정도 가능하다."
    new = "로컬 영상 또는 YouTube 주소 열기는 libmpv가 아직 없는 첫 실행에서도 사용할 수 있다. 기존 호환 libmpv를 찾지 못하면 지원 플랫폼에서 프로그램이 검증된 LGPL 런타임을 사용자 영역에 설치하고, 완료 후 사용자가 처음 요청한 영상을 그대로 연다. YouTube 주소에서는 yt-dlp와 현재 YouTube JavaScript challenge 처리에 필요한 Deno 2.3+도 확인하며, 없으면 각각 공식 고정 자산을 검증 설치한다. 설정의 **영상** 페이지에서는 libmpv 재설치와 수동 경로 지정도 가능하다."
    text = replace_required(text, old, new, path)
    old = "| yt-dlp | **YouTube 주소 미리보기에만 필요.** 릴리즈에는 검증된 2026.08.19 바이너리 포함, 개발 빌드는 별도 준비 |"
    new = "| yt-dlp | **YouTube 주소 미리보기에 필요.** 없으면 공식 2026.08.19 자산을 사용자 영역에 검증 설치 |\n| Deno 2.3+ | **현재 yt-dlp의 YouTube JavaScript challenge 처리에 필요.** 없으면 공식 v2.9.6 자산을 사용자 영역에 검증 설치 |"
    text = replace_required(text, old, new, path)
    start = text.index("### YouTube 주소 미리보기 준비")
    end = text.index("\n---\n", start)
    replacement = """### YouTube 주소 미리보기 준비

지원 플랫폼에서는 별도 도구를 미리 설치할 필요가 없습니다. 주소를 처음 열 때 yttStudio가 다음 순서로 준비합니다.

1. Deno 2.3 이상을 `YTTSTUDIO_DENO_PATH`와 `PATH`에서 찾습니다.
2. 없으면 공식 `denoland/deno v2.9.6` 자산을 사용자 영역에 내려받고 크기·SHA-256·실제 버전을 검증합니다.
3. `yt-dlp`를 `YTTSTUDIO_YTDLP_PATH`, 앱 경로와 `PATH`에서 찾습니다.
4. 없으면 공식 `yt-dlp/yt-dlp 2026.08.19` 자산을 SHA-256 검증 후 사용자 영역에 설치합니다.
5. yt-dlp 사전 확인에 관리 Deno 경로를 명시하고, libmpv `ytdl_hook`에서도 같은 Deno를 찾을 수 있도록 현재 프로세스 `PATH`에 등록합니다.

이 런타임들은 yttStudio 릴리스 파일 안에 직접 포함되지 않으며 실행 시 각 공식 upstream에서 받습니다. Windows의 관리 실행 파일은 `deno.exe`와 `yt-dlp.exe`이며 macOS/Linux에서는 `deno`와 `yt-dlp`입니다.

YouTube가 `HTTP 403`, `HTTP 429` 또는 bot challenge로 요청을 거부한 경우에는 더 이상 일반 **네트워크 실패**로 표시하지 않습니다. DNS·연결 실패·5xx 응답과 YouTube 접근 거부를 분리해 안내합니다.
"""
    text = text[:start] + replacement + text[end:]
    old = "| 앱 디렉터리와 `PATH`에서 `yt-dlp`를 찾지 못함 | **yt-dlp 없음** | `yt-dlp`를 앱 디렉터리 또는 `PATH`에 설치한 뒤 다시 시도 |"
    if old in text:
        text = text.replace(
            old,
            "| 자동 런타임 준비 자체가 실패함 | **재생 도구 준비 실패** | 로그의 Deno/yt-dlp 다운로드·검증 오류를 확인하고 다시 시도 |",
            1,
        )
    marker = "| 주소는 맞지만 YouTube/CDN에 연결하지 못함 | **네트워크 실패** | 네트워크·프록시·방화벽을 확인한 뒤 다시 시도 |"
    if marker in text and "**접근 거부**" not in text:
        text = text.replace(
            marker,
            marker + "\n| YouTube가 HTTP 403·429 또는 bot challenge로 요청을 거부함 | **접근 거부** | 잠시 후 다시 시도. 반복되면 yt-dlp/YouTube 쪽 변경 여부 확인 |",
            1,
        )
    path.write_text(text, encoding="utf-8")


def update_user_agent() -> None:
    path = Path("src/YttStudio.App/MpvAutoInstaller.cs")
    text = path.read_text(encoding="utf-8")
    text = replace_required(
        text,
        'UserAgent.ParseAdd("yttStudio/0.2.3")',
        'UserAgent.ParseAdd("yttStudio/0.2.4")',
        path,
    )
    path.write_text(text, encoding="utf-8")


update_dependencies()
update_legal_audit()
update_user_guide()
update_user_agent()

Path(".github/workflows/v024-doc-sync-once.yml").unlink(missing_ok=True)
Path(".github/scripts/v024_doc_sync.py").unlink(missing_ok=True)
