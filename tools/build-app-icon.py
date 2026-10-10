"""Draw the Jianyi app icon once and write every shipped copy of it.

One artwork definition (flat paths on a 256 x 256 grid) feeds all outputs, so they cannot drift apart:

    src/JianYiImage.App/Assets/Jianyi.svg        readable vector source
    src/JianYiImage.App/Assets/Jianyi.png        512 px preview (the publish folder only carries the ICO)
    src/JianYiImage.App/Assets/Jianyi.ico        16-256 px Windows icon, every frame drawn at its own size
    src/JianYiImage.App/Controls/BrandMark.xaml  vector mark for the title bar and the empty state

Two cuts of the same design: `full` (fanned polaroids, heart, shadow) for >= 32 px and `compact`
(one orange card behind a front card laid out on whole pixels) for <= 24 px, where the fan turns
into a muddy bucket shape.

Edges: every size is drawn at 8-32x supersampling and reduced with a box filter (exact area average,
no ringing). Frames are never resized from one master with LANCZOS. Requires Pillow only; nothing is
added to the viewer application.

    py -3.14 tools/build-app-icon.py            regenerate every output
    py -3.14 tools/build-app-icon.py --check    exit 1 when a committed output is stale
"""

from __future__ import annotations

import argparse
import io
import math
import sys
from pathlib import Path
from typing import NamedTuple

from PIL import Image

APP = Path(__file__).resolve().parents[1] / "src" / "JianYiImage.App"
SVG_PATH = APP / "Assets" / "Jianyi.svg"
PNG_PATH = APP / "Assets" / "Jianyi.png"
ICO_PATH = APP / "Assets" / "Jianyi.ico"
XAML_PATH = APP / "Controls" / "BrandMark.xaml"

ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
COMPACT_MAX = 24
PNG_SIZE = 512

INK = "#1E2B57"
WHITE = "#FFFFFF"
SHADOW = "#1E2B5733"
SKY = "#6EC6FF"
SUN = "#FFC93E"
SUN_HALO = "#FFE69A"
HILL_BACK = "#37B873"
HILL_FRONT = "#69DB8F"
PINK = "#FF8FA8"
ORANGE = "#FFA94D"
HEART_RED = "#FF6B81"

Cmds = list[tuple]  # ("M", x, y) | ("L", x, y) | ("C", x1, y1, x2, y2, x, y) | ("Z",)
K = 0.5522847498307936  # cubic Bezier circle constant


class Shape(NamedTuple):
    cmds: Cmds
    fill: str  # #RRGGBB or #RRGGBBAA


# --------------------------------------------------------------------------- artwork
def full() -> list[Shape]:
    shapes: list[Shape] = []

    def polaroid(deg: float, cx: float, cy: float, window: str) -> None:
        shapes.extend(_card(cx, cy, 150, 168, deg, 16, 7, WHITE))
        shapes.append(Shape(place(rrect(-63, -72, 126, 116, 9), deg, cx, cy), window))

    polaroid(-15, 106, 132, PINK)
    polaroid(14, 152, 128, ORANGE)
    shapes.append(Shape(place(rrect(-82, -91, 164, 182, 23), -2, 128, 144), SHADOW))
    polaroid(-2, 128, 134, SKY)

    def scene(cmds: Cmds) -> Cmds:
        return place(cmds, -2, 128, 134)

    heart = [("M", 0, 12), ("C", -5, 7, -15, 1, -15, -7), ("C", -15, -14, -7, -16, 0, -9),
             ("C", 7, -16, 15, -14, 15, -7), ("C", 15, 1, 5, 7, 0, 12), ("Z",)]
    shapes += [
        Shape(scene(circle(30, -38, 25)), SUN_HALO),
        Shape(scene(circle(30, -38, 16)), SUN),
        Shape(scene(ground(top_edge([(-63, 8), (-34, -12), (-4, -6), (24, 12), (63, 22)]), -63, 63, 44, 9)), HILL_BACK),
        Shape(scene(ground(top_edge([(-63, 30), (-22, 26), (26, 10), (63, 4)]), -63, 63, 44, 9)), HILL_FRONT),
        Shape(scene(move(heart, 0, 64)), HEART_RED),
    ]
    return shapes


def compact() -> list[Shape]:
    """16 design units = 1 px at 16 px, so the front card and its window sit on whole pixels."""
    # No ink outline on the back card: on the dark title bar it would only stain the 1-2 px orange edge brown.
    # The front card's outline is exactly 1 px: a thinner one blends with the orange into a muddy brown line.
    shapes = [Shape(place(rrect(-72, -84, 144, 168, 16), 6, 156, 104), ORANGE)]
    shapes += _card(112, 136, 160, 176, 0, 16, 16, WHITE)
    wx, wy, ww, wh = 48, 64, 128, 112
    shapes += [
        Shape(rrect(wx, wy, ww, wh, 8), SKY),
        # A circle inscribed in a 2 x 2 px block covers only 78% of each pixel (dull yellow); a rounded block does not.
        Shape(rrect(wx + ww - 48, wy + 16, 32, 32, 8), SUN),
        Shape(ground(top_edge([(wx, 132), (wx + 32, 112), (wx + 64, 118), (wx + 96, 136), (wx + ww, 142)]),
                     wx, wx + ww, wy + wh, 8), HILL_BACK),
        Shape(ground(top_edge([(wx, 156), (wx + 51, 148), (wx + 96, 128), (wx + ww, 124)]),
                     wx, wx + ww, wy + wh, 8), HILL_FRONT),
    ]
    return shapes


def _card(cx: float, cy: float, w: float, h: float, deg: float, radius: float, outline: float, fill: str) -> list[Shape]:
    """Ink outline plus flat fill, rotated about the card centre."""
    return [
        Shape(place(rrect(-w / 2 - outline, -h / 2 - outline, w + 2 * outline, h + 2 * outline, radius + outline),
                    deg, cx, cy), INK),
        Shape(place(rrect(-w / 2, -h / 2, w, h, radius), deg, cx, cy), fill),
    ]


# --------------------------------------------------------------------------- path building
def rrect(x: float, y: float, w: float, h: float, r: float) -> Cmds:
    k = r * K
    return [
        ("M", x + r, y), ("L", x + w - r, y), ("C", x + w - r + k, y, x + w, y + r - k, x + w, y + r),
        ("L", x + w, y + h - r), ("C", x + w, y + h - r + k, x + w - r + k, y + h, x + w - r, y + h),
        ("L", x + r, y + h), ("C", x + r - k, y + h, x, y + h - r + k, x, y + h - r),
        ("L", x, y + r), ("C", x, y + r - k, x + r - k, y, x + r, y), ("Z",),
    ]


def circle(cx: float, cy: float, r: float) -> Cmds:
    k = r * K
    return [
        ("M", cx + r, cy), ("C", cx + r, cy + k, cx + k, cy + r, cx, cy + r),
        ("C", cx - k, cy + r, cx - r, cy + k, cx - r, cy), ("C", cx - r, cy - k, cx - k, cy - r, cx, cy - r),
        ("C", cx + k, cy - r, cx + r, cy - k, cx + r, cy), ("Z",),
    ]


def top_edge(points: list[tuple[float, float]], k: float = 1 / 6) -> Cmds:
    """Smooth (Catmull-Rom) curve through the points, as M + C commands."""
    cmds: Cmds = [("M", *points[0])]
    for i in range(len(points) - 1):
        p0 = points[max(i - 1, 0)]
        p1, p2 = points[i], points[i + 1]
        p3 = points[min(i + 2, len(points) - 1)]
        cmds.append(("C", p1[0] + (p2[0] - p0[0]) * k, p1[1] + (p2[1] - p0[1]) * k,
                     p2[0] - (p3[0] - p1[0]) * k, p2[1] - (p3[1] - p1[1]) * k, p2[0], p2[1]))
    return cmds


def ground(top: Cmds, x0: float, x1: float, ybot: float, r: float) -> Cmds:
    """Area under `top` (left edge x0 to right edge x1), closed along a bottom with rounded corners r.

    Used for hills inside a rounded window, so no clipping is needed in SVG or XAML."""
    assert abs(top[-1][-2] - x1) < 1e-6, "top edge must end on the right edge"
    k = r * K
    return top + [
        ("L", x1, ybot - r), ("C", x1, ybot - r + k, x1 - r + k, ybot, x1 - r, ybot),
        ("L", x0 + r, ybot), ("C", x0 + r - k, ybot, x0, ybot - r + k, x0, ybot - r), ("Z",),
    ]


def place(cmds: Cmds, deg: float, tx: float, ty: float) -> Cmds:
    """Rotate about the origin (clockwise, y down), then translate."""
    ca, sa = math.cos(math.radians(deg)), math.sin(math.radians(deg))
    return _map(cmds, lambda x, y: (x * ca - y * sa + tx, x * sa + y * ca + ty))


def move(cmds: Cmds, dx: float, dy: float) -> Cmds:
    return _map(cmds, lambda x, y: (x + dx, y + dy))


def _map(cmds: Cmds, fn) -> Cmds:
    out: Cmds = []
    for op, *p in cmds:
        out.append((op, *[v for i in range(0, len(p), 2) for v in fn(p[i], p[i + 1])]))
    return out


# --------------------------------------------------------------------------- SVG / XAML
def rgba(color: str) -> tuple[int, int, int, int]:
    h = color.lstrip("#") + ("ff" if len(color.lstrip("#")) == 6 else "")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4, 6))  # type: ignore[return-value]


def _num(v: float) -> str:
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s in ("", "-0") else s


def path_data(cmds: Cmds) -> str:
    return " ".join(
        "Z" if op == "Z" else op + " ".join(f"{_num(p[i])},{_num(p[i + 1])}" for i in range(0, len(p), 2))
        for op, *p in cmds)


def to_svg(shapes: list[Shape]) -> str:
    lines = ['<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256">',
             "  <title>简壹图片浏览器</title>"]
    for s in shapes:
        r, g, b, a = rgba(s.fill)
        opacity = "" if a == 255 else f' fill-opacity="{_num(a / 255)}"'
        lines.append(f'  <path d="{path_data(s.cmds)}" fill="#{r:02X}{g:02X}{b:02X}"{opacity}/>')
    return "\n".join(lines + ["</svg>"]) + "\n"


def to_xaml(full_cut: list[Shape], compact_cut: list[Shape]) -> str:
    def paths(shapes: list[Shape]) -> str:
        out = []
        for s in shapes:
            r, g, b, a = rgba(s.fill)
            out.append(f'        <Path Data="{path_data(s.cmds)}" Fill="#{a:02X}{r:02X}{g:02X}{b:02X}" />')
        return "\n".join(out)

    return f"""<?xml version="1.0" encoding="utf-8"?>
<!-- Generated by tools/build-app-icon.py from the same artwork as Assets/Jianyi.svg. Edit the artwork there. -->
<UserControl x:Class="JianYiImage.App.Controls.BrandMark"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Viewbox Stretch="Uniform">
    <Grid Width="256" Height="256">
      <Canvas x:Name="FullArt" Width="256" Height="256">
{paths(full_cut)}
      </Canvas>
      <Canvas x:Name="CompactArt" Width="256" Height="256" Visibility="Collapsed">
{paths(compact_cut)}
      </Canvas>
    </Grid>
  </Viewbox>
</UserControl>
"""


# --------------------------------------------------------------------------- rasteriser
def _outline(cmds: Cmds, k: float) -> list[tuple[float, float]]:
    """Flatten one closed subpath into sample-space points (about 4 samples per piece)."""
    pts: list[tuple[float, float]] = []
    x = y = 0.0
    for op, *p in cmds:
        if op in ("M", "L"):
            x, y = p[0] * k, p[1] * k
            pts.append((x, y))
        elif op == "C":
            x1, y1, x2, y2, x3, y3 = (v * k for v in p)
            length = math.hypot(x1 - x, y1 - y) + math.hypot(x2 - x1, y2 - y1) + math.hypot(x3 - x2, y3 - y2)
            steps = max(8, math.ceil(length / 4))
            for i in range(1, steps + 1):
                t = i / steps
                u = 1 - t
                pts.append((u**3 * x + 3 * u * u * t * x1 + 3 * u * t * t * x2 + t**3 * x3,
                            u**3 * y + 3 * u * u * t * y1 + 3 * u * t * t * y2 + t**3 * y3))
            x, y = x3, y3
    return pts


def _fill_mask(pts: list[tuple[float, float]], left: int, top: int, width: int, height: int) -> Image.Image:
    """Even-odd scanline fill that tests sample centres.

    Pillow's own polygon fill truncates coordinates and includes both end samples, which grows every shape
    by a sample (12% of a pixel at 8x). Testing centres keeps edges on whole sample boundaries exact."""
    edges = []
    for i, (x0, y0) in enumerate(pts):
        x1, y1 = pts[(i + 1) % len(pts)]
        if y0 == y1:
            continue
        if y0 > y1:
            x0, y0, x1, y1 = x1, y1, x0, y0
        edges.append((y0, y1, x0, (x1 - x0) / (y1 - y0)))
    edges.sort()
    buf = bytearray(width * height)
    active: list[tuple] = []
    nxt = 0
    for row in range(height):
        yc = top + row + 0.5
        while nxt < len(edges) and edges[nxt][0] <= yc:
            active.append(edges[nxt])
            nxt += 1
        active = [e for e in active if e[1] > yc]
        xs = sorted(e[2] + (yc - e[0]) * e[3] for e in active)
        for a, b in zip(xs[::2], xs[1::2]):
            first = max(0, math.ceil(a - 0.5) - left)
            last = min(width - 1, math.ceil(b - 0.5) - 1 - left)
            if last >= first:
                buf[row * width + first:row * width + last + 1] = b"\xff" * (last - first + 1)
    return Image.frombytes("L", (width, height), bytes(buf))


def render(shapes: list[Shape], px: int) -> Image.Image:
    """Draw at `px` x `px`. Supersampled, then reduced with a box filter (premultiplied by Pillow)."""
    ss = 32 if px <= 128 else 16 if px <= 256 else 8
    size, k = px * ss, px * ss / 256
    canvas = Image.new("RGBA", (size, size))
    for shape in shapes:
        pts = _outline(shape.cmds, k)
        left = max(0, math.floor(min(x for x, _ in pts)) - 2)
        top = max(0, math.floor(min(y for _, y in pts)) - 2)
        right = min(size, math.ceil(max(x for x, _ in pts)) + 3)
        bottom = min(size, math.ceil(max(y for _, y in pts)) + 3)
        mask = _fill_mask(pts, left, top, right - left, bottom - top)
        r, g, b, a = rgba(shape.fill)
        if a < 255:
            mask = mask.point(lambda v: v * a // 255)
        layer = Image.new("RGBA", mask.size, (r, g, b, 0))
        layer.putalpha(mask)
        canvas.alpha_composite(layer, (left, top))
    return canvas.reduce(ss)


# --------------------------------------------------------------------------- outputs
def ico_bytes(frames: dict[int, Image.Image]) -> bytes:
    sizes = sorted(frames)
    buffer = io.BytesIO()
    frames[sizes[-1]].save(buffer, format="ICO", sizes=[(s, s) for s in sizes],
                           append_images=[frames[s] for s in sizes[:-1]])
    return buffer.getvalue()


def ico_frames(data: bytes) -> dict[int, bytes]:
    ico = Image.open(io.BytesIO(data))
    return {w: ico.ico.getimage((w, h)).convert("RGBA").tobytes() for w, h in ico.ico.sizes()}


def build() -> dict:
    full_cut, compact_cut = full(), compact()
    return {
        "svg": to_svg(full_cut),
        "xaml": to_xaml(full_cut, compact_cut),
        "png": render(full_cut, PNG_SIZE),
        "ico": ico_bytes({s: render(compact_cut if s <= COMPACT_MAX else full_cut, s) for s in ICO_SIZES}),
    }


def write(outputs: dict) -> None:
    SVG_PATH.write_text(outputs["svg"], encoding="utf-8", newline="\r\n")
    XAML_PATH.write_text(outputs["xaml"], encoding="utf-8", newline="\r\n")
    outputs["png"].save(PNG_PATH)
    ICO_PATH.write_bytes(outputs["ico"])
    print("Wrote", ", ".join(p.relative_to(APP.parents[1]).as_posix() for p in (SVG_PATH, PNG_PATH, ICO_PATH, XAML_PATH)))
    print("ICO frames:", ", ".join(str(s) for s in ICO_SIZES))


def stale(outputs: dict) -> list[Path]:
    """Compare text byte-for-byte (newline-normalised) and images by decoded pixels."""
    def text(path: Path) -> str | None:
        return path.read_text(encoding="utf-8").replace("\r\n", "\n") if path.exists() else None

    bad = [p for p, new in ((SVG_PATH, outputs["svg"]), (XAML_PATH, outputs["xaml"])) if text(p) != new]
    if not PNG_PATH.exists() or Image.open(PNG_PATH).convert("RGBA").tobytes() != outputs["png"].tobytes():
        bad.append(PNG_PATH)
    if not ICO_PATH.exists() or ico_frames(ICO_PATH.read_bytes()) != ico_frames(outputs["ico"]):
        bad.append(ICO_PATH)
    return bad


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--check", action="store_true", help="exit 1 when a committed output is stale")
    args = parser.parse_args()
    outputs = build()
    if not args.check:
        write(outputs)
        return 0
    bad = stale(outputs)
    for path in bad:
        print("STALE", path.relative_to(APP.parents[1]).as_posix())
    print("All icon outputs are current." if not bad else "Run: py -3.14 tools/build-app-icon.py")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
