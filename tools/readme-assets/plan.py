"""Turn the OCR word boxes of the sample images into drag/click plans (image pixel coordinates, numbers only).

    py -3.14 tools/readme-assets/plan.py <work dir>

Reads <work>/ocr/<name>.ocr.json (written by ocr-dump.ps1 for every sample) and writes <work>/plans/<scenario>.json,
which capture.ps1 uses to know where to drag the real mouse.
"""
import json
import pathlib
import sys

sys.stdout.reconfigure(encoding="utf-8")
BASE = pathlib.Path(sys.argv[1]).resolve()
OCR = BASE / "ocr"
PLANS = BASE / "plans"
PLANS.mkdir(parents=True, exist_ok=True)


def load(name):
    return json.loads((OCR / f"{name}.ocr.json").read_text(encoding="utf-8-sig"))


def words(doc):
    for line in doc["lines"]:
        for w in line["words"]:
            yield w


def find_word(doc, text):
    hits = [w for w in words(doc) if w["text"] == text]
    if not hits:
        raise SystemExit(f"word not found: {text!r}")
    return hits[0]


def line_with(doc, needle):
    for line in doc["lines"]:
        if needle in line["text"].replace(" ", ""):
            return line
    raise SystemExit(f"line not found: {needle!r}")


def center_y(box):
    return box["y"] + box["h"] / 2


def write(name, doc, **fields):
    plan = {"imageWidth": doc["width"], "imageHeight": doc["height"], **fields}
    (PLANS / f"{name}.json").write_text(json.dumps(plan, ensure_ascii=False, indent=2), encoding="utf-8")
    print(name, json.dumps(fields, ensure_ascii=False))


# ---- cafe menu: the Wi-Fi line (the classic "copy the password from a photo") ----
menu = load("cafe-menu")
wifi = line_with(menu, "banshan-cafe")
first = wifi["words"][0]
password = find_word(menu, "20261010")
banshan = find_word(menu, "banshan-cafe")
y = center_y(password)
line_drag = {"x0": first["x"] + 4, "y0": y, "x1": password["x"] + password["w"] - 4, "y1": y, "steps": 26}
write("select", menu, drag=line_drag)
# context-menu shot: select the big title, right-click it near the top so the menu and its submenu stay inside the window
title_line = line_with(menu, "半山咖啡")
tws = title_line["words"]
ty = center_y(title_line)
write(
    "menu",
    menu,
    drag={"x0": tws[0]["x"] + 6, "y0": ty, "x1": tws[-1]["x"] + tws[-1]["w"] - 6, "y1": ty, "steps": 12},
    click={"x": tws[1]["x"] + tws[1]["w"] / 2, "y": ty},
)

# ---- landscape: text sitting on a picture ----
land = load("landscape")
title = land["lines"][0]
tw = title["words"]
ty = center_y(title)
write("selection", land, drag={"x0": tw[0]["x"] + 6, "y0": ty, "x1": tw[-1]["x"] + tw[-1]["w"] - 6, "y1": ty, "steps": 14})

# ---- landscape again: Shift+drag a rectangle around the sun (region copy as an image) ----
write("region", land, drag={"x0": 880, "y0": 380, "x1": 1400, "y1": 700, "steps": 16})

# ---- slide: first row, so the panel shot also shows a highlight ----
slide = load("meeting-slide")
row = line_with(slide, "安装程序新增OCR语言包勾选页")
sy = center_y(row)
write("slide-row", slide, drag={"x0": row["words"][0]["x"] + 4, "y0": sy, "x1": row["words"][-1]["x"] + row["words"][-1]["w"] - 4, "y1": sy, "steps": 18})


# ---- notice: first row (the date) is the hero highlight; the panel next to it lists every line ----
notice = load("notice")
nrow = line_with(notice, "时间：10月18日")
ny = center_y(nrow)
write("hero", notice, drag={"x0": nrow["words"][0]["x"] + 4, "y0": ny, "x1": nrow["words"][-1]["x"] + nrow["words"][-1]["w"] - 4, "y1": ny, "steps": 20})
