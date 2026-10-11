"""Assemble the deliverables from the rendered video, the soundtrack and the cue sheet.

    py -3.14 tools/promo-video/package.py --video <video.mp4> --audio <music.wav> --cues <cues.json> --cover <frame0.png> --out <folder>

Writes into --out (file names are what gets uploaded, so they are in Chinese):
  简壹图片浏览器-宣传片-竖屏.mp4         the video with music and sound effects (H.264 + AAC 192k)
  简壹图片浏览器-宣传片-竖屏-无声.mp4    the same picture without sound, for adding music/voice in an editor
  字幕.srt                               every on-screen caption with its time (for platforms or for a voice-over)
  配乐-不含音效.wav / 音效-与画面同步.wav  the soundtrack split, if music.py --stems wrote <audio>-music/-sfx.wav
  封面-9比16.png / 封面-3比4.png          the first frame, and a 3:4 crop of it for feeds that use 3:4 covers
The video stream is copied, never re-encoded.
"""

from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
from pathlib import Path

from PIL import Image

NAME = "简壹图片浏览器-宣传片-竖屏"


def stamp(seconds: float) -> str:
    ms = int(round(seconds * 1000))
    h, ms = divmod(ms, 3_600_000)
    m, ms = divmod(ms, 60_000)
    s, ms = divmod(ms, 1000)
    return f"{h:02d}:{m:02d}:{s:02d},{ms:03d}"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--video", type=Path, required=True)
    parser.add_argument("--audio", type=Path, required=True)
    parser.add_argument("--cues", type=Path, required=True)
    parser.add_argument("--cover", type=Path, required=True, help="PNG of frame 0 (render.py --stills 0)")
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    import imageio_ffmpeg
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    out = args.out
    out.mkdir(parents=True, exist_ok=True)

    final = out / f"{NAME}.mp4"
    subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", str(args.video), "-i", str(args.audio), "-map", "0:v:0", "-map", "1:a:0",
                    "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest", "-movflags", "+faststart", str(final)], check=True)
    silent = out / f"{NAME}-无声.mp4"
    subprocess.run([ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-i", str(args.video), "-map", "0:v:0", "-c:v", "copy",
                    "-movflags", "+faststart", str(silent)], check=True)

    cues = json.loads(args.cues.read_text(encoding="utf-8"))
    blocks = []
    for i, c in enumerate(cues["captions"], start=1):
        text = c["text"] + (f"\n{c['sub']}" if c["sub"] else "")
        blocks.append(f"{i}\n{stamp(c['at'])} --> {stamp(min(c['out'], cues['duration']))}\n{text}\n")
    (out / "字幕.srt").write_text("\n".join(blocks), encoding="utf-8-sig")

    for suffix, target in (("-music", "配乐-不含音效.wav"), ("-sfx", "音效-与画面同步.wav")):
        stem = args.audio.with_name(args.audio.stem + suffix + ".wav")
        if stem.exists():
            shutil.copy2(stem, out / target)

    cover = Image.open(args.cover).convert("RGB")
    cover.save(out / "封面-9比16.png", optimize=True)
    top = 100                                   # keeps the caption and the whole window card
    cover.crop((0, top, 1080, top + 1440)).save(out / "封面-3比4.png", optimize=True)

    for produced in sorted(out.iterdir()):
        if produced.name.startswith("."):
            continue
        print(f"{produced.name:28} {produced.stat().st_size / 1024 / 1024:7.2f} MB")
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
