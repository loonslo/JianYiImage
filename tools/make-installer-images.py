"""Draw the installer's wizard images (the tall one on the welcome and finish pages, the small one in the page header).

Inno Setup picks the size that matches the display scale, so each image is written at 100%, 150% and 200%.
The bitmaps are not committed: tools/build-release.py generates them into the build folder.

    py -3.14 tools/make-installer-images.py --out <folder>

Needs Pillow and the Microsoft YaHei fonts that ship with Windows.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

PROJECT = Path(__file__).resolve().parents[1]
ICON = PROJECT / "src/JianYiImage.App/Assets/Jianyi.png"
FONTS = Path(os.environ.get("WINDIR", "")) / "Fonts"

LARGE = (164, 314)        # Inno Setup's design size for the welcome/finish image at 100% scale
SMALL = (55, 55)          # ... and for the page header image
SCALES = {"": 1.0, "-150": 1.5, "-200": 2.0}
TOP, BOTTOM = (22, 30, 58), (47, 83, 160)      # deep navy to blue, close to the app's accent colour


def font(name: str, size: int) -> ImageFont.FreeTypeFont:
    for candidate in (FONTS / name, FONTS / "msyh.ttc"):
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    raise SystemExit(f"font not found: {name} (looked in {FONTS})")


def gradient(size: tuple[int, int]) -> Image.Image:
    w, h = size
    image = Image.new("RGB", size)
    draw = ImageDraw.Draw(image)
    for y in range(h):
        t = y / max(1, h - 1)
        color = tuple(round(a + (b - a) * t) for a, b in zip(TOP, BOTTOM))
        draw.line((0, y, w, y), fill=color)
    return image


def centered(draw: ImageDraw.ImageDraw, text: str, fnt: ImageFont.FreeTypeFont, y: int, width: int, fill) -> None:
    left, _, right, _ = draw.textbbox((0, 0), text, font=fnt)
    draw.text(((width - (right - left)) / 2 - left, y), text, font=fnt, fill=fill)


def large_image(scale: float) -> Image.Image:
    ss = 4                                    # draw at 4x and reduce, so the text edges are clean
    w, h = (round(LARGE[0] * scale), round(LARGE[1] * scale))
    canvas = gradient((w * ss, h * ss))
    icon = Image.open(ICON).convert("RGBA")
    side = round(112 * scale * ss)
    icon = icon.resize((side, side), Image.LANCZOS)
    canvas.paste(icon, ((w * ss - side) // 2, round(46 * scale * ss)), icon)
    draw = ImageDraw.Draw(canvas)
    centered(draw, "简壹图片浏览器", font("msyhbd.ttc", round(17 * scale * ss)), round(184 * scale * ss), w * ss, (255, 255, 255))
    centered(draw, "图里的文字，划选即复制", font("msyh.ttc", round(10.5 * scale * ss)), round(214 * scale * ss), w * ss, (190, 205, 235))
    centered(draw, "JianYiImage", font("msyh.ttc", round(9 * scale * ss)), round(286 * scale * ss), w * ss, (120, 145, 200))
    return canvas.resize((w, h), Image.LANCZOS)


def small_image(scale: float) -> Image.Image:
    w, h = (round(SMALL[0] * scale), round(SMALL[1] * scale))
    canvas = Image.new("RGBA", (w, h), (255, 255, 255, 255))
    side = round(47 * scale)
    icon = Image.open(ICON).convert("RGBA").resize((side, side), Image.LANCZOS)
    canvas.alpha_composite(icon, ((w - side) // 2, (h - side) // 2))
    return canvas.convert("RGB")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    for suffix, scale in SCALES.items():
        large_image(scale).save(args.out / f"wizard-large{suffix}.bmp")
        small_image(scale).save(args.out / f"wizard-small{suffix}.bmp")
    print("wrote", ", ".join(sorted(p.name for p in args.out.glob("wizard-*.bmp"))))
    return 0


if __name__ == "__main__":
    sys.exit(main())
