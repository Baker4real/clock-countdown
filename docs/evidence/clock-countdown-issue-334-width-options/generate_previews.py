from __future__ import annotations

import hashlib
import json
import subprocess
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[3]
OUT = Path(__file__).resolve().parent
BASELINE_VERSION = "2026-11-14-v79"
BASELINE_CAPTURE = "long-notes-day-top"
BASELINE_REL = Path("assets/ui-references/floating-window/520x390") / BASELINE_VERSION
BASELINE_PNG = ROOT / BASELINE_REL / f"{BASELINE_CAPTURE}.png"
BASELINE_MANIFEST = ROOT / BASELINE_REL / "manifest.json"
PREVIEW_VERSION = "v1"
FRAME_WIDTH = 520
FRAME_HEIGHT = 390
BOARD_WIDTH = 1112
BOARD_HEIGHT = 650
FRAME_Y = 100
LEFT_X = 24
RIGHT_X = 568
CARD_TOP = 147
CARD_INNER_TOP = 148
CARD_VISIBLE_BOTTOM = 278
CARD_WIDTH = 319
CARD_BORDER = 2
CHECKBOX_WIDTH = 36
MENU_WIDTH = 36
BODY_RIGHT_MARGIN = 8
BODY_X = 97
REGION_X = 97
REGION_RIGHT = 342
REGION_WIDTH = REGION_RIGHT - REGION_X
BODY_TOP = 157
BODY_LINE_HEIGHT = 18
DATE_HEADER_HEIGHT = 18
DATE_HEADER_GAP = 4
FONT_SIZE = 13
ALLOWED_DIFF = (60, 147, 379, 278)

NOTE_TEXT = (
    "中段锚点6：这是一条没有手工换行符的长便签正文，"
    "它必须依靠真实 WPF TextWrapping 在固定纸面宽度内自然折行，同时保持日期印记、完成复选框、标签和相邻卡片互不遮挡，"
    "编号 6 的末尾校验文字，尾锚点。"
)

COLORS = {
    "canvas": "#FFFBF8",
    "ink": "#3B190D",
    "body": "#6B4A3C",
    "muted": "#82665F",
    "accent": "#C23E68",
    "note": "#FEF5DC",
    "note_line": "#F6D9B2",
    "peach_line": "#F6DFD3",
    "stamp": "#FEEFF1",
    "stamp_text": "#D41F57",
    "stamp_edge": "#E5A8B8",
}

UI_FONT = "C:/Windows/Fonts/msyh.ttc"
UI_BOLD_FONT = "C:/Windows/Fonts/msyhbd.ttc"
NUMERAL_FONT = "C:/Windows/Fonts/bahnschrift.ttf"

OPTIONS = [
    {
        "id": "option-a-narrow-stamp",
        "title": "方案 A：三行窄印记",
        "subtitle": "48 -> 42 DIP；日期文字不变，印记左右内边距 4 -> 0",
        "file": "v1-option-a-narrow-stamp.png",
        "stampWidth": 42,
        "bodyColumnWidth": 203,
        "bodyTextWidth": 195,
        "estimatedFullWidthChars": 15,
        "mode": "narrow",
        "geometry": "319 = 2 边 + 36 勾选 + 203 正文列 + 42 印记 + 36 ...",
    },
    {
        "id": "option-b-top-date",
        "title": "方案 B：上缘单行日期",
        "subtitle": "释放完整 48 DIP 右列；日期改在正文上缘单行显示",
        "file": "v1-option-b-top-date.png",
        "stampWidth": 0,
        "bodyColumnWidth": 245,
        "bodyTextWidth": 237,
        "estimatedFullWidthChars": 18,
        "mode": "top",
        "geometry": "319 = 2 边 + 36 勾选 + 245 正文列 + 36 ...",
    },
]


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def font(size: int, bold: bool = False, numeral: bool = False) -> ImageFont.FreeTypeFont:
    path = NUMERAL_FONT if numeral else (UI_BOLD_FONT if bold else UI_FONT)
    return ImageFont.truetype(path, size=size)


def blend(foreground: str, background: str, alpha: float) -> tuple[int, int, int, int]:
    fg = tuple(int(foreground[index:index + 2], 16) for index in (1, 3, 5))
    bg = tuple(int(background[index:index + 2], 16) for index in (1, 3, 5))
    rgb = tuple(round(alpha * f + (1 - alpha) * b) for f, b in zip(fg, bg))
    return (*rgb, 255)


def tokenize(text: str) -> list[str]:
    tokens: list[str] = []
    ascii_word = ""
    for char in text:
        if ord(char) < 128 and (char.isalnum() or char in ".-"):
            ascii_word += char
            continue
        if ascii_word:
            tokens.append(ascii_word)
            ascii_word = ""
        tokens.append(char)
    if ascii_word:
        tokens.append(ascii_word)
    return tokens


def wrap_text(draw: ImageDraw.ImageDraw, text: str, face: ImageFont.FreeTypeFont, width: int) -> list[str]:
    lines: list[str] = []
    current = ""
    for token in tokenize(text):
        candidate = current + token
        if current and draw.textlength(candidate, font=face) > width:
            lines.append(current.rstrip())
            current = token.lstrip()
        else:
            current = candidate
        while draw.textlength(current, font=face) > width:
            split = 1
            while split < len(current) and draw.textlength(current[:split + 1], font=face) <= width:
                split += 1
            lines.append(current[:split])
            current = current[split:].lstrip()
    if current:
        lines.append(current.rstrip())
    return lines


def draw_centered_line(
    draw: ImageDraw.ImageDraw,
    x: float,
    line_top: float,
    line_height: int,
    text: str,
    face: ImageFont.FreeTypeFont,
    fill: str,
    center_x: float | None = None,
) -> None:
    bounds = draw.textbbox((0, 0), text, font=face)
    y = line_top + (line_height - (bounds[3] - bounds[1])) / 2 - bounds[1]
    if center_x is None:
        draw.text((x, y), text, font=face, fill=fill)
        return
    width = draw.textlength(text, font=face)
    draw.text((center_x - width / 2, y), text, font=face, fill=fill)


def draw_rules(draw: ImageDraw.ImageDraw) -> None:
    rule = blend(COLORS["peach_line"], COLORS["note"], 0.24)
    for absolute_y in range(CARD_INNER_TOP + 17, CARD_VISIBLE_BOTTOM, BODY_LINE_HEIGHT):
        local_y = absolute_y - CARD_INNER_TOP
        draw.line((0, local_y, REGION_WIDTH, local_y), fill=rule, width=1)


def render_candidate(base: Image.Image, option: dict, body_lines: list[str]) -> Image.Image:
    candidate = base.copy().convert("RGBA")
    region = Image.new("RGBA", (REGION_WIDTH, CARD_VISIBLE_BOTTOM - CARD_INNER_TOP), COLORS["note"])
    region_draw = ImageDraw.Draw(region)
    draw_rules(region_draw)
    candidate.paste(region, (REGION_X, CARD_INNER_TOP))
    draw = ImageDraw.Draw(candidate)
    face = font(FONT_SIZE)

    if option["mode"] == "narrow":
        stamp_x = REGION_WIDTH - option["stampWidth"]
        region_draw.rectangle((stamp_x, 0, REGION_WIDTH, region.height), fill=COLORS["stamp"])
        region_draw.rectangle((stamp_x, 0, stamp_x + 2, region.height), fill=COLORS["stamp_edge"])
        candidate.paste(region, (REGION_X, CARD_INNER_TOP))
        draw = ImageDraw.Draw(candidate)
        center_x = REGION_X + stamp_x + option["stampWidth"] / 2
        stack_top = 182
        draw_centered_line(draw, 0, stack_top, 18, "7 月", font(13), COLORS["stamp_text"], center_x)
        draw_centered_line(draw, 0, stack_top + 18, 26, "15", font(20, bold=True, numeral=True), COLORS["stamp_text"], center_x)
        draw_centered_line(draw, 0, stack_top + 44, 18, "周一", font(13), COLORS["stamp_text"], center_x)
        text_top = BODY_TOP
    else:
        date_height = DATE_HEADER_HEIGHT
        draw.rounded_rectangle(
            (BODY_X, BODY_TOP, BODY_X + option["bodyTextWidth"], BODY_TOP + date_height),
            radius=5,
            fill=COLORS["stamp"],
        )
        draw_centered_line(
            draw,
            BODY_X + 8,
            BODY_TOP,
            date_height,
            "7 月 15 日 · 周一",
            font(13),
            COLORS["stamp_text"],
        )
        text_top = BODY_TOP + date_height + DATE_HEADER_GAP

    for index, line in enumerate(body_lines):
        draw_centered_line(
            draw,
            BODY_X,
            text_top + index * BODY_LINE_HEIGHT,
            BODY_LINE_HEIGHT,
            line,
            face,
            COLORS["ink"],
        )
    clipped = base.copy().convert("RGBA")
    clipped.paste(candidate.crop(ALLOWED_DIFF), (ALLOWED_DIFF[0], ALLOWED_DIFF[1]))
    return clipped


def draw_dimension(draw: ImageDraw.ImageDraw, frame_x: int, width: int, label: str) -> None:
    start = frame_x + BODY_X
    end = start + width
    y = 512
    draw.line((start, y, end, y), fill=COLORS["accent"], width=2)
    draw.line((start, y - 5, start, y + 5), fill=COLORS["accent"], width=2)
    draw.line((end, y - 5, end, y + 5), fill=COLORS["accent"], width=2)
    bounds = draw.textbbox((0, 0), label, font=font(15, bold=True))
    text_width = bounds[2] - bounds[0]
    draw.text((frame_x + FRAME_WIDTH / 2 - text_width / 2, 526), label, font=font(15, bold=True), fill=COLORS["ink"])


def build_board(base: Image.Image, candidate: Image.Image, option: dict) -> Image.Image:
    board = Image.new("RGBA", (BOARD_WIDTH, BOARD_HEIGHT), COLORS["canvas"])
    draw = ImageDraw.Draw(board)
    draw.text((24, 20), option["title"], font=font(24, bold=True), fill=COLORS["ink"])
    draw.text((24, 53), option["subtitle"], font=font(14), fill=COLORS["body"])
    draw.text((LEFT_X, 77), "现役 v79 | long-notes-day-top", font=font(13), fill=COLORS["muted"])
    draw.text((RIGHT_X, 77), "本地合成对比稿 | 非 WPF 实拍", font=font(13), fill=COLORS["accent"])
    board.paste(base, (LEFT_X, FRAME_Y))
    board.paste(candidate, (RIGHT_X, FRAME_Y))
    draw_dimension(
        draw,
        LEFT_X,
        189,
        "正文可用 189 DIP | 约 14 汉字/行",
    )
    draw_dimension(
        draw,
        RIGHT_X,
        option["bodyTextWidth"],
        f"正文可用 {option['bodyTextWidth']} DIP | 约 {option['estimatedFullWidthChars']} 汉字/行",
    )
    draw.text((LEFT_X, 558), "319 = 2 边 + 36 勾选 + 197 正文列 + 48 印记 + 36 ...", font=font(12), fill=COLORS["muted"])
    draw.text((RIGHT_X, 558), option["geometry"], font=font(12), fill=COLORS["muted"])
    draw.line((24, 590, 1088, 590), fill=COLORS["peach_line"], width=1)
    draw.text(
        (24, 607),
        "左帧逐像素引用 v79；右帧只改卡片区域。520 x 390 外壳、玩偶、命令区、36 DIP 命中槽与右侧安全区不变。",
        font=font(13),
        fill=COLORS["body"],
    )
    return board


def verify_source_binding(manifest: dict) -> None:
    for entry in manifest["sourceBinding"]["files"]:
        source = ROOT / entry["path"]
        if not source.is_file():
            raise RuntimeError(f"Missing source binding file: {entry['path']}")
        actual = sha256(source)
        if actual != entry["sha256"]:
            raise RuntimeError(f"Source binding mismatch: {entry['path']}: {actual}")


def main() -> None:
    manifest = json.loads(BASELINE_MANIFEST.read_text(encoding="utf-8"))
    capture = next(item for item in manifest["captures"] if item["captureId"] == BASELINE_CAPTURE)
    if sha256(BASELINE_PNG) != capture["pngSha256"]:
        raise RuntimeError("Baseline PNG hash does not match the v79 manifest")
    verify_source_binding(manifest)
    base = Image.open(BASELINE_PNG).convert("RGBA")
    if base.size != (FRAME_WIDTH, FRAME_HEIGHT):
        raise RuntimeError(f"Unexpected baseline dimensions: {base.size}")

    measurement = ImageDraw.Draw(Image.new("RGB", (1, 1)))
    face = font(FONT_SIZE)
    current_lines = wrap_text(measurement, NOTE_TEXT, face, 189)
    source_commit = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=ROOT,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    ).stdout.strip()

    output_manifest = {
        "schemaVersion": 1,
        "issue": 334,
        "previewVersion": PREVIEW_VERSION,
        "designOnly": True,
        "visualBaseline": False,
        "method": "Local Pillow raster composition over the committed WPF frame; no generative imagery",
        "source": {
            "commit": source_commit,
            "baselineVersion": BASELINE_VERSION,
            "captureId": BASELINE_CAPTURE,
            "manifestPath": BASELINE_MANIFEST.relative_to(ROOT).as_posix(),
            "pngPath": BASELINE_PNG.relative_to(ROOT).as_posix(),
            "pngSha256": capture["pngSha256"],
            "sourceBindingAggregateSha256": manifest["sourceBinding"]["aggregateSha256"],
            "sourceBindingFilesVerified": len(manifest["sourceBinding"]["files"]),
        },
        "comparison": {
            "boardWidth": BOARD_WIDTH,
            "boardHeight": BOARD_HEIGHT,
            "frameWidth": FRAME_WIDTH,
            "frameHeight": FRAME_HEIGHT,
            "frameY": FRAME_Y,
            "currentFrameX": LEFT_X,
            "candidateFrameX": RIGHT_X,
            "allowedCandidateDiffInFrame": ALLOWED_DIFF,
            "currentFrameMustEqualBaselinePixels": True,
        },
        "estimateMethod": "Microsoft YaHei 13 px local font metrics; full-width character estimate is floor(usable DIP / 13); not a WPF runtime measurement",
        "current": {
            "bodyColumnWidth": 197,
            "bodyTextWidth": 189,
            "estimatedFullWidthChars": 14,
            "estimatedFixtureLines": len(current_lines),
        },
        "options": [],
    }

    for option in OPTIONS:
        body_lines = wrap_text(measurement, NOTE_TEXT, face, option["bodyTextWidth"])
        candidate = render_candidate(base, option, body_lines)
        board = build_board(base, candidate, option)
        target = OUT / option["file"]
        board.save(target, format="PNG", optimize=True)
        extra_height = DATE_HEADER_HEIGHT + DATE_HEADER_GAP if option["mode"] == "top" else 0
        current_height = max(72, CARD_BORDER + 18 + len(current_lines) * BODY_LINE_HEIGHT)
        option_height = max(72, CARD_BORDER + 18 + extra_height + len(body_lines) * BODY_LINE_HEIGHT)
        record = dict(option)
        record.update(
            {
                "sha256": sha256(target),
                "width": BOARD_WIDTH,
                "height": BOARD_HEIGHT,
                "estimatedFixtureLines": len(body_lines),
                "estimatedFixtureCardHeightDelta": option_height - current_height,
                "bodyLinesPreview": body_lines,
            }
        )
        output_manifest["options"].append(record)

    (OUT / "preview-manifest.json").write_text(
        json.dumps(output_manifest, ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(json.dumps({
        "generated": [option["file"] for option in OPTIONS],
        "sourceCommit": source_commit,
        "currentLines": len(current_lines),
        "optionLines": {option["id"]: wrap_text(measurement, NOTE_TEXT, face, option["bodyTextWidth"]) for option in OPTIONS},
    }, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
