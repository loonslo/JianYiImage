"""Turn the raw window captures into the README images: crop to the visible frame, soft shadow, cursor overlay, GIF.

    py -3.14 tools/readme-assets/post.py <work dir> [hero selection region menu narrow gif installer]

Reads <work>/shots (and <work>/shots-select for the GIF) and writes <work>/assets-out.
"""
import json
import pathlib
import sys

from PIL import Image, ImageChops, ImageDraw, ImageFilter

sys.stdout.reconfigure(encoding="utf-8")
BASE = pathlib.Path(sys.argv[1]).resolve()
OUT = BASE / "assets-out"
OUT.mkdir(parents=True, exist_ok=True)

SHADOW_MARGIN = 36


def read_meta(path):
    return json.loads(pathlib.Path(path).read_text(encoding="utf-8-sig"))


def crop_visible(img, meta):
    """The visible frame minus its outermost pixel ring: that ring is the DWM border, which blends with whatever is
    behind the window (a screen grab shows the desktop there). Measured: the last row/column are not the app's pixels."""
    vf = meta["visibleFrame"]
    return img.crop((vf["x"] + 1, vf["y"] + 1, vf["x"] + vf["w"] - 1, vf["y"] + vf["h"] - 1))


def with_shadow(img, margin=SHADOW_MARGIN, blur=18, offset=(0, 10), opacity=0.42):
    """Transparent canvas, blurred drop shadow, then the window on top (square corners like the real window)."""
    w, h = img.size
    canvas = Image.new("RGBA", (w + margin * 2, h + margin * 2), (0, 0, 0, 0))
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rectangle(
        (margin + offset[0], margin + offset[1], margin + offset[0] + w - 1, margin + offset[1] + h - 1),
        fill=(0, 0, 0, int(255 * opacity)),
    )
    shadow = shadow.filter(ImageFilter.GaussianBlur(blur))
    canvas = Image.alpha_composite(canvas, shadow)
    canvas.alpha_composite(img.convert("RGBA"), (margin, margin))
    return canvas


def save_png(img, name):
    path = OUT / name
    img.save(path, optimize=True)
    print(f"{name:28} {path.stat().st_size / 1024:7.0f} KiB  {img.size}")
    return path


def window_shot(raw_png, meta_json, out_name, screen_capture=False):
    meta = read_meta(meta_json)
    img = Image.open(raw_png).convert("RGB")
    if screen_capture:
        # screen grabs cover the whole window rectangle, including the invisible resize border (shows the desktop)
        img = crop_visible(img, meta)
    else:
        img = crop_visible(img, meta)
    return save_png(with_shadow(img), out_name)


def installer_shot(raw_png, out_name):
    img = Image.open(raw_png).convert("RGB")
    bands = [img.getchannel(i).point(lambda v: 255 if v > 8 else 0) for i in range(3)]
    left, top, right, bottom = ImageChops.add(ImageChops.add(bands[0], bands[1]), bands[2]).getbbox()
    return save_png(with_shadow(img.crop((left + 1, top + 1, right - 1, bottom - 1))), out_name)


def draw_ibeam(frame, x, y, scale=1.3):
    """A small I-beam cursor (white with a dark outline) drawn at 4x and reduced for clean edges."""
    s = 4
    h = int(22 * scale)
    pad = 6
    size = (14 * s, (h + pad) * s)
    layer = Image.new("RGBA", size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    cx = size[0] // 2
    top, bottom = 3 * s, (h + 1) * s
    d.line((cx, top, cx, bottom), fill=(0, 0, 0, 255), width=5 * s // 2)
    for yy in (top, bottom):
        d.line((cx - 4 * s, yy, cx + 4 * s, yy), fill=(0, 0, 0, 255), width=5 * s // 2)
    d.line((cx, top, cx, bottom), fill=(255, 255, 255, 255), width=s)
    for yy in (top, bottom):
        d.line((cx - 3 * s, yy, cx + 3 * s, yy), fill=(255, 255, 255, 255), width=s)
    layer = layer.resize((size[0] // s, size[1] // s), Image.LANCZOS)
    frame.alpha_composite(layer, (int(x - layer.width / 2), int(y - layer.height / 2)))


def build_gif(shots_dir, out_name, crop, width, hold):
    shots_dir = pathlib.Path(shots_dir)
    meta = read_meta(shots_dir / "select.meta.json")
    win = meta["window"]
    vf = meta["visibleFrame"]
    ox, oy = win["left"] + vf["x"], win["top"] + vf["y"]
    frames = []
    durations = []
    for item in meta["frames"]:
        img = Image.open(shots_dir / f"{item['n']}.png").convert("RGBA")
        draw_ibeam(img, item["x"] - ox, item["y"] - oy)
        frames.append(img)
        durations.append(90)
    durations[0] = 700
    durations[-1] = hold[0]
    for name, ms in (("sel-hold", hold[0]), ("sel-copied", hold[1])):
        img = Image.open(shots_dir / f"{name}.png").convert("RGBA")
        last = meta["frames"][-1]
        draw_ibeam(img, last["x"] - ox, last["y"] - oy)
        frames.append(img)
        durations.append(ms)
    box = tuple(crop)
    ratio = width / (box[2] - box[0])
    small = []
    for f in frames:
        c = f.crop(box).convert("RGB")
        small.append(c.resize((width, round((box[3] - box[1]) * ratio)), Image.LANCZOS))
    # one shared palette for every frame (no flicker), built from frames that cover all the colours
    picks = [small[0], small[len(small) // 2], small[-3], small[-1]]
    sheet = Image.new("RGB", (small[0].width, sum(p.height for p in picks)))
    y = 0
    for p in picks:
        sheet.paste(p, (0, y))
        y += p.height
    palette = sheet.quantize(colors=160, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    paletted = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in small]
    path = OUT / out_name
    paletted[0].save(path, save_all=True, append_images=paletted[1:], duration=durations, loop=0, optimize=True, disposal=1)
    print(f"{out_name:28} {path.stat().st_size / 1024:7.0f} KiB  {paletted[0].size}  {len(paletted)} frames, {sum(durations) / 1000:.1f}s")
    # also keep the last frame as a still, handy for checking
    small[-1].save(OUT / (pathlib.Path(out_name).stem + "-last.png"))


if __name__ == "__main__":
    what = sys.argv[2:] or ["all"]
    shots = BASE / "shots"
    if "hero" in what or "all" in what:
        window_shot(shots / "hero.png", shots / "hero.meta.json", "hero.png")
    if "selection" in what or "all" in what:
        window_shot(shots / "selection.png", shots / "selection.meta.json", "selection-on-picture.png")
    if "region" in what or "all" in what:
        window_shot(shots / "region.png", shots / "region.meta.json", "region-copy.png")
    if "narrow" in what or "all" in what:
        window_shot(shots / "narrow.png", shots / "narrow.meta.json", "narrow-layout.png")
    if "menu" in what or "all" in what:
        window_shot(shots / "menu.png", shots / "menu.meta.json", "context-menu.png", screen_capture=True)
    if "installer" in what:
        installer_shot(BASE / "wizard" / "p2-components-recommended.png", "installer-components.png")
    if "gif" in what or "all" in what:
        build_gif(BASE / "shots-select", "select-demo.gif", crop=(1, 300, 1425, 892), width=900, hold=(900, 2200))
