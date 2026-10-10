"""Render the sample images in samples/ from their HTML sources in samples/src/ with headless Chrome.

    py -3.14 tools/readme-assets/render-samples.py [--out samples]

The samples are original drawings (a menu, a notice, a slide, a receipt and an illustration with a caption); every shop
name, phone number, password and date in them is made up. Fonts are the ones Windows ships (Microsoft YaHei, KaiTi), so
the result depends on running this on Windows. Needs Chrome and Pillow.
"""

from __future__ import annotations

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

from PIL import Image

PROJECT = Path(__file__).resolve().parents[2]
SOURCES = PROJECT / "samples/src"

# html name -> (width, height, output file)
SAMPLES = {
    "cafe-menu": (1600, 1000, "01-cafe-menu.png"),
    "notice": (1600, 1000, "02-notice.png"),
    "meeting-slide": (1600, 900, "03-meeting-slide.png"),
    "receipt": (760, 1280, "04-receipt.png"),
    "landscape": (1600, 1000, "05-landscape.jpg"),
}


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


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--out", type=Path, default=PROJECT / "samples")
    args = parser.parse_args()
    chrome = find_chrome()
    args.out.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="jianyiimage-samples-") as scratch:
        scratch_path = Path(scratch)
        for name, (width, height, target) in SAMPLES.items():
            raw = scratch_path / f"{name}.png"
            # a throw-away profile folder: without it Chrome can attach to a browser the user is running
            subprocess.run(
                [
                    str(chrome), "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
                    f"--window-size={width},{height}", f"--user-data-dir={scratch_path / 'profile'}",
                    "--virtual-time-budget=3000", f"--screenshot={raw}", (SOURCES / f"{name}.html").as_uri(),
                ],
                check=True,
                capture_output=True,
            )
            image = Image.open(raw).convert("RGB")
            destination = args.out / target
            if target.endswith(".jpg"):
                image.save(destination, quality=90, optimize=True, subsampling=0)
            else:
                image.save(destination, optimize=True)
            print(f"{target:22} {destination.stat().st_size / 1024:6.0f} KiB  {image.size}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
