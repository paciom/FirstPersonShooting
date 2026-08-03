"""Composites a preview capture onto an arbitrary backdrop, via a black/white pair.

The select screen's backdrop colour has changed over the project's life, so a
capture that has to MATCH an older frame (for instance to seed a second
image-to-video clip that must look like the first) cannot simply inherit
today's. Rendering the same pose twice, once on black and once on white,
recovers real coverage without an alpha channel:

    K = a*F            (on black)
    W = a*F + (1 - a)  (on white)

so (W - K) is the inverse coverage and K is already premultiplied. Compositing
is therefore backdrop*(W - K) + K — no un-premultiply, no fringing.

  python cutoutcomposite.py black.png white.png out.png --bg 5,13,25
"""
import argparse

from PIL import Image


def composite(black, white, backdrop):
    k = black.convert("RGB").load()
    w = white.convert("RGB").load()
    width, height = black.size
    out = Image.new("RGB", (width, height))
    op = out.load()
    for y in range(height):
        for x in range(width):
            kr, kg, kb = k[x, y]
            wr, wg, wb = w[x, y]
            # Per channel, because anti-aliased edges pick up slightly
            # different coverage in each after the encode rounds them.
            inv = ((wr - kr) / 255.0, (wg - kg) / 255.0, (wb - kb) / 255.0)
            op[x, y] = tuple(
                min(255, max(0, round(component + backdrop[i] * inv[i])))
                for i, component in enumerate((kr, kg, kb)))
    return out


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("black")
    parser.add_argument("white")
    parser.add_argument("out")
    parser.add_argument("--bg", default="5,13,25", help="r,g,b in 0-255")
    args = parser.parse_args()

    backdrop = tuple(int(part) for part in args.bg.split(","))
    black, white = Image.open(args.black), Image.open(args.white)
    if black.size != white.size:
        raise SystemExit("the two captures differ in size")
    composite(black, white, backdrop).save(args.out)
    print("wrote", args.out, black.size, "on", backdrop)


if __name__ == "__main__":
    main()
