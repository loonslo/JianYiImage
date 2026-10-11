"""Render the composition frame by frame with headless Chrome and encode it with ffmpeg.

    py -3.14 tools/promo-video/render.py --data <data.js> --out <video.mp4> [--style dark|light] [--audio <wav>]
    py -3.14 tools/promo-video/render.py --data <data.js> --stills 1.0,7.5,27 --out <folder> [--style light]

Chrome runs with a throw-away profile on its own DevTools port and is ended by PID; the page is driven through the
DevTools protocol: window.renderFrame(n) sets up frame n, then Page.captureScreenshot grabs it. Frames are piped as
PNG into ffmpeg (H.264, yuv420p, BT.709, 30 fps); with --audio the soundtrack is muxed in as AAC.
"""

from __future__ import annotations

import argparse
import base64
import json
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request
from pathlib import Path

import websocket

HERE = Path(__file__).resolve().parent
PAGE = HERE / "composition" / "index.html"


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


class Page:
    def __init__(self, url: str, profile: Path):
        self.chrome = subprocess.Popen(
            [str(find_chrome()), "--headless=new", "--hide-scrollbars", "--force-device-scale-factor=1", "--window-size=1080,1920",
             "--remote-debugging-port=0", f"--user-data-dir={profile}", "--allow-file-access-from-files", "--no-first-run",
             "--no-default-browser-check", "--disable-extensions", "--mute-audio", "about:blank"],
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        )
        port_file = profile / "DevToolsActivePort"
        for _ in range(100):
            if port_file.exists() and port_file.read_text().strip():
                break
            time.sleep(0.1)
        port = int(port_file.read_text().splitlines()[0])
        targets = json.load(urllib.request.urlopen(f"http://127.0.0.1:{port}/json/list"))
        page = next(t for t in targets if t["type"] == "page")
        self.ws = websocket.create_connection(page["webSocketDebuggerUrl"], suppress_origin=True, timeout=120)
        self.next_id = 0
        self.call("Emulation.setDeviceMetricsOverride", width=1080, height=1920, deviceScaleFactor=1, mobile=False)
        self.call("Page.enable")
        self.call("Page.navigate", url=url)
        self.eval("new Promise(r => { const go = () => window.ready ? window.ready.then(() => r(true)) : setTimeout(go, 50); go(); })")

    def call(self, method: str, **params):
        self.next_id += 1
        self.ws.send(json.dumps({"id": self.next_id, "method": method, "params": params}))
        while True:
            message = json.loads(self.ws.recv())
            if message.get("id") == self.next_id:
                if "error" in message:
                    raise RuntimeError(f"{method}: {message['error']}")
                return message.get("result", {})

    def eval(self, expression: str):
        result = self.call("Runtime.evaluate", expression=expression, awaitPromise=True, returnByValue=True)
        if "exceptionDetails" in result:
            raise RuntimeError(json.dumps(result["exceptionDetails"], ensure_ascii=False)[:2000])
        return result.get("result", {}).get("value")

    def shot(self) -> bytes:
        data = self.call("Page.captureScreenshot", format="png", clip={"x": 0, "y": 0, "width": 1080, "height": 1920, "scale": 1})["data"]
        return base64.b64decode(data)

    def close(self) -> None:
        try:
            self.ws.close()
        finally:
            if self.chrome.poll() is None:
                subprocess.run(["taskkill", "/F", "/T", "/PID", str(self.chrome.pid)], capture_output=True)
                self.chrome.wait(10)
            time.sleep(1.0)                                     # let Chrome's helper processes release the profile


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--data", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--style", default="dark", choices=["dark", "light"])
    parser.add_argument("--audio", type=Path)
    parser.add_argument("--stills", help="comma separated seconds: write PNGs into --out instead of a video")
    parser.add_argument("--frames", help="first:last frame numbers, for a quick partial render")
    parser.add_argument("--cues", action="store_true", help="write the cue sheet (events, captions) as JSON to --out and stop")
    args = parser.parse_args()
    import imageio_ffmpeg
    ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
    url = f"{PAGE.as_uri()}?data={args.data.resolve().as_uri()}&style={args.style}"
    with tempfile.TemporaryDirectory(prefix="jianyiimage-promo-chrome-", ignore_cleanup_errors=True) as profile:
        page = Page(url, Path(profile))
        try:
            info = page.eval("window.PROMO_INFO")
            if args.cues:
                args.out.write_text(json.dumps(page.eval("window.promoCues()"), ensure_ascii=False, indent=1), encoding="utf-8")
                print(args.out)
                return 0
            if args.stills:
                args.out.mkdir(parents=True, exist_ok=True)
                for value in args.stills.split(","):
                    seconds = float(value)
                    page.eval(f"window.renderTime({seconds})")
                    page.eval(f"window.renderTime({seconds})")      # second pass: text boxes measured after the first
                    target = args.out / f"{args.style}-{seconds:05.2f}.png"
                    target.write_bytes(page.shot())
                    print(target)
                return 0
            first, last = 0, info["frames"] - 1
            if args.frames:
                first, last = (int(v) for v in args.frames.split(":"))
            command = [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "image2pipe", "-framerate", str(info["fps"]), "-c:v", "png", "-i", "-"]
            if args.audio:
                command += ["-i", str(args.audio)]
            command += ["-vf", "scale=out_color_matrix=bt709:out_range=tv,format=yuv420p,setparams=range=tv:color_primaries=bt709:color_trc=bt709:colorspace=bt709", "-c:v", "libx264", "-preset", "slow", "-crf", "16",
                        "-profile:v", "high", "-level:v", "4.2", "-colorspace", "bt709", "-color_primaries", "bt709", "-color_trc", "bt709",
                        "-color_range", "tv", "-r", str(info["fps"])]
            if args.audio:
                command += ["-c:a", "aac", "-b:a", "192k", "-shortest"]
            command += ["-movflags", "+faststart", str(args.out)]
            encoder = subprocess.Popen(command, stdin=subprocess.PIPE)
            started = time.time()
            for n in range(first, last + 1):
                page.eval(f"window.renderFrame({n})")
                encoder.stdin.write(page.shot())
                if n % 60 == 0:
                    print(f"frame {n}/{last}  {time.time() - started:5.1f}s", flush=True)
            encoder.stdin.close()
            if encoder.wait() != 0:
                raise SystemExit("ffmpeg failed")
            print(f"{args.out}  ({last - first + 1} frames in {time.time() - started:.0f}s)")
        finally:
            page.close()
    return 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
