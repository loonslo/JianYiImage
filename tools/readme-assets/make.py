"""Regenerate the screenshots and the animation used by README.md from the real application.

    py -3.14 tools/readme-assets/make.py --exe <JianYiImage.exe> --work <empty scratch folder> --take-over-input
        [--setup <JianYiImage-v...-setup.exe>] [--out assets]

WARNING: the capture drives the REAL mouse and keyboard for about two minutes (it drags across the text in the sample
images and presses Ctrl+C). Do not touch the mouse or keyboard meanwhile; --take-over-input confirms you accept that.
The window is only captured, never the desktop, and no installer is ever run to completion: the wizard is walked up to
"ready to install" with window messages.

Steps: OCR word boxes of the samples -> drag plans -> capture scenarios -> crop / shadow / cursor / GIF -> <out>.
Needs Pillow, Windows PowerShell 5.1 and a Windows OCR language (the plans are computed from what the engine reads).
"""

from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[1]
SAMPLES = PROJECT / "samples"

# name used by plan.py -> sample file
OCR_SOURCES = {
    "cafe-menu": "01-cafe-menu.png",
    "notice": "02-notice.png",
    "meeting-slide": "03-meeting-slide.png",
    "receipt": "04-receipt.png",
    "landscape": "05-landscape.jpg",
}

# scenario, sample, plan, extra capture.ps1 arguments, output folder under the work dir
CAPTURES = [
    ("hero", "02-notice.png", "hero", [], "shots"),
    ("selection", "05-landscape.jpg", "selection", [], "shots"),
    ("region", "05-landscape.jpg", "region", [], "shots"),
    ("menu", "01-cafe-menu.png", "menu", [], "shots"),
    ("narrow", "03-meeting-slide.png", None, ["-Width", "560", "-Height", "900"], "shots"),
    ("select", "01-cafe-menu.png", "select", [], "shots-select"),
]

POST_TARGETS = ["hero", "selection", "region", "menu", "narrow", "gif"]


def powershell(script: Path, *arguments: str) -> None:
    command = ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(script), *arguments]
    subprocess.run(command, check=True)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--exe", type=Path, required=True, help="the built JianYiImage.exe")
    parser.add_argument("--work", type=Path, required=True, help="scratch folder (created if missing, must be empty)")
    parser.add_argument("--out", type=Path, default=PROJECT / "assets")
    parser.add_argument("--setup", type=Path, help="also capture the installer's component page from this setup.exe")
    parser.add_argument("--take-over-input", action="store_true", help="confirm that the real mouse and keyboard may be used")
    args = parser.parse_args()
    if not args.take_over_input:
        parser.error("refusing to start: the capture drives the real mouse and keyboard, pass --take-over-input")
    work = args.work.resolve()
    if work.exists() and any(work.iterdir()):
        parser.error("--work must be empty")
    work.mkdir(parents=True, exist_ok=True)

    (work / "ocr").mkdir()
    for name, sample in OCR_SOURCES.items():
        powershell(HERE / "ocr-dump.ps1", "-ImagePath", str(SAMPLES / sample), "-OutFile", str(work / "ocr" / f"{name}.ocr.json"))
    subprocess.run([sys.executable, str(HERE / "plan.py"), str(work)], check=True)

    for scenario, sample, plan, extra, folder in CAPTURES:
        arguments = [
            "-Exe", str(args.exe.resolve()), "-Image", str(SAMPLES / sample), "-OutDir", str(work / folder), "-Scenario", scenario,
            *extra,
        ]
        if plan:
            arguments += ["-Plan", str(work / "plans" / f"{plan}.json")]
        powershell(HERE / "capture.ps1", *arguments)

    targets = list(POST_TARGETS)
    if args.setup:
        empty_target = work / "installer-target"        # the wizard is never allowed to install into it
        powershell(
            HERE / "capture-installer.ps1", "-Setup", str(args.setup.resolve()), "-Dir", str(empty_target),
            "-OutDir", str(work / "wizard"),
        )
        targets.append("installer")

    subprocess.run([sys.executable, str(HERE / "post.py"), str(work), *targets], check=True)
    args.out.mkdir(parents=True, exist_ok=True)
    for produced in sorted((work / "assets-out").iterdir()):
        if produced.name.endswith(("-last.png", "-zoom.png", "-check.png")):
            continue
        shutil.copy2(produced, args.out / produced.name)
        print("->", args.out / produced.name)
    return 0


if __name__ == "__main__":
    sys.exit(main())
