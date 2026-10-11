"""Turn the recorded clips into the data file the composition reads.

    py -3.14 tools/promo-video/build_data.py --rec <record.py work folder> [--out <rec>/data.js]

Expects <rec>/<scene>.json from record.py and the frames of every clip extracted to <rec>/frames/<scene>/00001.jpg...
(record.py's lossless .mkv files, e.g. ffmpeg -i hook.mkv -q:v 1 -pix_fmt yuvj444p frames/hook/%05d.jpg).
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
SHAPES = {"arrow": "a", "ibeam": "i", "cross": "c", "sizeall": "s"}   # anything else is drawn as the arrow


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--rec", type=Path, required=True)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    rec = args.rec.resolve()
    clips, region = {}, None
    for meta_path in sorted(rec.glob("*.json")):
        meta = json.loads(meta_path.read_text(encoding="utf-8"))
        if "frames" not in meta:
            continue
        name = meta["scene"]
        frames_dir = rec / "frames" / name
        count = len(list(frames_dir.glob("*.jpg")))
        if count != len(meta["frames"]):
            raise SystemExit(f"{name}: {count} extracted frames but {len(meta['frames'])} logged")
        size = (meta["region"]["w"], meta["region"]["h"])
        if region and region != size:
            raise SystemExit(f"{name}: region {size} differs from {region}")
        region = size
        frames = meta["frames"]
        clips[name] = {
            "dir": frames_dir.as_uri() + "/",
            "t": [f["t"] for f in frames],
            "x": [f["x"] for f in frames],
            "y": [f["y"] for f in frames],
            "c": "".join(SHAPES.get(f["cursor"], "a") for f in frames),
            "events": meta["events"],
        }
    installer = PROJECT / "assets" / "installer-components.png"
    with Image.open(installer) as image:
        installer_size = image.size
    data = {
        "region": {"w": region[0], "h": region[1]},
        "clips": clips,
        "installer": {"src": installer.as_uri(), "w": installer_size[0], "h": installer_size[1]},
        "icon": (PROJECT / "src" / "JianYiImage.App" / "Assets" / "Jianyi.svg").as_uri(),
    }
    out = args.out or rec / "data.js"
    out.write_text("window.PROMO_DATA = " + json.dumps(data, ensure_ascii=False) + ";\n", encoding="utf-8")
    print(f"{out}: {len(clips)} clips, region {region[0]}x{region[1]}")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
