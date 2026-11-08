"""Decode the generated GIFs with Pillow and check them against what the
encoder was told to write. Pillow is an independent decoder, so this catches
LZW and block-structure bugs my own code would agree with itself about."""
import json, sys
from PIL import Image

man = json.load(open("out-cs/manifest.json"))
fails = []


def check(name, exp):
    path = "out-cs/%s.gif" % name
    im = Image.open(path)
    pal = exp["palette"]
    transparent = exp.get("transparent")

    if (im.width, im.height) != (exp["width"], exp["height"]):
        fails.append("%s: size %s, expected %s" % (name, (im.width, im.height),
                                                   (exp["width"], exp["height"])))
        return

    # Pillow reseeks one Image object, so each frame must be copied out while
    # it is current; list(ImageSequence.Iterator(im)) is N aliases of the last.
    frames, durations = [], []
    try:
        for i in range(len(exp["frames"]) + 1):
            im.seek(i)
            frames.append(im.convert("RGBA").copy())
            durations.append(im.info.get("duration"))
    except EOFError:
        pass
    if len(frames) != len(exp["frames"]):
        fails.append("%s: %d frames decoded, expected %d" % (name, len(frames), len(exp["frames"])))
        return

    for fi, (frame, want) in enumerate(zip(frames, exp["frames"])):
        dur = durations[fi]
        want_dur = exp["delays"][fi]
        if dur != want_dur:
            fails.append("%s f%d: duration %sms, expected %sms" % (name, fi, dur, want_dur))

        px = frame.load()
        bad = 0
        first = None
        for y in range(im.height):
            for x in range(im.width):
                idx = want[y * im.width + x]
                got = px[x, y]
                if transparent is not None and idx == transparent:
                    ok = got[3] == 0
                    expect_desc = "transparent"
                else:
                    ok = got[:3] == tuple(pal[idx])
                    expect_desc = tuple(pal[idx])
                if not ok:
                    bad += 1
                    if first is None:
                        first = "(%d,%d) got %s expected %s (index %d)" % (x, y, got, expect_desc, idx)
        if bad:
            fails.append("%s f%d: %d/%d pixels wrong; first %s"
                         % (name, fi, bad, im.width * im.height, first))
        else:
            print("  %s f%d: %d px exact, delay %sms" % (name, fi, im.width * im.height, dur))

    if "source" in exp:
        # Quantiser accuracy: mean absolute error per channel against the original.
        total, n = 0, 0
        for fi, src in enumerate(exp["source"]):
            px = frames[fi].load()
            for y in range(im.height):
                for x in range(im.width):
                    o = (y * im.width + x) * 4
                    got = px[x, y]
                    for c in range(3):
                        total += abs(got[c] - src[o + c])
                        n += 1
        mae = total / n
        print("  quant: mean abs error %.2f/255 over %d channel samples" % (mae, n))
        if mae > 12:
            fails.append("quant: mean abs error %.2f too high" % mae)


for name, exp in man.items():
    print("%s:" % name)
    check(name, exp)

print()
if fails:
    print("FAIL (%d)" % len(fails))
    for f in fails:
        print("  - " + f)
    sys.exit(1)
print("all checks passed")
