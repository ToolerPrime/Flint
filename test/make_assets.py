"""Generate the test sprite sheet.

Frame heights are deliberately all distinct: sampling sin() at six evenly
spaced points gives only three distinct values, which would let a bug that
drops or duplicates frames pass unnoticed.
"""
from PIL import Image, ImageDraw
import os

W = H = 48
N = 6
OFFSETS = [-7, -4, -1, 2, 5, 8]       # six distinct vertical positions
assert len(set(OFFSETS)) == N

sheet = Image.new("RGB", (W * N, H), (255, 0, 255))   # flat magenta background
d = ImageDraw.Draw(sheet)
for i, dy in enumerate(OFFSETS):
    cy = H // 2 + dy
    ox = i * W
    d.ellipse([ox + 12, cy - 11, ox + 36, cy + 11], fill=(108, 194, 74), outline=(40, 90, 30))
    d.ellipse([ox + 18, cy - 5, ox + 22, cy - 1], fill=(20, 20, 20))
    d.ellipse([ox + 26, cy - 5, ox + 30, cy - 1], fill=(20, 20, 20))
    d.arc([ox + 18, cy + 1, ox + 30, cy + 8], 0, 180, fill=(30, 30, 30), width=2)

os.makedirs("assets", exist_ok=True)
sheet.save("assets/walk_sheet.png")
print("assets/walk_sheet.png", sheet.size, "frames:", N)
