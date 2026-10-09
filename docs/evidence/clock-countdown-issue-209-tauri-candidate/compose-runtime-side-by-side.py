from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
REFERENCE = ROOT.parents[2] / "assets" / "ui-references" / "floating-window" / "520x390" / "2026-10-09-v32" / "floating-countdowns-day.png"
TAURI_CAPTURE = ROOT / "runtime" / "floating-countdowns-day.png"
COMPARISON = ROOT / "side-by-side-runtime.png"
BINDING = ROOT / "runtime" / "runtime-side-by-side.json"


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def font(candidates: tuple[str, ...], size: int) -> ImageFont.FreeTypeFont:
    for candidate in candidates:
        path = Path(candidate)
        if path.exists():
            return ImageFont.truetype(str(path), size)
    return ImageFont.load_default()


def render_comparison() -> None:
    reference = Image.open(REFERENCE).convert("RGBA")
    capture = Image.open(TAURI_CAPTURE).convert("RGBA")
    if reference.size != (520, 390) or capture.size != (520, 390):
        raise ValueError(f"expected 520x390 inputs, got {reference.size} and {capture.size}")

    canvas = Image.new("RGBA", (1040, 432), (255, 253, 250, 255))
    draw = ImageDraw.Draw(canvas)
    title_font = font(("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/segoeui.ttf"), 15)
    small_font = font(("C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/msyh.ttc"), 11)
    ink = (59, 25, 13, 255)
    muted = (115, 78, 68, 255)

    draw.text((14, 10), "WPF production · v32 baseline frame", fill=ink, font=title_font)
    draw.text((534, 10), "Tauri slice · real WebView2 runtime capture", fill=ink, font=title_font)
    canvas.alpha_composite(reference, (0, 42))
    canvas.alpha_composite(capture, (520, 42))
    draw.rectangle((0, 42, 519, 431), outline=(239, 207, 198, 255), width=1)
    draw.rectangle((520, 42, 1039, 431), outline=(239, 207, 198, 255), width=1)
    draw.text((14, 414), "v32 manifest-bound production frame (2026-10-09)", fill=muted, font=small_font)
    draw.text((534, 414), "TauriGate 20261009T134140436-800ddbc3 · receipt passed", fill=muted, font=small_font)
    canvas.convert("RGB").save(COMPARISON, format="PNG", optimize=False)


def update_binding() -> None:
    data = {
        "reference": {
            "path": "assets/ui-references/floating-window/520x390/2026-10-09-v32/floating-countdowns-day.png",
            "sha256": sha256(REFERENCE),
        },
        "tauriCapture": {
            "path": "docs/evidence/clock-countdown-issue-209-tauri-candidate/runtime/floating-countdowns-day.png",
            "sha256": sha256(TAURI_CAPTURE),
            "captureKind": "tauri-webview2-runtime",
        },
        "comparison": {
            "path": "docs/evidence/clock-countdown-issue-209-tauri-candidate/side-by-side-runtime.png",
            "sha256": sha256(COMPARISON),
        },
    }
    BINDING.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    render_comparison()
    update_binding()
    print(json.dumps({
        "referenceSha256": sha256(REFERENCE),
        "tauriCaptureSha256": sha256(TAURI_CAPTURE),
        "comparisonSha256": sha256(COMPARISON),
    }, ensure_ascii=False))
