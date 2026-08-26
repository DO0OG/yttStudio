<div align="center">

<img src="docs/assets/logo.png" width="112" alt="YttStudio" />

# YttStudio

**A WYSIWYG editor built for YouTube YTT (SRV3) subtitles**

Position and style captions directly on the video, then export `.ytt` — on the desktop.

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![Avalonia](https://img.shields.io/badge/Avalonia-12-8B44AC)
![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)
![Tests](https://img.shields.io/badge/tests-150%20passing-brightgreen)

[한국어](README.md) · **English** · [日本語](README.ja.md)

</div>

---

## Why this exists

YouTube's built-in caption editor offers no styling at all. The player, however, already
supports colours, outlines, glow, positioning, karaoke timing, ruby text and vertical
writing through **YTT (YouTube Timed Text, internally SRV3)** — the XML format behind the
elaborate captions you see on music videos and covers.

The existing ecosystem has **converters**, but no editor that lets you **place captions on
the video itself**. YttStudio fills that gap.

## Screenshots

<div align="center">
<img src="docs/render-comparison/m2-canvas.png" width="880" alt="Editing canvas" />
</div>

<div align="center"><sub>
Captions are composited over the video and positioned with the mouse. Style presets on the
left, property panel on the right, track timeline and cue list below.
</sub></div>

<br />

<table>
<tr>
<td width="50%"><img src="docs/render-comparison/m1-render.png" alt="Rendering pipeline" /></td>
<td width="50%"><img src="docs/render-comparison/m3-effects.png" alt="Effects" /></td>
</tr>
<tr>
<td align="center"><sub>Caption rendering under YTT rules</sub></td>
<td align="center"><sub>Move, fade, shake and chromatic aberration</sub></td>
</tr>
</table>

## Features

| | |
|---|---|
| **Edit on the video** | Play through libmpv while dragging, multi-selecting and snap-aligning captions |
| **Playback and file drop** | Press `Space` to toggle play/pause after opening a video, or drop supported subtitle/video files onto the window to open them |
| **Timeline** | Zoom, pan sideways, drag the scrollbar, move blocks and trim their edges. Panels resize by dragging their borders |
| **Format rules enforced** | Coordinate mapping, font-scale clamping and opacity ceilings are checked in code |
| **Karaoke** | Automatic syllable splitting (Hangul, kana, Latin, Han), tap-along timing, five progression modes |
| **Effects** | Move · fade · shake · chromatic aberration · animation. Shake is deterministic, so scrubbing back reproduces it exactly |
| **Validator** | 17 rules covering YouTube's constraints, with undoable auto-fixes |
| **Round trip** | Import and export `.ytt` / `.srv3` / `.ass`, with lossy conversions reported as warnings |
| **Projects** | `.yttproj` package, autosave with a configurable interval, crash recovery |
| **Preferences** | Language, theme, libmpv path and autosave are set in a window and persist across runs |
| **Themes** | System default · light · dark, switched without restarting |
| **Three languages** | Korean · English · Japanese, switchable at runtime |

File drop supports subtitle files `.ytt` / `.srv3` / `.ass` and video files `.mp4` / `.mkv` / `.webm` / `.mov` / `.avi` / `.m4v`. Open `.yttproj` files through the project command.

## Getting started

### Requirements

| Item | Notes |
|---|---|
| .NET 10 SDK | Required to build |
| libmpv 2.0+ | **Only needed for video playback.** Editing, validation and saving work without it |

### Build and run

```bash
git clone --recursive https://github.com/DO0OG/YttStudio.git
cd YttStudio
dotnet build -c Release
dotnet run --project src/YttStudio.App
```

Pass a subtitle file to open it immediately.

```bash
dotnet run --project src/YttStudio.App -- samples/showcase.ass
```

### Pointing at libmpv

The easiest route is **Tools → Settings → Video**. Pick the library yourself, or let the app
download it on Windows. The chosen path persists across runs.

An environment variable works too. The lookup order is the stored preference →
`YTTSTUDIO_MPV_PATH` → the application directory → standard OS paths.

```bash
# Windows
set YTTSTUDIO_MPV_PATH=C:\path\to\libmpv-2.dll

# macOS / Linux
export YTTSTUDIO_MPV_PATH=/usr/lib/libmpv.so.2
```

Versions below 2.0 are rejected. If no library is found, only the video features are disabled
and the canvas falls back to a solid or checkerboard background.

> **Licensing note.** Official Windows mpv builds are GPLv2+, so YttStudio never ships a libmpv
> binary. The optional download in the settings window runs only after you have seen the source
> and licence and pressed the button yourself, and it installs into your own machine.
> On macOS and Linux the app points you at your package manager instead.

## Things worth knowing

**The preview is an editing approximation.** YouTube's renderer is DOM/CSS based, so glow
radius and line-break points differ slightly. Confirm the final result with a real upload.

**Save your work as `.yttproj`.** A `.ytt` file is the baked output — effects are flattened
into keyframes and do not survive a round trip, and track and draw order are not preserved.

**The absence of rotation and resize handles is deliberate.** The YTT format has neither
rotation nor free scaling. Offering them would transform the on-screen preview while the
exported file ignored the change, which is worse than not having them.

**Viewport modes (normal, theatre, fullscreen, mobile) are disabled.** They will not be
implemented on guesswork before each mode's actual coordinate behaviour has been measured.

## Documentation

| Document | Contents |
|---|---|
| [User guide](docs/USER_GUIDE.md) | Feature-by-feature usage and limitations |
| [Dependencies](docs/DEPENDENCIES.md) | Pinned versions, distribution strategy, local patches |
| [Performance](docs/PERFORMANCE.md) | Measurements per resolution and the reasoning behind backend choices |
| [Format verification](docs/YTT-VERIFICATION.md) | Evidence and confidence grade for each YTT rule |
| [Manual QA](docs/MANUAL_QA.md) | Checks that cannot be automated |
| [Third-party notices](docs/THIRD-PARTY-NOTICES.md) | Bundled fonts and libraries |

## Built with

.NET 10 · C# 14 · Avalonia 12 · SkiaSharp · libmpv · xUnit

Subtitle format I/O uses [YTSubConverter](https://github.com/arcusmaximus/YTSubConverter) (MIT).
One local patch is applied so that `ap` and `ju` can be set independently; see the
[dependencies document](docs/DEPENDENCIES.md) for details.

## Licence

See [LICENSE](LICENSE). Notices for bundled fonts and external libraries are in
[third-party notices](docs/THIRD-PARTY-NOTICES.md).

## Contributors

- [DO0OG](https://github.com/DO0OG)

---

## Quick canvas editing

- Double-click an empty area of the preview to create a two-second cue at the current playback time and clicked position, then start typing immediately.
- Single-click a cue to select it and drag it to reposition it. Double-click an existing cue to edit its text inline.
- Use `Ctrl+Z` to undo and `Ctrl+Y` or `Ctrl+Shift+Z` to redo. When a text box has focus, its native text history remains in control.