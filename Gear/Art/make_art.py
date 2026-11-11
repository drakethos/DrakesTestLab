"""Draws the lab gear art: one atlas for the clipboard and glasses meshes, and the two inventory icons.

    python Gear/Art/make_art.py        (run from the repo root; writes next to this script)

Atlas layout (512 x 512, pixel rects, y down):
    paper   (0,0)-(300,420)     lined page with handwritten notes
    board   (320,0)-(512,200)   brown masonite
    clip    (320,220)-(512,300) brushed steel
    black   (320,320)-(416,416) matte black plastic (glasses frames)
    lens    (416,320)-(512,416) dark smoky lens
"""
import math
import os
import random

from PIL import Image, ImageDraw, ImageFilter, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
random.seed(7)


def font(name, size):
    for candidate in (name, "Inkfree.ttf", "segoepr.ttf", "arial.ttf"):
        try:
            return ImageFont.truetype(os.path.join("C:/Windows/Fonts", candidate), size)
        except OSError:
            continue
    return ImageFont.load_default()


def noise(img, box, amount):
    px = img.load()
    x0, y0, x1, y1 = box
    for y in range(y0, y1):
        for x in range(x0, x1):
            r, g, b, a = px[x, y]
            n = random.randint(-amount, amount)
            px[x, y] = (max(0, min(255, r + n)), max(0, min(255, g + n)), max(0, min(255, b + n)), a)


def atlas():
    img = Image.new("RGBA", (512, 512), (40, 40, 40, 255))
    d = ImageDraw.Draw(img)

    # --- paper -------------------------------------------------------------
    d.rectangle((0, 0, 299, 419), fill=(246, 241, 226, 255))
    noise(img, (0, 0, 300, 420), 5)
    d = ImageDraw.Draw(img)
    # margin line and ruling
    d.line((34, 0, 34, 419), fill=(222, 140, 140, 255), width=2)
    for y in range(44, 420, 26):
        d.line((0, y, 299, y), fill=(170, 190, 220, 255), width=1)
    # two punched holes at the top (where the clip sits)
    for x in (120, 180):
        d.ellipse((x - 5, 10, x + 5, 20), fill=(120, 110, 95, 255))

    ink = (38, 46, 78, 255)
    faint = (90, 96, 120, 255)
    hand = font("Inkfree.ttf", 22)
    small = font("Inkfree.ttf", 17)
    lines = [
        ("LAB NOTES", hand, ink, 0),
        ("piece hp 100% - ok", small, ink, 1),
        ("Drake_Clipboard", small, ink, 2),
        ("spawn: Greydwarf x3", small, ink, 3),
        ("[x] ward on  [ ] off", small, ink, 4),
        ("zdo 1:4471  owner me", small, faint, 5),
        ("fix hover text !!", small, ink, 6),
        ("chest 12/24  wood 300", small, faint, 7),
        ("~ ~ ~ ~ ~ ~ ~ ~", small, faint, 8),
        ("TODO repair wall", small, ink, 9),
        ("retest after rebuild", small, faint, 10),
        ("ok ok ok", small, ink, 11),
    ]
    for text, f, colour, row in lines:
        y = 24 + row * 26
        # each line gets its own little tilt, like real handwriting
        layer = Image.new("RGBA", (300, 40), (0, 0, 0, 0))
        ld = ImageDraw.Draw(layer)
        ld.text((44 + random.randint(-3, 6), 4), text, font=f, fill=colour)
        layer = layer.rotate(random.uniform(-1.6, 1.6), resample=Image.BICUBIC, expand=False)
        img.alpha_composite(layer, (0, y))
    d = ImageDraw.Draw(img)
    # a scribbled underline and a circled word
    for i in range(70):
        x = 44 + i * 2
        d.point((x, 50 + int(2 * math.sin(i * 0.9))), fill=ink)
    d.ellipse((40, 180, 120, 212), outline=ink, width=2)
    # a few stray marks
    for _ in range(16):
        x, y = random.randint(40, 290), random.randint(300, 410)
        d.line((x, y, x + random.randint(-12, 12), y + random.randint(-4, 4)), fill=faint, width=1)

    # --- board: brown masonite ---------------------------------------------
    d.rectangle((320, 0, 511, 199), fill=(122, 82, 48, 255))
    noise(img, (320, 0, 512, 200), 10)
    d = ImageDraw.Draw(img)
    for _ in range(120):
        x, y = random.randint(320, 511), random.randint(0, 199)
        d.point((x, y), fill=(96, 62, 34, 255))

    # --- clip: brushed steel -------------------------------------------------
    for y in range(220, 300):
        shade = 165 + int(40 * math.sin((y - 220) / 80 * math.pi))
        d.line((320, y, 511, y), fill=(shade, shade + 4, shade + 8, 255))
    noise(img, (320, 220, 512, 300), 6)
    d = ImageDraw.Draw(img)

    # --- black plastic -------------------------------------------------------
    d.rectangle((320, 320, 415, 415), fill=(22, 22, 24, 255))
    noise(img, (320, 320, 416, 416), 3)
    d = ImageDraw.Draw(img)
    # --- lens: smoky dark glass with a highlight -----------------------------
    d.rectangle((416, 320, 511, 415), fill=(34, 38, 46, 255))
    for i in range(30):
        d.line((430 + i, 330, 470 + i, 400), fill=(60 + i, 66 + i, 78 + i, 255))
    img.save(os.path.join(HERE, "gear_atlas.png"))


def rounded(d, box, r, **kw):
    d.rounded_rectangle(box, radius=r, **kw)


def clipboard_icon():
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    rounded(d, (12, 6, 52, 60), 4, fill=(118, 78, 44, 255), outline=(70, 44, 22, 255), width=2)
    d.rectangle((16, 14, 48, 56), fill=(245, 240, 225, 255))
    for y in (24, 31, 38, 45):
        d.line((20, y, 44 - (y % 11), y), fill=(60, 70, 110, 255), width=2)
    rounded(d, (24, 2, 40, 14), 3, fill=(190, 194, 200, 255), outline=(110, 114, 120, 255), width=1)
    img.save(os.path.join(HERE, "clipboard_icon.png"))


def glasses_icon():
    img = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    black = (18, 18, 20, 255)
    lens = (70, 80, 100, 220)
    rounded(d, (4, 22, 29, 42), 6, fill=lens, outline=black, width=3)
    rounded(d, (35, 22, 60, 42), 6, fill=lens, outline=black, width=3)
    d.line((29, 30, 35, 30), fill=black, width=3)
    d.line((4, 28, 0, 24), fill=black, width=3)
    d.line((60, 28, 64, 24), fill=black, width=3)
    d.line((10, 27, 18, 25), fill=(200, 210, 230, 160), width=2)
    d.line((41, 27, 49, 25), fill=(200, 210, 230, 160), width=2)
    img.save(os.path.join(HERE, "glasses_icon.png"))


if __name__ == "__main__":
    atlas()
    clipboard_icon()
    glasses_icon()
    print("wrote gear_atlas.png, clipboard_icon.png, glasses_icon.png in", HERE)
