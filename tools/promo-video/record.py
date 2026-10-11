"""Record the raw footage for the vertical promo video from the real JianYiImage window.

    py -3.14 tools/promo-video/record.py --exe <JianYiImage.exe> --demo <demo folder> --ocr <ocr folder>
        --work <empty folder> --take-over-input [--only scene,scene]

WARNING: this drives the REAL mouse and keyboard for about two minutes; a dark full-screen backdrop says so while it
runs. It stops at once (releasing every key and button it holds) when the cursor is not where it put it or another
program has the keyboard focus, so a stray touch can never send its keys to a different window. It never presses
Delete, never clicks a menu item and never changes the recognition language. The clipboard is saved before and put
back afterwards, because Ctrl+C and "copy all" are part of the footage.

Each scene starts the app on one demo image, puts the window at a fixed spot, waits for OCR, activates the window with
a click on an empty part of the canvas and then captures the window area at 30 fps (GDI, piped into a lossless
ffmpeg file) while it acts. Next to every clip it writes the cursor position and shape of every frame and the input
events with timestamps; the composition draws the cursor, clicks and key caps from those.

--demo is the folder written by make_demo.py; --ocr holds <name>.ocr.json from tools/readme-assets/ocr-dump.ps1 for
chat, notes, receipt and landscape (the drag targets are computed from what the engine reads).
"""

from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import json
import math
import os
import subprocess
import sys
import threading
import time
from pathlib import Path

import numpy as np

HERE = Path(__file__).resolve().parent
FPS = 30
VISIBLE_W, VISIBLE_H = 780, 1340          # visible window size (narrow layout: the text panel docks at the bottom)
WINDOW_LEFT, WINDOW_TOP = 900, 40         # outer window position on the primary screen
CANVAS_BG = (0x16, 0x16, 0x16)            # AppCanvasColor
TITLE_H, TOOLBAR_H, STATUS_H = 32, 44, 28

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
dwmapi = ctypes.WinDLL("dwmapi")
user32.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))   # per-monitor v2: every coordinate is a physical pixel


# ---------------------------------------------------------------- Win32 plumbing

class RECT(ctypes.Structure):
    _fields_ = [("left", wt.LONG), ("top", wt.LONG), ("right", wt.LONG), ("bottom", wt.LONG)]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [("dx", wt.LONG), ("dy", wt.LONG), ("mouseData", wt.DWORD), ("dwFlags", wt.DWORD), ("time", wt.DWORD),
                ("dwExtraInfo", ctypes.c_size_t)]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wt.WORD), ("wScan", wt.WORD), ("dwFlags", wt.DWORD), ("time", wt.DWORD), ("dwExtraInfo", ctypes.c_size_t)]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [("uMsg", wt.DWORD), ("wParamL", wt.WORD), ("wParamH", wt.WORD)]


class _INPUTUNION(ctypes.Union):
    _fields_ = [("mi", MOUSEINPUT), ("ki", KEYBDINPUT), ("hi", HARDWAREINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("u", _INPUTUNION)]


class CURSORINFO(ctypes.Structure):
    _fields_ = [("cbSize", wt.DWORD), ("flags", wt.DWORD), ("hCursor", wt.HANDLE), ("ptScreenPos", wt.POINT)]


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG), ("biPlanes", wt.WORD), ("biBitCount", wt.WORD),
                ("biCompression", wt.DWORD), ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG), ("biYPelsPerMeter", wt.LONG),
                ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD)]


user32.SendInput.argtypes = [wt.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
user32.GetCursorInfo.argtypes = [ctypes.POINTER(CURSORINFO)]
user32.GetCursorPos.argtypes = [ctypes.POINTER(wt.POINT)]
user32.GetForegroundWindow.restype = wt.HWND
user32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.POINTER(wt.DWORD)]
user32.WindowFromPoint.argtypes = [wt.POINT]
user32.WindowFromPoint.restype = wt.HWND
user32.GetAncestor.argtypes = [wt.HWND, wt.UINT]
user32.GetAncestor.restype = wt.HWND
user32.SetWindowPos.argtypes = [wt.HWND, wt.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.UINT]
user32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(RECT)]
user32.IsWindowVisible.argtypes = [wt.HWND]
user32.GetWindowTextLengthW.argtypes = [wt.HWND]
user32.PostMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
user32.ShowWindow.argtypes = [wt.HWND, ctypes.c_int]
user32.LoadCursorW.restype = wt.HANDLE
user32.LoadCursorW.argtypes = [wt.HINSTANCE, ctypes.c_void_p]
user32.GetAsyncKeyState.restype = ctypes.c_short
user32.GetDC.restype = wt.HDC
user32.GetDC.argtypes = [wt.HWND]
user32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]
dwmapi.DwmGetWindowAttribute.argtypes = [wt.HWND, wt.DWORD, ctypes.c_void_p, wt.DWORD]
gdi32.CreateCompatibleDC.restype = wt.HDC
gdi32.CreateCompatibleDC.argtypes = [wt.HDC]
gdi32.CreateDIBSection.restype = wt.HBITMAP
gdi32.CreateDIBSection.argtypes = [wt.HDC, ctypes.c_void_p, wt.UINT, ctypes.POINTER(ctypes.c_void_p), wt.HANDLE, wt.DWORD]
gdi32.SelectObject.restype = wt.HGDIOBJ
gdi32.SelectObject.argtypes = [wt.HDC, wt.HGDIOBJ]
gdi32.BitBlt.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.HDC, ctypes.c_int, ctypes.c_int, wt.DWORD]
gdi32.DeleteObject.argtypes = [wt.HGDIOBJ]
gdi32.DeleteDC.argtypes = [wt.HDC]

SCREEN_W = user32.GetSystemMetrics(0)
SCREEN_H = user32.GetSystemMetrics(1)
CURSOR_NAMES = {}
for _name, _idc in {"arrow": 32512, "ibeam": 32513, "wait": 32514, "cross": 32515, "sizeall": 32646, "hand": 32649,
                    "appstarting": 32650}.items():
    _handle = user32.LoadCursorW(None, ctypes.c_void_p(_idc))
    if _handle:
        CURSOR_NAMES[_handle] = _name

VK_SHIFT, VK_CONTROL, VK_ESCAPE, VK_RIGHT = 0x10, 0x11, 0x1B, 0x27
VK_C, VK_R = 0x43, 0x52
EXTENDED_KEYS = {VK_RIGHT}


class Abort(Exception):
    """The run is no longer under the script's control (cursor moved by someone, focus elsewhere...)."""


def window_pid(hwnd: int) -> int:
    pid = wt.DWORD()
    user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    return pid.value


def cursor_pos() -> tuple[int, int]:
    p = wt.POINT()
    user32.GetCursorPos(ctypes.byref(p))
    return p.x, p.y


def cursor_shape() -> tuple[str, int]:
    info = CURSORINFO()
    info.cbSize = ctypes.sizeof(CURSORINFO)
    user32.GetCursorInfo(ctypes.byref(info))
    handle = info.hCursor or 0
    return CURSOR_NAMES.get(handle, "other"), handle


def visible_frame(hwnd: int) -> RECT:
    r = RECT()
    dwmapi.DwmGetWindowAttribute(hwnd, 9, ctypes.byref(r), ctypes.sizeof(RECT))   # DWMWA_EXTENDED_FRAME_BOUNDS
    return r


# ---------------------------------------------------------------- clipboard (saved before, restored after)

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
kernel32.GlobalLock.restype = ctypes.c_void_p
kernel32.GlobalLock.argtypes = [wt.HGLOBAL]
kernel32.GlobalUnlock.argtypes = [wt.HGLOBAL]
kernel32.GlobalSize.restype = ctypes.c_size_t
kernel32.GlobalSize.argtypes = [wt.HGLOBAL]
kernel32.GlobalAlloc.restype = wt.HGLOBAL
kernel32.GlobalAlloc.argtypes = [wt.UINT, ctypes.c_size_t]
user32.OpenClipboard.argtypes = [wt.HWND]
user32.EnumClipboardFormats.argtypes = [wt.UINT]
user32.EnumClipboardFormats.restype = wt.UINT
user32.GetClipboardData.argtypes = [wt.UINT]
user32.GetClipboardData.restype = wt.HANDLE
user32.SetClipboardData.argtypes = [wt.UINT, wt.HANDLE]
user32.SetClipboardData.restype = wt.HANDLE
# formats whose data is a GDI/metafile handle rather than global memory; Windows re-synthesizes them from CF_DIB etc.
NON_HGLOBAL = {2, 3, 9, 14, 0x0080, 0x0082, 0x0083, 0x008E}


def open_clipboard() -> None:
    for _ in range(50):
        if user32.OpenClipboard(None):
            return
        time.sleep(0.05)
    raise RuntimeError("clipboard stays busy")


def save_clipboard() -> list[tuple[int, bytes]]:
    open_clipboard()
    try:
        saved, fmt = [], 0
        while True:
            fmt = user32.EnumClipboardFormats(fmt)
            if not fmt:
                break
            if fmt in NON_HGLOBAL:
                continue
            handle = user32.GetClipboardData(fmt)
            if not handle:
                continue
            ptr = kernel32.GlobalLock(handle)
            if not ptr:
                continue
            try:
                saved.append((fmt, ctypes.string_at(ptr, kernel32.GlobalSize(handle))))
            finally:
                kernel32.GlobalUnlock(handle)
        return saved
    finally:
        user32.CloseClipboard()


def restore_clipboard(saved: list[tuple[int, bytes]]) -> None:
    open_clipboard()
    try:
        user32.EmptyClipboard()
        for fmt, data in saved:
            handle = kernel32.GlobalAlloc(0x0002, max(1, len(data)))       # GMEM_MOVEABLE
            ptr = kernel32.GlobalLock(handle)
            ctypes.memmove(ptr, data, len(data))
            kernel32.GlobalUnlock(handle)
            user32.SetClipboardData(fmt, handle)                          # the clipboard owns it from now on
    finally:
        user32.CloseClipboard()


def clipboard_text() -> str | None:
    open_clipboard()
    try:
        handle = user32.GetClipboardData(13)                              # CF_UNICODETEXT
        if not handle:
            return None
        ptr = kernel32.GlobalLock(handle)
        try:
            return ctypes.wstring_at(ptr)
        finally:
            kernel32.GlobalUnlock(handle)
    finally:
        user32.CloseClipboard()


def clipboard_has_image() -> bool:
    open_clipboard()
    try:
        return bool(user32.IsClipboardFormatAvailable(8))                 # CF_DIB
    finally:
        user32.CloseClipboard()


# ---------------------------------------------------------------- screen capture -> lossless ffmpeg file

class Capture(threading.Thread):
    def __init__(self, ffmpeg: str, region: tuple[int, int, int, int], out: Path):
        super().__init__(daemon=True)
        self.x, self.y, self.w, self.h = region
        self.out = out
        self.frames: list[dict] = []
        self.stop_flag = threading.Event()
        self.started_at: float | None = None
        self.ready = threading.Event()
        self.error: BaseException | None = None
        self.proc = subprocess.Popen(
            [ffmpeg, "-hide_banner", "-loglevel", "error", "-y", "-f", "rawvideo", "-pix_fmt", "bgr0", "-s", f"{self.w}x{self.h}",
             "-framerate", str(FPS), "-i", "-", "-c:v", "libx264rgb", "-preset", "ultrafast", "-qp", "0", str(out)],
            stdin=subprocess.PIPE,
        )

    def run(self) -> None:
        screen = user32.GetDC(None)
        mem = gdi32.CreateCompatibleDC(screen)
        header = BITMAPINFOHEADER(ctypes.sizeof(BITMAPINFOHEADER), self.w, -self.h, 1, 32, 0, 0, 0, 0, 0, 0)
        bits = ctypes.c_void_p()
        bitmap = gdi32.CreateDIBSection(mem, ctypes.byref(header), 0, ctypes.byref(bits), None, 0)
        old = gdi32.SelectObject(mem, bitmap)
        try:
            n = 0
            self.started_at = time.perf_counter()
            self.ready.set()
            while not self.stop_flag.is_set():
                due = self.started_at + n / FPS
                delay = due - time.perf_counter()
                if delay > 0:
                    time.sleep(delay)
                t = time.perf_counter() - self.started_at
                gdi32.BitBlt(mem, 0, 0, self.w, self.h, screen, self.x, self.y, 0x00CC0020)   # SRCCOPY
                gdi32.GdiFlush()
                cx, cy = cursor_pos()
                shape, handle = cursor_shape()
                self.proc.stdin.write(ctypes.string_at(bits, self.w * self.h * 4))
                self.frames.append({
                    "t": round(t, 4), "x": cx - self.x, "y": cy - self.y, "cursor": shape, "handle": handle,
                    "left": bool(user32.GetAsyncKeyState(0x01) & 0x8000), "shift": bool(user32.GetAsyncKeyState(VK_SHIFT) & 0x8000),
                })
                n += 1
        except BaseException as exc:      # surfaced by stop()
            self.error = exc
            self.ready.set()
        finally:
            gdi32.SelectObject(mem, old)
            gdi32.DeleteObject(bitmap)
            gdi32.DeleteDC(mem)
            user32.ReleaseDC(None, screen)

    def now(self) -> float:
        return time.perf_counter() - self.started_at

    def stop(self) -> None:
        self.stop_flag.set()
        self.join(5)
        self.proc.stdin.close()
        self.proc.wait(30)
        if self.error:
            raise self.error


# ---------------------------------------------------------------- input with safety checks

class Driver:
    def __init__(self):
        self.pid: int | None = None
        self.expected: tuple[int, int] | None = None
        self.held_keys: set[int] = set()
        self.left_down = False
        self.right_down = False
        self.capture: Capture | None = None
        self.events: list[dict] = []

    # -- checks
    def check(self, need_foreground: bool = True) -> None:
        if self.expected is not None:
            x, y = cursor_pos()
            if abs(x - self.expected[0]) > 2 or abs(y - self.expected[1]) > 2:
                raise Abort(f"cursor moved by someone else: at {x},{y}, expected {self.expected}")
        if need_foreground and self.pid is not None:
            fg = user32.GetForegroundWindow()
            if not fg or window_pid(fg) != self.pid:
                raise Abort("the app no longer has the keyboard focus")

    def pause(self, seconds: float, need_foreground: bool = True) -> None:
        end = time.perf_counter() + seconds
        while True:
            self.check(need_foreground)
            left = end - time.perf_counter()
            if left <= 0:
                return
            time.sleep(min(0.02, left))

    def log(self, kind: str, **fields) -> None:
        if self.capture and self.capture.started_at is not None:
            entry = {"t": round(self.capture.now(), 4), "type": kind}
            for key, value in fields.items():
                entry[key] = value
            if "x" in entry and "y" in entry:
                entry["x"] -= self.capture.x
                entry["y"] -= self.capture.y
            self.events.append(entry)

    # -- raw input
    def _send(self, *inputs: INPUT) -> None:
        array = (INPUT * len(inputs))(*inputs)
        if user32.SendInput(len(inputs), array, ctypes.sizeof(INPUT)) != len(inputs):
            raise Abort(f"SendInput was blocked (error {ctypes.get_last_error()})")

    def _mouse(self, flags: int, data: int = 0) -> None:
        self._send(INPUT(0, _INPUTUNION(mi=MOUSEINPUT(0, 0, data & 0xFFFFFFFF, flags, 0, 0))))

    def _key(self, vk: int, up: bool) -> None:
        flags = (0x0002 if up else 0) | (0x0001 if vk in EXTENDED_KEYS else 0)
        self._send(INPUT(1, _INPUTUNION(ki=KEYBDINPUT(vk, 0, flags, 0, 0))))

    def warp(self, x: float, y: float) -> None:
        nx = int((round(x) + 0.5) * 65536 / SCREEN_W)
        ny = int((round(y) + 0.5) * 65536 / SCREEN_H)
        self._send(INPUT(0, _INPUTUNION(mi=MOUSEINPUT(nx, ny, 0, 0x0001 | 0x8000, 0, 0))))
        self.expected = (int(round(x)), int(round(y)))

    # -- gestures
    def move(self, x: float, y: float, seconds: float, arc: float = 0.08, need_foreground: bool = True) -> None:
        """Ease-in-out along a slight arc, 60 updates per second, like a hand would."""
        self.check(need_foreground)
        sx, sy = cursor_pos()
        dx, dy = x - sx, y - sy
        dist = math.hypot(dx, dy)
        steps = max(1, int(seconds * 60))
        # control point: halfway, pushed sideways by arc * distance
        mx, my = sx + dx / 2 - dy * arc, sy + dy / 2 + dx * arc
        start = time.perf_counter()
        for k in range(1, steps + 1):
            u = k / steps
            u = u * u * (3 - 2 * u)
            px = (1 - u) ** 2 * sx + 2 * (1 - u) * u * mx + u * u * x
            py = (1 - u) ** 2 * sy + 2 * (1 - u) * u * my + u * u * y
            self.check(need_foreground)
            self.warp(px, py)
            due = start + k / 60
            delay = due - time.perf_counter()
            if delay > 0:
                time.sleep(delay)
        if dist == 0:
            self.warp(x, y)

    def left(self, down: bool) -> None:
        self.check()
        self._mouse(0x0002 if down else 0x0004)
        self.left_down = down
        x, y = cursor_pos()
        self.log("ldown" if down else "lup", x=x, y=y)

    def click(self) -> None:
        self.left(True)
        time.sleep(0.07)
        self.left(False)

    def right_click(self) -> None:
        self.check()
        x, y = cursor_pos()
        self._mouse(0x0008)
        self.right_down = True
        time.sleep(0.07)
        self._mouse(0x0010)
        self.right_down = False
        self.log("rclick", x=x, y=y)

    def wheel(self, notches: int) -> None:
        self.check()
        x, y = cursor_pos()
        self._mouse(0x0800, 120 if notches > 0 else -120)
        self.log("wheel", x=x, y=y, dir=1 if notches > 0 else -1)

    def key_down(self, vk: int) -> None:
        self.check()
        self._key(vk, False)
        self.held_keys.add(vk)

    def key_up(self, vk: int) -> None:
        self._key(vk, True)
        self.held_keys.discard(vk)

    def chord(self, label: str, *vks: int) -> None:
        self.log("key", label=label)
        for vk in vks:
            self.key_down(vk)
            time.sleep(0.06)
        for vk in reversed(vks):
            self.key_up(vk)
            time.sleep(0.04)

    def release_everything(self) -> None:
        for vk in list(self.held_keys) + [VK_SHIFT, VK_CONTROL]:
            try:
                self._key(vk, True)
            except Abort:
                pass
        self.held_keys.clear()
        try:
            if self.left_down:
                self._mouse(0x0004)
            if self.right_down:
                self._mouse(0x0010)
        except Abort:
            pass
        self.left_down = self.right_down = False


# ---------------------------------------------------------------- the app

class App:
    def __init__(self, exe: Path, image: Path, driver: Driver, wait_ocr: bool = True):
        self.proc = subprocess.Popen([str(exe), str(image)])
        self.driver = driver
        self.hwnd = 0
        deadline = time.time() + 30
        while not self.hwnd:
            if time.time() > deadline or self.proc.poll() is not None:
                raise RuntimeError("the app did not show its window")
            self.hwnd = self._find_window()
            time.sleep(0.15)
        user32.ShowWindow(self.hwnd, 9)                                  # SW_RESTORE
        self.place()
        driver.pid = self.proc.pid
        if wait_ocr:
            self.uia("wait-ocr")
            time.sleep(0.4)
        else:                                                           # animated GIFs are never recognized
            time.sleep(1.5)

    def _find_window(self) -> int:
        found = []

        @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
        def callback(hwnd, _):
            if user32.IsWindowVisible(hwnd) and window_pid(hwnd) == self.proc.pid and user32.GetWindowTextLengthW(hwnd) > 0:
                found.append(hwnd)
            return True

        user32.EnumWindows(callback, 0)
        return found[0] if found else 0

    def place(self) -> None:
        # the outer rectangle includes invisible resize borders; size it so the VISIBLE frame is exactly what we want
        width, height = VISIBLE_W + 14, VISIBLE_H + 7
        for _ in range(3):
            user32.SetWindowPos(self.hwnd, wt.HWND(-1), WINDOW_LEFT, WINDOW_TOP, width, height, 0x0040)   # TOPMOST, SHOWWINDOW
            time.sleep(0.5)
            vf = visible_frame(self.hwnd)
            dw, dh = VISIBLE_W - (vf.right - vf.left), VISIBLE_H - (vf.bottom - vf.top)
            if dw == 0 and dh == 0:
                break
            width, height = width + dw, height + dh
        time.sleep(0.6)

    def region(self) -> tuple[int, int, int, int]:
        """Capture area: the visible frame minus its 1 px DWM border, which blends with whatever is behind it."""
        vf = visible_frame(self.hwnd)
        return vf.left + 1, vf.top + 1, (vf.right - vf.left) - 2, (vf.bottom - vf.top) - 2

    def uia(self, mode: str, ids: str = "", names: list[str] | None = None) -> str:
        escaped = ",".join("".join(chr(92) + "u%04x" % ord(c) for c in name) for name in (names or []))
        result = subprocess.run(
            ["powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(HERE / "uia.ps1"), "-ProcessId", str(self.proc.pid),
             "-Mode", mode, "-Ids", ids, "-Names", escaped],
            capture_output=True, text=True, encoding="ascii", errors="replace", check=True,
        )
        return result.stdout.strip()

    def rects(self, ids: str = "", names: list[str] | None = None) -> dict[str, tuple[float, float, float, float]]:
        out = self.uia("rects", ids, names)
        items = json.loads(out) if out else []
        if isinstance(items, dict):
            items = [items]
        result = {}
        for item in items:
            key = item["key"]
            if chr(92) + "u" in key:
                key = key.encode("ascii").decode("unicode_escape")
            result[key] = (item["x"], item["y"], item["w"], item["h"])
        return result

    def grab(self) -> np.ndarray:
        x, y, w, h = self.region()
        screen = user32.GetDC(None)
        mem = gdi32.CreateCompatibleDC(screen)
        header = BITMAPINFOHEADER(ctypes.sizeof(BITMAPINFOHEADER), w, -h, 1, 32, 0, 0, 0, 0, 0, 0)
        bits = ctypes.c_void_p()
        bitmap = gdi32.CreateDIBSection(mem, ctypes.byref(header), 0, ctypes.byref(bits), None, 0)
        old = gdi32.SelectObject(mem, bitmap)
        gdi32.BitBlt(mem, 0, 0, w, h, screen, x, y, 0x00CC0020)
        gdi32.GdiFlush()
        frame = np.frombuffer(ctypes.string_at(bits, w * h * 4), dtype=np.uint8).reshape(h, w, 4).copy()
        gdi32.SelectObject(mem, old)
        gdi32.DeleteObject(bitmap)
        gdi32.DeleteDC(mem)
        user32.ReleaseDC(None, screen)
        return frame

    def image_rect(self, panel_height: int = 0) -> tuple[int, int, int, int]:
        """Where the picture sits inside the capture area, found by looking for non-canvas pixels."""
        frame = self.grab()
        h, w = frame.shape[:2]
        top, bottom = TITLE_H + TOOLBAR_H + 2, h - STATUS_H - panel_height - 2
        area = frame[top:bottom, :, :3].astype(np.int16)
        bg = np.array(CANVAS_BG[::-1], dtype=np.int16)                  # BGR
        mask = (np.abs(area - bg).max(axis=2) > 14)
        # ignore the two round previous/next buttons floating at the vertical middle
        mid = (bottom - top) // 2
        mask[max(0, mid - 26):mid + 26, :66] = False
        mask[max(0, mid - 26):mid + 26, w - 66:] = False
        ys, xs = np.nonzero(mask)
        if len(xs) == 0:
            raise RuntimeError("no picture on the canvas")
        return int(xs.min()), int(ys.min()) + top, int(xs.max()) + 1, int(ys.max()) + 1 + top

    def close(self) -> None:
        if self.proc.poll() is None:
            user32.PostMessageW(self.hwnd, 0x0010, 0, 0)                 # WM_CLOSE
            try:
                self.proc.wait(5)
            except subprocess.TimeoutExpired:
                self.proc.kill()
        self.driver.pid = None


# ---------------------------------------------------------------- scenes

class Scene:
    def __init__(self, recorder: "Recorder", name: str, image: str, ocr: str | None, image_size: tuple[int, int]):
        self.r, self.name, self.image, self.ocr_name, self.image_size = recorder, name, image, ocr, image_size
        self.app: App | None = None
        self.map: tuple[float, float, float] | None = None              # screen x0, y0, scale for image pixels
        self.meta: dict = {}

    def ocr(self) -> dict:
        return json.loads((self.r.ocr / f"{self.ocr_name}.ocr.json").read_text(encoding="utf-8-sig"))

    def locate(self, panel_height: int = 0) -> None:
        rx, ry, _, _ = self.app.region()
        x0, y0, x1, y1 = self.app.image_rect(panel_height)
        iw, ih = self.image_size
        scale = (x1 - x0) / iw
        if abs((y1 - y0) / ih - scale) > 0.02:
            raise RuntimeError(f"{self.name}: picture box {x0},{y0},{x1},{y1} does not match a {iw}x{ih} image")
        self.map = (rx + x0, ry + y0, scale)
        self.meta.setdefault("image_rects", []).append({"x": x0, "y": y0, "w": x1 - x0, "h": y1 - y0, "scale": round(scale, 5)})

    def pt(self, ix: float, iy: float) -> tuple[float, float]:
        x0, y0, s = self.map
        return x0 + ix * s, y0 + iy * s

    def center(self, rect: tuple[float, float, float, float]) -> tuple[float, float]:
        x, y, w, h = rect
        return x + w / 2, y + h / 2

    def open(self, activate_at: tuple[float, float], park_at: tuple[float, float], wait_ocr: bool = True) -> None:
        """Start the app, find the picture, activate the window with a click on an empty spot, park the cursor."""
        self.app = App(self.r.exe, self.r.demo / self.image, self.r.driver, wait_ocr)
        self.locate()
        d = self.r.driver
        ax, ay = self.pt(*activate_at)
        d.move(ax, ay, 0.35, need_foreground=False)
        hit = user32.GetAncestor(user32.WindowFromPoint(wt.POINT(int(ax), int(ay))), 2)      # GA_ROOT
        if window_pid(hit) != self.app.proc.pid:
            raise Abort(f"{self.name}: the activation point is not on the app window")
        self.r.driver._mouse(0x0002)
        time.sleep(0.06)
        self.r.driver._mouse(0x0004)
        time.sleep(0.35)
        d.check()                                                       # now the app must own the focus
        px, py = self.pt(*park_at)
        d.move(px, py, 0.3)
        d.pause(0.4)

    def start(self) -> None:
        region = self.app.region()
        self.meta["region"] = {"x": region[0], "y": region[1], "w": region[2], "h": region[3]}
        cap = Capture(self.r.ffmpeg, region, self.r.work / f"{self.name}.mkv")
        self.r.driver.capture, self.r.driver.events = cap, []
        cap.start()
        cap.ready.wait(5)
        if cap.error:
            raise cap.error

    def stop(self) -> None:
        d = self.r.driver
        cap = d.capture
        cap.stop()
        frames = cap.frames
        self.meta.update({
            "scene": self.name, "image": self.image, "fps": FPS, "frames": frames, "events": d.events,
            "duration": frames[-1]["t"] if frames else 0,
            "unknown_cursors": sorted({f["handle"] for f in frames if f["cursor"] == "other"}),
        })
        (self.r.work / f"{self.name}.json").write_text(json.dumps(self.meta, ensure_ascii=False, indent=1), encoding="utf-8")
        d.capture = None
        print(f"  {self.name}: {len(frames)} frames, {self.meta['duration']:.2f}s")

    def finish(self) -> None:
        if self.app:
            self.app.close()


def line_with(doc: dict, needle: str) -> dict:
    for line in doc["lines"]:
        if needle in line["text"].replace(" ", ""):
            return line
    raise SystemExit(f"OCR line not found: {needle!r}")


def scene_hook(r: "Recorder") -> None:
    s = Scene(r, "hook", "01-群聊截图.png", "chat", (1080, 1700))
    try:
        doc = s.ocr()
        line = line_with(doc, "海滨区半山路")
        y = line["y"] + line["h"] / 2
        first, last = line["words"][0], line["words"][-1]
        s.open(activate_at=(540, 1330), park_at=(900, 1420))
        d = r.driver
        s.start()
        d.pause(0.5)
        d.move(*s.pt(first["x"] + 3, y), 0.6)
        d.pause(0.2)
        d.log("mark", name="drag")
        d.left(True)
        d.pause(0.12)
        d.move(*s.pt(last["x"] + last["w"] - 3, y), 0.9, arc=0.0)
        d.pause(0.15)
        d.left(False)
        d.pause(0.45)
        d.chord("Ctrl+C", VK_CONTROL, VK_C)
        d.pause(0.4)
        d.log("copied", text=clipboard_text())
        d.pause(1.3)
        s.stop()
    finally:
        s.finish()


def scene_notes(r: "Recorder") -> None:
    s = Scene(r, "notes", "02-读书笔记.png", "notes", (1080, 1700))
    try:
        doc = s.ocr()
        l1 = line_with(doc, "先把脑子里")
        l4 = line_with(doc, "清单不在长")
        period = l4["words"][1]
        s.open(activate_at=(540, 1420), park_at=(960, 1380))
        app, d = s.app, r.driver
        buttons = app.rects("ExtractTextButton")
        s.start()
        d.pause(0.4)
        d.move(*s.pt(l1["x"] + 3, l1["y"] + l1["h"] / 2), 0.6)
        d.pause(0.2)
        d.log("mark", name="drag")
        d.left(True)
        d.pause(0.12)
        d.move(*s.pt(period["x"] + period["w"] - 2, l4["y"] + l4["h"] / 2), 1.3, arc=0.04)
        d.pause(0.15)
        d.left(False)
        d.pause(0.45)
        d.chord("Ctrl+C", VK_CONTROL, VK_C)
        d.pause(0.4)
        d.log("copied", text=clipboard_text())
        d.pause(0.7)
        # one-click extraction: the panel docks at the bottom of this narrow window
        d.move(*s.center(buttons["ExtractTextButton"]), 0.7)
        d.pause(0.2)
        d.log("mark", name="extract")
        d.click()
        d.pause(1.3)
        copy_all = app.rects("CopyAllTextButton")["CopyAllTextButton"]   # slow (PowerShell); this part is cut
        d.log("mark", name="panel-ready")
        d.pause(0.3)
        d.move(*s.center(copy_all), 0.7)
        d.pause(0.2)
        d.log("mark", name="copy-all")
        d.click()
        d.pause(0.4)
        d.log("copied", text=clipboard_text())
        d.pause(1.2)
        s.meta["ui"] = {"ExtractTextButton": buttons["ExtractTextButton"], "CopyAllTextButton": copy_all}
        s.stop()
    finally:
        s.finish()


def scene_landscape(r: "Recorder") -> None:
    s = Scene(r, "landscape", "04-山海之间.jpg", "landscape", (1600, 1000))
    try:
        doc = s.ocr()
        title = line_with(doc, "山海之间")
        ty = title["y"] + title["h"] / 2
        first, last = title["words"][0], title["words"][-1]
        s.open(activate_at=(800, -120), park_at=(1500, 1150))
        d = r.driver
        s.start()
        d.pause(0.4)
        d.move(*s.pt(first["x"] + 6, ty), 0.6)
        d.pause(0.2)
        d.log("mark", name="drag")
        d.left(True)
        d.pause(0.1)
        d.move(*s.pt(last["x"] + last["w"] - 6, ty), 0.7, arc=0.0)
        d.pause(0.12)
        d.left(False)
        d.pause(0.8)
        # Shift + drag: a rectangle around the sun becomes a picture on the clipboard
        d.move(*s.pt(860, 360), 0.6)
        d.pause(0.15)
        d.log("mark", name="region")
        d.key_down(VK_SHIFT)
        d.log("key", label="Shift")
        d.pause(0.25)
        d.left(True)
        d.pause(0.1)
        d.move(*s.pt(1400, 700), 0.9, arc=0.0)
        d.pause(0.12)
        d.left(False)
        d.key_up(VK_SHIFT)
        d.pause(0.5)
        d.chord("Ctrl+C", VK_CONTROL, VK_C)
        d.pause(0.5)
        d.log("copied", image=clipboard_has_image())
        d.pause(1.1)
        s.stop()
    finally:
        s.finish()


def scene_menu(r: "Recorder") -> None:
    s = Scene(r, "menu", "04-山海之间.jpg", "landscape", (1600, 1000))
    try:
        doc = s.ocr()
        title = line_with(doc, "山海之间")
        ty = title["y"] + title["h"] / 2
        words = title["words"]
        s.open(activate_at=(800, -120), park_at=(1500, 1150))
        app, d = s.app, r.driver
        s.start()
        d.pause(0.3)
        d.move(*s.pt(words[0]["x"] + 6, ty), 0.5)
        d.pause(0.15)
        d.left(True)
        d.pause(0.1)
        d.move(*s.pt(words[-1]["x"] + words[-1]["w"] - 6, ty), 0.6, arc=0.0)
        d.pause(0.1)
        d.left(False)
        d.pause(0.35)
        d.move(*s.pt(words[1]["x"] + words[1]["w"] / 2, ty), 0.3)
        d.pause(0.15)
        d.log("mark", name="menu")
        d.right_click()
        d.pause(0.8)
        items = app.rects(names=["用 Bing 搜索", "翻译所选文字", "识别语言"])  # slow (PowerShell); this part is cut
        d.log("mark", name="menu-ready")
        d.pause(0.2)
        for label, dwell in (("用 Bing 搜索", 0.45), ("翻译所选文字", 0.45), ("识别语言", 1.6)):
            ix, iy = s.center(items[label])
            d.move(ix - 30, iy, 0.35, arc=0.02)
            d.log("mark", name=f"hover:{label}")
            d.pause(dwell)
        d.chord("Esc", VK_ESCAPE)
        d.pause(0.25)
        d.chord("Esc", VK_ESCAPE)
        d.pause(0.4)
        s.meta["ui"] = dict(items)
        s.stop()
    finally:
        s.finish()


def scene_nav(r: "Recorder") -> None:
    s = Scene(r, "nav", "01-群聊截图.png", None, (1080, 1700))
    try:
        s.open(activate_at=(540, 1330), park_at=(1000, 1500))
        d = r.driver
        s.start()
        d.pause(0.5)
        for _ in range(4):
            d.chord("→", VK_RIGHT)
            d.pause(0.6)
        d.pause(0.4)
        s.stop()
    finally:
        s.finish()


def scene_zoom(r: "Recorder") -> None:
    s = Scene(r, "zoom", "03-购物小票.png", "receipt", (760, 1280))
    try:
        doc = s.ocr()
        total = line_with(doc, "合计")
        s.open(activate_at=(40, 1240), park_at=(700, 1250))
        d = r.driver
        s.start()
        d.pause(0.4)
        d.move(*s.pt(total["x"] + 200, total["y"] + total["h"] / 2), 0.6)
        d.pause(0.25)
        d.log("mark", name="wheel")
        for _ in range(6):
            d.wheel(1)
            d.pause(0.11)
        d.pause(1.2)
        s.stop()
    finally:
        s.finish()


def scene_rotate(r: "Recorder") -> None:
    s = Scene(r, "rotate", "04-山海之间.jpg", None, (1600, 1000))
    try:
        s.open(activate_at=(800, -120), park_at=(1560, 1240))
        d = r.driver
        s.start()
        d.pause(0.5)
        d.chord("R", VK_R)
        d.pause(1.1)
        d.chord("Shift+R", VK_SHIFT, VK_R)
        d.pause(0.9)
        s.stop()
    finally:
        s.finish()


def scene_gif(r: "Recorder") -> None:
    s = Scene(r, "gif", "06-动图.gif", None, (640, 640))
    try:
        s.open(activate_at=(320, -60), park_at=(620, 900), wait_ocr=False)
        d = r.driver
        s.start()
        d.pause(2.6)
        s.stop()
    finally:
        s.finish()


def scene_overflow(r: "Recorder") -> None:
    s = Scene(r, "overflow", "02-读书笔记.png", None, (1080, 1700))
    try:
        s.open(activate_at=(540, 1420), park_at=(960, 1380))
        app, d = s.app, r.driver
        button = app.rects("OverflowButton")["OverflowButton"]
        s.start()
        d.pause(0.4)
        d.move(*s.center(button), 0.7)
        d.pause(0.2)
        d.log("mark", name="overflow")
        d.click()
        d.pause(0.7)
        items = app.rects(names=["设为默认图片浏览器…", "添加右键打开 / 打开方式"])   # slow; this part is cut
        d.log("mark", name="overflow-ready")
        d.pause(0.2)
        for label, dwell in (("设为默认图片浏览器…", 0.7), ("添加右键打开 / 打开方式", 1.2)):
            ix, iy = s.center(items[label])
            d.move(ix + 20, iy, 0.3, arc=0.02)
            d.log("mark", name=f"hover:{label}")
            d.pause(dwell)
        d.chord("Esc", VK_ESCAPE)
        d.pause(0.5)
        s.meta["ui"] = {"OverflowButton": button, **items}
        s.stop()
    finally:
        s.finish()


SCENES = {
    "hook": scene_hook, "notes": scene_notes, "landscape": scene_landscape, "menu": scene_menu, "nav": scene_nav,
    "zoom": scene_zoom, "rotate": scene_rotate, "gif": scene_gif, "overflow": scene_overflow,
}


class Recorder:
    def __init__(self, args):
        import imageio_ffmpeg
        self.exe, self.demo, self.ocr, self.work = args.exe.resolve(), args.demo.resolve(), args.ocr.resolve(), args.work.resolve()
        self.ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
        self.driver = Driver()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--exe", type=Path, required=True)
    parser.add_argument("--demo", type=Path, required=True)
    parser.add_argument("--ocr", type=Path, required=True)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--only", default="", help="comma separated scene names")
    parser.add_argument("--take-over-input", action="store_true", help="confirm that the real mouse and keyboard may be used")
    args = parser.parse_args()
    if not args.take_over_input:
        parser.error("refusing to start: the recording drives the real mouse and keyboard, pass --take-over-input")
    if SCREEN_W < WINDOW_LEFT + VISIBLE_W + 40 or SCREEN_H < WINDOW_TOP + VISIBLE_H + 40:
        parser.error(f"the screen ({SCREEN_W}x{SCREEN_H}) is too small for a {VISIBLE_W}x{VISIBLE_H} window")
    names = [n for n in args.only.split(",") if n] or list(SCENES)
    unknown = [n for n in names if n not in SCENES]
    if unknown:
        parser.error(f"unknown scenes: {unknown}")
    args.work.mkdir(parents=True, exist_ok=True)
    r = Recorder(args)

    settings = Path(os.environ["LOCALAPPDATA"]) / "JianYiImage" / "settings.json"
    settings_before = settings.read_bytes() if settings.exists() else None
    clipboard = save_clipboard()
    print(f"clipboard saved ({len(clipboard)} formats)")
    backdrop = subprocess.Popen([sys.executable, str(HERE / "backdrop.py"),
                                 "正在录制简壹图片浏览器的宣传片素材\n\n请不要碰鼠标和键盘\n大约两分钟，结束后这块幕布会自动关闭\n\n"
                                 "（动一下鼠标，录制就会立刻中止）"])
    status = 0
    try:
        time.sleep(1.5)
        for name in names:
            print(f"scene {name}")
            SCENES[name](r)
            time.sleep(0.4)
    except Abort as exc:
        print(f"ABORTED: {exc}")
        status = 2
    finally:
        r.driver.release_everything()
        if r.driver.capture:
            try:
                r.driver.capture.stop()
            except Exception as exc:          # noqa: BLE001 - report and keep cleaning up
                print(f"capture stop failed: {exc}")
        backdrop.terminate()
        restore_clipboard(clipboard)
        print("clipboard restored")
        settings_after = settings.read_bytes() if settings.exists() else None
        if settings_after != settings_before:
            if settings_before is None:
                settings.unlink()
            else:
                settings.write_bytes(settings_before)
            print("settings.json had changed and was put back")
        else:
            print("settings.json unchanged")
    return status


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.exit(main())
