"""Builds assets/app.ico - an arrow dropping into a Switch, on the rounded
gradient tile the other apps use, in HomeNX's blue.

Frames 16-48 are 32-bit BMP, 64-256 PNG; small frames get a bolder glyph.

    python tools/make_icon.py
"""

import os
import struct
import sys
from io import BytesIO

from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
TARGET = os.path.join(HERE, "assets", "app.ico")
PREVIEW = os.path.join(HERE, "assets", "icon-preview.png")

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
BMP_UP_TO = 48
SS = 8

TOP = (74, 124, 255)
BOTTOM = (30, 40, 128)
RADIUS = 0.235
WHITE = (255, 255, 255, 255)
BLUE = (96, 200, 255, 255)     # left Joy-Con
CORAL = (255, 122, 100, 255)   # right Joy-Con
SCREEN = (24, 34, 98, 255)


def gradient(n):
    im = Image.new("RGBA", (n, n))
    px = im.load()
    for y in range(n):
        for x in range(n):
            t = (x * 0.25 + y * 0.75) / float(n - 1)
            px[x, y] = tuple(int(TOP[i] + (BOTTOM[i] - TOP[i]) * t) for i in range(3)) + (255,)
    return im


def glyph(n, bold):
    """The arrow and the console on a 256 grid."""
    im = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    s = n / 256.0

    def line(points, width):
        pts = [(x * s, y * s) for x, y in points]
        d.line(pts, fill=WHITE, width=int(width * s), joint="curve")
        r = width * s / 2.0
        for x, y in (pts[0], pts[-1]):
            d.ellipse((x - r, y - r, x + r, y + r), fill=WHITE)

    w = 30 if bold else 24
    tip = 128 if bold else 126
    line([(128, 40), (128, tip)], w)
    line([(128 - 40, tip - 40), (128, tip), (128 + 40, tip - 40)], w)

    # The console: white body, the two Joy-Cons in their colours.
    top, bottom = (164, 226) if bold else (166, 222)
    left, right, grip = 32, 224, (50 if bold else 46)
    body = Image.new("L", (n, n), 0)
    ImageDraw.Draw(body).rounded_rectangle((left * s, top * s, right * s, bottom * s), radius=20 * s, fill=255)
    for box, colour in (((left, top, right, bottom), WHITE),
                        ((left, top, left + grip, bottom), BLUE),
                        ((right - grip, top, right, bottom), CORAL)):
        part = Image.new("L", (n, n), 0)
        ImageDraw.Draw(part).rectangle([v * s for v in box], fill=255)
        layer = Image.new("RGBA", (n, n), colour)
        layer.putalpha(Image.composite(body, Image.new("L", (n, n), 0), part))
        im.alpha_composite(layer)
    if not bold:
        ImageDraw.Draw(im).rounded_rectangle(((left + grip + 8) * s, (top + 9) * s, (right - grip - 8) * s, (bottom - 9) * s),
                                             radius=5 * s, fill=SCREEN)
    return im


def master(size):
    small = size <= 32
    n = max(size, 64) * SS
    tile = gradient(n)
    shape = Image.new("L", (n, n), 0)
    ImageDraw.Draw(shape).rounded_rectangle((0, 0, n - 1, n - 1), radius=int(RADIUS * n), fill=255)

    if not small:
        glow = Image.new("L", (n, n), 0)
        ImageDraw.Draw(glow).ellipse((-n * 0.3, -n * 0.75, n * 1.3, n * 0.42), fill=46)
        glow = glow.filter(ImageFilter.GaussianBlur(n * 0.06))
        white = Image.new("RGBA", (n, n), WHITE)
        white.putalpha(glow)
        tile.alpha_composite(white)

    mark = glyph(n, small)
    if not small:
        sh = mark.getchannel("A").filter(ImageFilter.GaussianBlur(n * 0.018)).point(lambda v: v * 0.30)
        shadow = Image.new("RGBA", (n, n), (6, 12, 50, 255))
        shadow.putalpha(sh)
        tile.alpha_composite(shadow, (0, int(n * 0.012)))
    tile.alpha_composite(mark)
    tile.putalpha(shape)

    im = tile.resize((size, size), Image.LANCZOS)
    if small:
        im = im.filter(ImageFilter.UnsharpMask(radius=0.6, percent=60, threshold=2))
    return im


def png_bytes(im):
    buf = BytesIO()
    im.save(buf, format="PNG", optimize=True)
    return buf.getvalue()


def bmp_bytes(im):
    w, h = im.size
    px = im.load()
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    mask = bytearray(((w + 31) // 32) * 4 * h)
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(xor), 0, 0, 0, 0)
    return bytes(header) + bytes(xor) + bytes(mask)


def contact_sheet(frames):
    pad, slot = 12, 64
    sheet = Image.new("RGBA", (pad + len(frames) * (slot + pad), 2 * (slot + pad) + pad), (255, 255, 255, 255))
    sheet.paste(Image.new("RGBA", (sheet.width, slot + pad), (32, 32, 36, 255)), (0, slot + pad))
    for i, (size, im, _) in enumerate(frames):
        shown = im if size <= slot else im.resize((slot, slot), Image.LANCZOS)
        x = pad + i * (slot + pad) + (slot - shown.width) // 2
        sheet.alpha_composite(shown, (x, pad + (slot - shown.height) // 2))
        sheet.alpha_composite(shown, (x, slot + 2 * pad + (slot - shown.height) // 2))
    sheet.save(PREVIEW)


def main():
    frames = []
    for size in SIZES:
        im = master(size)
        frames.append((size, im, bmp_bytes(im) if size <= BMP_UP_TO else png_bytes(im)))
    offset = 6 + 16 * len(frames)
    entries = b""
    blobs = b""
    for size, _, blob in frames:
        s = 0 if size >= 256 else size
        entries += struct.pack("<BBBBHHII", s, s, 0, 0, 1, 32, len(blob), offset)
        blobs += blob
        offset += len(blob)
    with open(TARGET, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, len(frames)) + entries + blobs)
    contact_sheet(frames)
    master(256).save(os.path.join(HERE, "assets", "icon-256.png"))
    print("wrote %s (%d bytes)" % (os.path.relpath(TARGET, HERE), os.path.getsize(TARGET)))
    return 0


if __name__ == "__main__":
    sys.exit(main())
