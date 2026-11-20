from __future__ import annotations

import hashlib
import json
import re
import sys
from pathlib import Path

from PIL import Image, ImageChops

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
MANIFEST_PATH = OUT / "preview-manifest.json"
README_PATH = OUT / "README.md"


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def require(condition: bool, message: str) -> None:
    if not condition:
        raise RuntimeError(message)


def check_readme_links() -> int:
    text = README_PATH.read_text(encoding="utf-8")
    links = re.findall(r"!?\[[^\]]*\]\(([^)]+)\)", text)
    checked = 0
    for raw in links:
        target = raw.split()[0].strip("<>")
        if target.startswith(("https://", "http://", "#")):
            continue
        target = target.split("#", 1)[0]
        require(bool(target), "Empty local Markdown link")
        resolved = (OUT / target).resolve()
        require(resolved.is_file(), f"Broken local Markdown link: {raw}")
        checked += 1
    require(checked >= 8, f"Expected at least 8 local evidence links, got {checked}")
    for required in ("日期印记", "长便签", "滚动", "36", "推荐", "unverified", "不进视觉基线"):
        require(required in text, f"README is missing required coverage: {required}")
    return checked


def main() -> None:
    require(MANIFEST_PATH.is_file(), "Missing preview-manifest.json")
    require(README_PATH.is_file(), "Missing README.md")
    manifest = json.loads(MANIFEST_PATH.read_text(encoding="utf-8"))
    require(manifest["issue"] == 334, "Wrong issue")
    require(manifest["previewVersion"] == "v1", "Wrong preview version")
    require(manifest["designOnly"] is True, "Previews must be design-only")
    require(manifest["visualBaseline"] is False, "Previews must not join the visual baseline")
    require(len(manifest["options"]) == 2, "Expected two width options")
    require(manifest["current"]["bodyTextWidth"] == 189, "Current body width drift")

    source = manifest["source"]
    baseline_manifest_path = ROOT / source["manifestPath"]
    baseline_png_path = ROOT / source["pngPath"]
    baseline_manifest = json.loads(baseline_manifest_path.read_text(encoding="utf-8"))
    require(baseline_manifest["baselineVersion"] == "2026-11-14-v79", "Baseline version drift")
    require(sha256(baseline_png_path) == source["pngSha256"], "Baseline PNG hash mismatch")
    capture = next(item for item in baseline_manifest["captures"] if item["captureId"] == source["captureId"])
    require(capture["pngSha256"] == source["pngSha256"], "Baseline capture binding mismatch")
    require(
        baseline_manifest["sourceBinding"]["aggregateSha256"] == source["sourceBindingAggregateSha256"],
        "Source binding aggregate mismatch",
    )
    for entry in baseline_manifest["sourceBinding"]["files"]:
        source_path = ROOT / entry["path"]
        require(source_path.is_file(), f"Missing bound source: {entry['path']}")
        require(sha256(source_path) == entry["sha256"], f"Bound source changed: {entry['path']}")

    comparison = manifest["comparison"]
    baseline = Image.open(baseline_png_path).convert("RGBA")
    require(baseline.size == (520, 390), "Baseline frame size mismatch")
    left_box = (
        comparison["currentFrameX"],
        comparison["frameY"],
        comparison["currentFrameX"] + 520,
        comparison["frameY"] + 390,
    )
    right_box = (
        comparison["candidateFrameX"],
        comparison["frameY"],
        comparison["candidateFrameX"] + 520,
        comparison["frameY"] + 390,
    )
    allowed = comparison["allowedCandidateDiffInFrame"]

    for option in manifest["options"]:
        require(option["file"].startswith("v1-"), f"Unversioned preview: {option['file']}")
        require(not (ROOT / "assets" / option["file"]).exists(), "Preview leaked into assets")
        image_path = OUT / option["file"]
        require(image_path.is_file(), f"Missing preview: {option['file']}")
        require(sha256(image_path) == option["sha256"], f"Preview hash mismatch: {option['file']}")
        with Image.open(image_path) as probe:
            probe.verify()
        board = Image.open(image_path).convert("RGBA")
        require(board.size == (1112, 650), f"Preview dimensions mismatch: {option['file']}")
        current = board.crop(left_box)
        require(ImageChops.difference(current, baseline).getbbox(alpha_only=False) is None, f"Current frame pixels changed: {option['file']}")
        candidate = board.crop(right_box)
        difference = ImageChops.difference(candidate, baseline)
        require(difference.getbbox(alpha_only=False) is not None, f"Candidate has no visual delta: {option['file']}")
        outside = difference.copy()
        outside.paste((0, 0, 0, 0), (allowed[0], allowed[1], allowed[2], allowed[3]))
        require(outside.getbbox(alpha_only=False) is None, f"Candidate changed shell or safe area: {option['file']}: {outside.getbbox(alpha_only=False)}")
        expected_chars = option["bodyTextWidth"] // 13
        require(option["estimatedFullWidthChars"] == expected_chars, f"Character estimate mismatch: {option['file']}")
        require(option["bodyTextWidth"] > 189, f"Option does not widen body: {option['file']}")

    checked_links = check_readme_links()
    print(json.dumps({
        "status": "PASS",
        "previews": len(manifest["options"]),
        "sourceBindingFiles": len(baseline_manifest["sourceBinding"]["files"]),
        "localLinks": checked_links,
        "baselinePngSha256": source["pngSha256"],
        "checks": [
            "PNG decode and dimensions",
            "preview SHA-256",
            "v79 baseline PNG SHA-256",
            "all current sourceBinding file SHA-256",
            "current frame pixel identity",
            "candidate delta confined to card interior",
            "versioned filenames and non-baseline destination",
            "README local links and required impact coverage",
        ],
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"FAIL: {error}", file=sys.stderr)
        raise SystemExit(1) from error
