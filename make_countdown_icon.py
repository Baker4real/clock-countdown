from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
size = 256
image = Image.new("RGBA", (size, size), (0, 0, 0, 0))
draw = ImageDraw.Draw(image)

navy = "#172033"
cream = "#FBFAF8"
coral = "#D85C4B"
muted = "#AEB8CA"

draw.rounded_rectangle((10, 10, 246, 246), radius=54, fill=navy)
draw.ellipse((43, 43, 213, 213), fill=cream)

center = (128, 128)
for angle in range(0, 360, 30):
    import math
    outer = 76
    inner = 66 if angle % 90 else 62
    radians = math.radians(angle - 90)
    x1 = center[0] + inner * math.cos(radians)
    y1 = center[1] + inner * math.sin(radians)
    x2 = center[0] + outer * math.cos(radians)
    y2 = center[1] + outer * math.sin(radians)
    draw.line((x1, y1, x2, y2), fill=muted, width=5)

draw.line((128, 128, 128, 78), fill=navy, width=13)
draw.line((128, 128, 170, 150), fill=coral, width=13)
draw.ellipse((116, 116, 140, 140), fill=coral)

png_path = root / "countdown-icon.png"
ico_path = root / "countdown-icon.ico"
image.save(png_path)
image.save(ico_path, format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print(ico_path)
