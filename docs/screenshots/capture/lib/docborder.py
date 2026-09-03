"""Stroke a hairline frame around a screenshot whose own edge does not show on the page.

A shot whose border colour matches the page it lands on has no visible boundary, so the reader
cannot see where the picture stops. macOS gives a decorated window a hairline for free; an
undecorated one, or a region crop, has none.

Two choices worth keeping:

  * The canvas GROWS by one pixel a side and the original is pasted inside it, so no pixel of the
    capture is overwritten. Every output is exactly +2 x +2 of its input.
  * The grey is DERIVED from the image's own edge luminance rather than fixed. A fixed light grey
    (the #BDBDBD macOS strokes windows with) is invisible against a near-white window edge on a
    near-white page, which is the exact case this exists for: measured 243 on this project's two
    settings-style windows.

Run through the capture scripts, never by hand: a border applied by hand is silently lost the next
time the shot is captured.

Usage: python docborder.py <image.png> [more.png ...]
"""
import sys
from PIL import Image

# How far the hairline sits from the image's own edge, in luminance. Clamped to a mid grey: a light
# one vanishes on a light page and a dark one on a dark page, and the border has to carry the
# contrast whenever the image itself barely differs from the page.
DISTANCE = 130
MIN_LUM, MAX_LUM = 96, 176


def edge_luminance(im):
    """Median luminance of the opaque pixels on the image's own one-pixel frame."""
    px, (w, h) = im.load(), im.size
    lums = []
    for x in range(0, w, 7):
        for y in (0, h - 1):
            r, g, b, a = px[x, y]
            if a > 200:
                lums.append(0.2126 * r + 0.7152 * g + 0.0722 * b)
    for y in range(0, h, 7):
        for x in (0, w - 1):
            r, g, b, a = px[x, y]
            if a > 200:
                lums.append(0.2126 * r + 0.7152 * g + 0.0722 * b)
    lums.sort()
    return lums[len(lums) // 2] if lums else 255.0


def border_grey(edge):
    away = edge - DISTANCE if edge > 127 else edge + DISTANCE
    return int(round(min(MAX_LUM, max(MIN_LUM, away))))


def add_border(path):
    im = Image.open(path).convert("RGBA")
    w, h = im.size
    if min(im.getpixel(c)[3] for c in ((0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1))) < 255:
        # Transparent rounded corners: a square frame would box the corners and leave page
        # background inside it. Such an image needs a border tracing its shape, not this.
        print(f"skipped (transparent corners): {path}")
        return
    v = border_grey(edge_luminance(im))
    out = Image.new("RGBA", (w + 2, h + 2), (v, v, v, 255))
    out.paste(im, (1, 1))
    out.save(path)
    print(f"bordered {path}: {w}x{h} -> {w + 2}x{h + 2}, #{v:02x}{v:02x}{v:02x}")


for p in sys.argv[1:]:
    add_border(p)
