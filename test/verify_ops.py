"""Check the GIF written by `Flint.exe --opstest` using Pillow, an independent
decoder. Run from this folder after make_assets.py and the opstest."""
import hashlib
import sys
from PIL import Image

PATH = sys.argv[1] if len(sys.argv) > 1 else "out-cs/walk.gif"
EXPECTED_FRAMES = 6

im = Image.open(PATH)
frames, delays = [], []
try:
    for i in range(EXPECTED_FRAMES + 1):
        im.seek(i)
        frames.append(im.convert("RGBA").copy())
        delays.append(im.info.get("duration"))
except EOFError:
    pass

fails = []


def check(ok, what):
    print(("  ok   " if ok else "  FAIL ") + what)
    if not ok:
        fails.append(what)


print("GIF checks on", PATH)
check(len(frames) == EXPECTED_FRAMES, f"{len(frames)} frames decoded, expected {EXPECTED_FRAMES}")
check(all(d == 90 for d in delays), f"all delays are 90ms ({delays})")
check(im.info.get("loop") == 0, f"loops forever (loop={im.info.get('loop')})")

hashes = [hashlib.md5(f.tobytes()).hexdigest()[:8] for f in frames]
check(len(set(hashes)) == len(hashes),
      f"all {len(hashes)} frames differ ({len(set(hashes))} distinct)")

f0 = frames[0]
check(f0.getpixel((0, 0))[3] == 0, "top-left corner is transparent")

w, h = f0.size
opaque = sum(1 for p in f0.get_flattened_data() if p[3] > 0)
check(0 < opaque < w * h, f"frame 0 is partly opaque ({opaque} of {w * h} px)")

# No magenta may survive the colour key, in any frame.
stray = 0
for f in frames:
    for r, g, b, a in f.get_flattened_data():
        if a and r > 200 and g < 60 and b > 200:
            stray += 1
check(stray == 0, f"no magenta pixels survive ({stray} found)")

# The sprite moves vertically, so the topmost opaque row must vary across frames.
tops = []
for f in frames:
    px = f.load()
    top = next((y for y in range(h) for x in range(w) if px[x, y][3] > 0), None)
    tops.append(top)
check(len(set(tops)) > 1, f"sprite moves between frames (top rows {tops})")

print()
if fails:
    print(f"FAIL ({len(fails)})")
    sys.exit(1)
print("all GIF checks passed")
