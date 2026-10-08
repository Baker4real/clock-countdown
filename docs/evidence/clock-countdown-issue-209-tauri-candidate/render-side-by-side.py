from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parent
REFERENCE = ROOT.parents[2] / "assets" / "ui-references" / "floating-window" / "520x390" / "2026-09-11-v22" / "floating-countdowns-day.png"
CANDIDATE_SOURCE = ROOT / "candidate.html"
CANDIDATE_RENDER = ROOT / "candidate-render.png"
COMPARISON = ROOT / "side-by-side.png"
BOUNDARY = ROOT / "candidate-boundary.json"


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
    candidate = Image.open(CANDIDATE_RENDER).convert("RGBA")
    if reference.size != (520, 390) or candidate.size != (520, 390):
        raise ValueError(f"expected 520x390 inputs, got {reference.size} and {candidate.size}")

    canvas = Image.new("RGBA", (1040, 432), (255, 253, 250, 255))
    draw = ImageDraw.Draw(canvas)
    title_font = font(("C:/Windows/Fonts/msyh.ttc", "C:/Windows/Fonts/segoeui.ttf"), 15)
    small_font = font(("C:/Windows/Fonts/segoeui.ttf", "C:/Windows/Fonts/msyh.ttc"), 11)
    ink = (59, 25, 13, 255)
    muted = (115, 78, 68, 255)

    draw.text((14, 10), "WPF v22 reference · actual-wpf-floating-window", fill=ink, font=title_font)
    draw.text((534, 10), "Tauri candidate · static HTML screenshot", fill=ink, font=title_font)
    canvas.alpha_composite(reference, (0, 42))
    canvas.alpha_composite(candidate, (520, 42))
    draw.rectangle((0, 42, 519, 431), outline=(239, 207, 198, 255), width=1)
    draw.rectangle((520, 42, 1039, 431), outline=(239, 207, 198, 255), width=1)
    draw.text((14, 414), "v22 manifest-bound reference frame", fill=muted, font=small_font)
    draw.text((534, 414), "candidate/dry-run · no Tauri/WebView2 runtime", fill=muted, font=small_font)
    canvas.convert("RGB").save(COMPARISON, format="PNG", optimize=False)


def update_boundary() -> None:
    data = json.loads(BOUNDARY.read_text(encoding="utf-8"))
    data["reference"]["sha256"] = sha256(REFERENCE)
    data["candidateSource"] = {
        "path": "docs/evidence/clock-countdown-issue-209-tauri-candidate/candidate.html",
        "sha256": sha256(CANDIDATE_SOURCE),
    }
    data["candidateRender"] = {
        "path": "docs/evidence/clock-countdown-issue-209-tauri-candidate/candidate-render.png",
        "sha256": sha256(CANDIDATE_RENDER),
        "renderKind": "headless-browser-static-html",
    }
    data["comparison"]["sha256"] = sha256(COMPARISON)
    BOUNDARY.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    render_comparison()
    update_boundary()
    print(json.dumps({
        "referenceSha256": sha256(REFERENCE),
        "candidateSourceSha256": sha256(CANDIDATE_SOURCE),
        "candidateRenderSha256": sha256(CANDIDATE_RENDER),
        "comparisonSha256": sha256(COMPARISON),
    }, ensure_ascii=False))
