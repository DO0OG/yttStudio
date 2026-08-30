<div align="center">

<img src="docs/assets/logo.png" width="112" alt="yttStudio" />

# yttStudio

**A WYSIWYG editor built for YouTube YTT (SRV3) subtitles**

Position and style captions directly on the video, then export `.ytt` — on the desktop.

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-8B44AC)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![Tests](https://img.shields.io/badge/tests-multi--platform%20CI-brightgreen)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/bd1f9a95330940aeab504f29f2e57d1a)](https://app.codacy.com/gh/DO0OG/yttStudio/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade)

[한국어](README.md) · **English** · [日本語](README.ja.md)

</div>

---

YouTube's built-in caption editor offers no styling at all. The player, however, already
supports colours, outlines, glow, positioning, karaoke timing, ruby text and vertical
writing through **YTT (YouTube Timed Text, internally SRV3)**. The existing ecosystem has
**converters**, but no editor that lets you **place captions on the video itself**.
yttStudio fills that gap.

<div align="center">
<img src="docs/render-comparison/m2-canvas.png" width="880" alt="Editing canvas" />
</div>

<div align="center"><sub>
Edit captions in place on the preview. Style presets on the left, the property panel on
the right, the track timeline and cue list below.
</sub></div>

## Features

| | |
|---|---|
| **Edit on the video** | Drag, multi-select and snap-align captions during playback, and fix the text in place |
| **Open by YouTube address** | Paste an address and the preview streams it — nothing is downloaded |
| **Timeline** | Zoom, horizontal pan, block moves and edge trims. Drag panel borders to resize |
| **Format rules enforced** | Coordinate conversion, font-scale compression and opacity limits are checked in code |
| **Karaoke** | Automatic syllable splitting (Hangul, kana, Latin, Han), tap input during playback, five progression modes |
| **Effects** | Move, fade, shake, chromatic aberration, animation. Shake is deterministic, so rewinding gives the same result |
| **Validator** | 17 YouTube constraint rules with undoable auto-fixes |
| **Round trip** | `.ytt` / `.srv3` / `.ass` import and export, with losses reported as warnings |
| **Projects** | `.yttproj` packages, autosave with a configurable interval, crash recovery |
| **Viewport modes** | Normal, theater, fullscreen and mobile, based on measured YouTube player ratios |
| **Preferences** | Language, theme, libmpv path and autosave are set in a window and persist across runs |
| **Three languages** | 한국어 · English · 日本語, switched live |

`Space` toggles play and pause. Drop `.ytt` / `.srv3` / `.ass` subtitles and
`.mp4` / `.mkv` / `.webm` / `.mov` / `.avi` / `.m4v` video onto the window to open them.

The full set of controls is in the [user guide](docs/USER_GUIDE.md).

## Recent changes

| | |
|---|---|
| **v0.2.5 YouTube playback fix** | Fixes a file lock that made the Deno runtime install fail every time a YouTube URL was opened. An existing yt-dlp without `--js-runtimes` support is now replaced with the pinned official asset |
| **v0.2.4 YouTube playback hotfix** | Current yt-dlp YouTube extraction needs a JavaScript challenge runtime. yttStudio now uses Deno 2.3+ and, when no compatible Deno is available, verifies and installs the official Deno v2.9.6 asset into the user data area for both yt-dlp preflight and libmpv playback |
| **Access errors classified correctly** | HTTP 403, 429 and bot-challenge responses are no longer reported as a generic network outage |
| **v0.2.3 video runtime by default** | If libmpv is missing, the app installs a verified LGPL runtime on the first video open. If yt-dlp is missing on the first YouTube URL open, the app verifies and installs an official upstream asset |
| **Playback shortcut cleanup** | `Space` now toggles playback wherever focus sits. The frame-step buttons became `⏮` and `⏭` |
| **Viewport modes** | Normal, theater, fullscreen and mobile ratios reproduced from measured YouTube players |
| **Installer packaging** | A Windows installer, a macOS `.dmg` and a Linux AppImage |
| **Preferences window and themes** | Light, dark and system default, switched without a restart |

## Getting started

Download from the [latest release](https://github.com/DO0OG/yttStudio/releases/latest).
The .NET runtime is bundled, so there is nothing else to install.

| Platform | File | How to install |
|---|---|---|
| Windows | `yttStudio-v*-win-x64-setup.exe` | Runs the installer: Start menu entry, uninstaller and file associations |
| Windows (portable) | `yttStudio-v*-win-x64.zip` | Extract and run `YttStudio.App.exe` |
| macOS (Apple Silicon) | `yttStudio-v*-osx-arm64.dmg` | Open it and drag `yttStudio.app` into `Applications` |
| Linux | `yttStudio-v*-linux-x86_64.AppImage` | `chmod +x`, then run |

> **The builds are not code-signed.** Without a certificate the first launch shows a
> warning. On Windows choose **More info → Run anyway** in the SmartScreen dialog; on
> macOS right-click `yttStudio.app` and choose **Open**.

### Video playback runtimes

Video playback is a core feature. On supported platforms no manual prerequisite installation is required.

| Item | v0.2.5 behaviour |
|---|---|
| libmpv 2.0+ | Required for local and YouTube playback. yttStudio first prefers a user-selected or discoverable compatible library; if none exists, the first video open installs a **verified LGPL runtime** into the user data area on supported platforms |
| yt-dlp | Required to resolve YouTube addresses. yttStudio first prefers an existing installation; if none exists, the first YouTube URL open downloads the pinned official `yt-dlp/yt-dlp 2026.08.19` release asset and verifies its SHA-256 before installing it into the user data area |
| Deno 2.3+ | Required by current yt-dlp for YouTube JavaScript challenges. If no compatible installation is found, yttStudio downloads the official `denoland/deno v2.9.6` asset, verifies its size and SHA-256, and installs it into the user data area |

The current in-app libmpv sources are the explicit LGPL build from `zhongfly/mpv-winbuild` on Windows x64 and the verified `Shusek/KMediaMpv` runtime on macOS arm64/Linux x64. yt-dlp and Deno are also obtained only from their official upstream release assets. These external runtimes are not embedded in yttStudio release ZIPs, installers, DMGs or AppImages. You can also reinstall libmpv or choose a custom path under **Tools → Settings → Video**. See [dependencies](docs/DEPENDENCIES.md) and [third-party notices](docs/THIRD-PARTY-NOTICES.md) for exact pins, hashes and licence boundaries.

### Building from source

The .NET 10 SDK is required.

```bash
git clone --recursive https://github.com/DO0OG/yttStudio.git
cd yttStudio
dotnet build -c Release
dotnet run --project src/YttStudio.App
```

Pass a subtitle file to open it straight away.

```bash
dotnet run --project src/YttStudio.App -- samples/showcase.ass
```

## Good to know

- **The preview is an editing approximation.** YouTube's real renderer is DOM/CSS based,
  so glow radius and line-break points differ slightly. Confirm the final result with a
  real upload.
- **Save your work as `.yttproj`.** `.ytt` is the flattened keyframe output, so effects
  are not restored when it is read back.
- **The absence of rotation and free-scale handles is deliberate.** The YTT format has
  neither rotation nor arbitrary box scaling. The resize handles convert a drag into the
  `SizePercent` font scale.
- **Fullscreen and mobile portrait viewports have not been measured yet.** They reuse the
  normal-mode ratio. The measurements are in [viewport modes](docs/viewport-modes.md).

## Documentation

| Document | Contents |
|---|---|
| [User guide](docs/USER_GUIDE.md) | Feature-by-feature usage and limits |
| [Dependencies](docs/DEPENDENCIES.md) | Pinned versions, distribution strategy, local patches |
| [Performance](docs/PERFORMANCE.md) | Measurements per resolution and the backend rationale |
| [Format verification](docs/YTT-VERIFICATION.md) | Evidence and confidence grades for the YTT rules |
| [Manual QA](docs/MANUAL_QA.md) | Checks that cannot be automated |
| [Third-party notices](docs/THIRD-PARTY-NOTICES.md) | Bundled fonts and libraries |

## Stack

.NET 10 · C# 14 · Avalonia 12 · SkiaSharp · libmpv · xUnit

Subtitle format I/O uses [YTSubConverter](https://github.com/arcusmaximus/YTSubConverter)
(MIT), with one local patch that treats `ap` and `ju` independently.

## Licence

See [LICENSE](LICENSE). Notices for bundled fonts and external libraries are in
[third-party notices](docs/THIRD-PARTY-NOTICES.md).

## Contributors

- [DO0OG](https://github.com/DO0OG)
