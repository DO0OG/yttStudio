"""Optional YttStudio browser-preview hooks for mitmproxy.

YttStudio does not install, launch, or bundle mitmproxy.  Set
YTTSTUDIO_SUBTITLE_FILE in the shell that starts ``mitmdump``.  The hooks only
touch matching YouTube requests and record non-fatal status in
``flow.metadata['yttstudio_preview']`` so a changed YouTube page cannot break
editing or export in YttStudio.
"""

from __future__ import annotations

import json
import logging
import os
import re
from pathlib import Path
from typing import Any
from urllib.parse import urlparse

from mitmproxy import http


SUBTITLE_ENVIRONMENT_VARIABLE = "YTTSTUDIO_SUBTITLE_FILE"
WATCH_PATH = "/watch"
TIMEDTEXT_PATH = "/api/timedtext"

# The suffix makes the match stop at the player-response object, including
# nested JSON objects, while retaining the page text around the assignment.
PLAYER_RESPONSE_PATTERN = re.compile(
    r"(?P<prefix>(?:var\s+)?ytInitialPlayerResponse\s*=\s*)"
    r"(?P<json>\{.*?\})"
    r"(?P<suffix>\s*;\s*var\s+meta\s*=)",
    re.DOTALL,
)


def _set_status(flow: http.HTTPFlow, hook: str, status: str, message: str) -> None:
    """Record a non-fatal hook status without making metadata mandatory."""
    try:
        metadata = flow.metadata
        preview_status = metadata.setdefault("yttstudio_preview", {})
        preview_status[hook] = {"status": status, "message": message}
    except Exception:  # pragma: no cover - metadata is supplied by mitmproxy
        logging.debug("Unable to record YttStudio preview status", exc_info=True)


def _request_url(flow: http.HTTPFlow) -> str:
    try:
        request = flow.request
        return str(getattr(request, "pretty_url", "") or getattr(request, "url", ""))
    except Exception:
        return ""


def _matches_path(url: str, expected_path: str) -> bool:
    try:
        return urlparse(url).path.rstrip("/").lower() == expected_path
    except Exception:
        return False


def _response_text(flow: http.HTTPFlow) -> str | None:
    try:
        response = flow.response
        if response is None:
            return None
        get_text = getattr(response, "get_text", None)
        if callable(get_text):
            return str(get_text(strict=False))
        return str(response.text)
    except Exception:
        return None


def generate_dummy_captions(video_id: str) -> dict[str, Any]:
    """Create the small caption-track structure needed to expose the CC menu."""
    caption_track = {
        "baseUrl": "https://www.youtube.com/api/timedtext?v=" + video_id,
        "vssId": ".pr",
        "languageCode": "pr",
        "isTranslatable": True,
        "name": {"runs": [{"text": "Preview"}]},
    }
    audio_track = {
        "defaultCaptionTrackIndex": 0,
        "hasDefaultTrack": True,
        "captionTrackIndices": [0],
    }
    renderer = {
        "captionTracks": [caption_track],
        "audioTracks": [audio_track],
        "defaultAudioTrackIndex": 0,
    }
    return {"playerCaptionsTracklistRenderer": renderer}


def ensure_subtitle_selector(flow: http.HTTPFlow) -> None:
    """Expose a ``Preview`` caption track in a matching watch-page response."""
    url = _request_url(flow)
    if not _matches_path(url, WATCH_PATH):
        _set_status(flow, "ensure_subtitle_selector", "not_applicable", "Not a YouTube watch page.")
        return

    html = _response_text(flow)
    if html is None:
        _set_status(
            flow,
            "ensure_subtitle_selector",
            "unavailable",
            "The response body could not be read; external preview is unavailable.",
        )
        return

    match = PLAYER_RESPONSE_PATTERN.search(html)
    if match is None:
        _set_status(
            flow,
            "ensure_subtitle_selector",
            "unavailable",
            "DOM/regex player-response discovery failed; external preview is unavailable.",
        )
        return

    try:
        player_response = json.loads(match.group("json"))
    except (TypeError, ValueError, json.JSONDecodeError):
        _set_status(
            flow,
            "ensure_subtitle_selector",
            "unavailable",
            "DOM/regex player-response JSON was invalid; external preview is unavailable.",
        )
        return

    if not isinstance(player_response, dict):
        _set_status(
            flow,
            "ensure_subtitle_selector",
            "unavailable",
            "The player response had an unsupported shape; external preview is unavailable.",
        )
        return

    if player_response.get("captions"):
        _set_status(
            flow,
            "ensure_subtitle_selector",
            "unchanged",
            "The watch page already contains a caption track.",
        )
        return

    video_details = player_response.get("videoDetails")
    video_id = video_details.get("videoId") if isinstance(video_details, dict) else None
    if not isinstance(video_id, str) or not video_id:
        _set_status(
            flow,
            "ensure_subtitle_selector",
            "unavailable",
            "The player response did not contain a video ID; external preview is unavailable.",
        )
        return

    player_response["captions"] = generate_dummy_captions(video_id)
    patched_html = (
        html[: match.start("json")]
        + json.dumps(player_response, separators=(",", ":"))
        + html[match.end("json") :]
    )

    try:
        flow.response.content = patched_html.encode("utf-8")
    except Exception:
        _set_status(
            flow,
            "ensure_subtitle_selector",
            "unavailable",
            "The watch-page response could not be patched; external preview is unavailable.",
        )
        return

    _set_status(
        flow,
        "ensure_subtitle_selector",
        "applied",
        "The Preview caption track was added to the watch page.",
    )


def _read_subtitle_file() -> bytes | None:
    subtitle_location = os.environ.get(SUBTITLE_ENVIRONMENT_VARIABLE, "").strip()
    if not subtitle_location:
        logging.warning("%s is not set; custom subtitle response was not applied.", SUBTITLE_ENVIRONMENT_VARIABLE)
        return None

    try:
        return Path(subtitle_location).read_bytes()
    except FileNotFoundError:
        logging.warning("Subtitle file %s was not found; custom subtitle response was not applied.", subtitle_location)
    except OSError:
        logging.warning("Subtitle file %s could not be read; custom subtitle response was not applied.", subtitle_location)
    return None


def apply_custom_subtitles(flow: http.HTTPFlow) -> None:
    """Serve the local subtitle file for a matching timed-text request."""
    url = _request_url(flow)
    if not _matches_path(url, TIMEDTEXT_PATH):
        _set_status(flow, "apply_custom_subtitles", "not_applicable", "Not a YouTube timed-text request.")
        return

    subtitle_bytes = _read_subtitle_file()
    if subtitle_bytes is None:
        _set_status(
            flow,
            "apply_custom_subtitles",
            "unavailable",
            "The local subtitle file could not be read; the original request was left unchanged.",
        )
        return

    try:
        flow.response = http.Response.make(
            200,
            subtitle_bytes,
            {
                "Content-Type": "text/xml; charset=utf-8",
                "Cache-Control": "no-cache, must-revalidate",
                "Pragma": "no-cache",
            },
        )
    except Exception:
        _set_status(
            flow,
            "apply_custom_subtitles",
            "unavailable",
            "The timed-text response could not be replaced; the original request was left unchanged.",
        )
        return

    _set_status(
        flow,
        "apply_custom_subtitles",
        "applied",
        "The local subtitle file was served for the timed-text request.",
    )


def response(flow: http.HTTPFlow) -> None:
    """mitmproxy response hook."""
    ensure_subtitle_selector(flow)


def request(flow: http.HTTPFlow) -> None:
    """mitmproxy request hook."""
    apply_custom_subtitles(flow)
