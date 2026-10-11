"""Build the folder of demo images the promo video is recorded on.

    py -3.14 tools/promo-video/make_demo.py --out <empty scratch folder>

Renders the two portrait pictures in demo-src/ with headless Chrome, draws a small looping GIF, and copies three of the
README samples next to them. Every name, address, phone number and password in them is made up. The file names carry a
number so the app's natural sort walks them in this order; they are what the title bar shows during the recording.
"""

from __future__ import annotations

import argparse
import math
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
SAMPLES = PROJECT / "samples"

RENDERED = {  # html in demo-src -> (width, height, file name in the demo folder)
    "chat": (1080, 1700, "01-群聊截图.png"),
    "notes": (1080, 1700, "02-读书笔记.png"),
}
COPIED = {  # README sample -> file name in the demo folder
    "04-receipt.png": "03-购物小票.png",
    "05-landscape.jpg": "04-山海之间.jpg",
    "03-meeting-slide.png": "05-会议纪要.png",
    "01-cafe-menu.png": "07-咖啡菜单.png",
}
GIF_NAME = "06-动图.gif"


def find_chrome() -> Path:
    candidates = [shutil.which("chrome"), shutil.which("chrome.exe")]
    for key in ("ProgramFiles", "ProgramFiles(x86)", "LOCALAPPDATA"):
        base = os.environ.get(key)
        if base:
            candidates.append(str(Path(base) / "Google/Chrome/Application/chrome.exe"))
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return Path(candidate)
    raise SystemExit("Chrome was not found")


def render_html(chrome: Path, scratch: Path, name: str, width: int, height: int, target: Path) -> None:
    raw = scratch / f"{name}.png"
    # a throw-away profile folder: without it Chrome can attach to a browser the user is running
    subprocess.run(
        [
            str(chrome), "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
            f"--window-size={width},{height}", f"--user-data-dir={scratch / 'profile'}", "--virtual-time-budget=3000",
            f"--screenshot={raw}", (HERE / "demo-src" / f"{name}.html").as_uri(),
        ],
        check=True,
        capture_output=True,
    )
    Image.open(raw).convert("RGB").save(target, optimize=True)


def draw_gif(target: Path) -> None:
    """Five balls in the icon's colours bouncing in a wave; 24 frames, 50 ms each."""
    size, ss, frames_count = 640, 4, 24
    colours = ["#6EC6FF", "#FFC93E", "#69DB8F", "#FF8FA8", "#FFA94D"]
    ground, radius, height = 430, 40, 190
    background = Image.new("RGB", (size * ss, size * ss))
    top, bottom = (234, 246, 255), (255, 255, 255)
    bg_draw = ImageDraw.Draw(background)
    for y in range(size * ss):
        t = y / (size * ss - 1)
        bg_draw.line([(0, y), (size * ss, y)], fill=tuple(round(a + (b - a) * t) for a, b in zip(top, bottom)))
    frames = []
    for f in range(frames_count):
        frame = background.copy()
        shadow_layer = Image.new("L", frame.size, 0)
        shadow_draw = ImageDraw.Draw(shadow_layer)
        balls = []
        for i, colour in enumerate(colours):
            phase = 2 * math.pi * f / frames_count - i * 0.55
            lift = abs(math.sin(phase))                        # 0 on the ground, 1 at the top
            squash = max(0.0, 1 - lift / 0.18) * 0.22           # flatten only right at the bounce
            cx = (128 + i * 96) * ss
            ry = radius * (1 - squash) * ss
            rx = radius * (1 + squash * 0.8) * ss
            cy = (ground - radius * (1 - squash) - lift * height) * ss
            balls.append((cx, cy, rx, ry, colour))
            spread = (1 - 0.45 * lift) * radius * 1.05 * ss
            shadow_draw.ellipse([cx - spread, ground * ss + 4 * ss, cx + spread, ground * ss + 18 * ss], fill=int(70 * (1 - 0.6 * lift)))
        shadow_layer = shadow_layer.filter(ImageFilter.GaussianBlur(4 * ss))
        frame.paste(Image.new("RGB", frame.size, (30, 43, 87)), mask=shadow_layer)
        draw = ImageDraw.Draw(frame)
        for cx, cy, rx, ry, colour in balls:
            draw.ellipse([cx - rx, cy - ry, cx + rx, cy + ry], fill=colour, outline="#1E2B57", width=5 * ss)
            draw.ellipse([cx - rx * 0.45, cy - ry * 0.62, cx - rx * 0.05, cy - ry * 0.25], fill="#FFFFFF")
        frames.append(frame.reduce(ss).convert("P", palette=Image.Palette.ADAPTIVE, colors=128))
    frames[0].save(target, save_all=True, append_images=frames[1:], duration=50, loop=0, disposal=1, optimize=False)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    out = args.out.resolve()
    if out.exists() and any(out.iterdir()):
        parser.error("--out must be empty")
    out.mkdir(parents=True, exist_ok=True)
    chrome = find_chrome()
    with tempfile.TemporaryDirectory(prefix="jianyiimage-promo-demo-") as scratch:
        for name, (width, height, target) in RENDERED.items():
            render_html(chrome, Path(scratch), name, width, height, out / target)
    for source, target in COPIED.items():
        shutil.copy2(SAMPLES / source, out / target)
    draw_gif(out / GIF_NAME)
    for produced in sorted(out.iterdir()):
        with Image.open(produced) as image:
            print(f"{produced.name:16} {image.size[0]}x{image.size[1]} {produced.stat().st_size / 1024:6.0f} KiB")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
