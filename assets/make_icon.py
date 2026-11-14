"""Draws the app icon (two overlapping windows) and writes assets/app.ico and assets/icon-256.png.

Needs Pillow:  python assets/make_icon.py
"""
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
S = 4                     # supersampling factor
SIZE = 256 * S
LEFT = "#C96442"          # clay
RIGHT = "#2F7DEB"         # blue
GAP = 9 * S               # clear ring that separates the two windows
RADIUS = 30 * S


def card(draw, box, fill):
    draw.rounded_rectangle(box, radius=RADIUS, fill=fill)
    # a lighter title bar strip, so each shape reads as a window
    x0, y0, x1, _ = box
    draw.rounded_rectangle((x0 + 16 * S, y0 + 16 * S, x1 - 16 * S, y0 + 30 * S), radius=7 * S, fill=(255, 255, 255, 90))


img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
draw = ImageDraw.Draw(img, "RGBA")

left = (18 * S, 58 * S, 146 * S, 214 * S)
right = (110 * S, 42 * S, 238 * S, 198 * S)

card(draw, left, LEFT)
# cut a gap around the front window, then draw it
hole = (right[0] - GAP, right[1] - GAP, right[2] + GAP, right[3] + GAP)
mask = Image.new("L", (SIZE, SIZE), 0)
ImageDraw.Draw(mask).rounded_rectangle(hole, radius=RADIUS + GAP, fill=255)
img.paste((0, 0, 0, 0), (0, 0), mask)
draw = ImageDraw.Draw(img, "RGBA")
card(draw, right, RIGHT)

icon = img.resize((256, 256), Image.LANCZOS)
icon.save(HERE / "icon-256.png")
icon.save(HERE / "app.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
print("wrote", HERE / "app.ico")
