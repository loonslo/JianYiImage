"use strict";
/* Composition of the 30 s vertical promo. Everything on screen is a pure function of time t (seconds), so render.py
   can step through the frames one by one. The recorded footage arrives through data.js (build_data.py): per clip the
   frame files, the time of every frame, the cursor position/shape of every frame and the input events. */

const FPS = 30, BPM = 128, BEAT = 60 / BPM;
const B = n => n * BEAT;
const DURATION = B(64);                       // 30.0 s, 16 bars
const END = B(56);                            // the end card starts on bar 15
const CARD = { cx: 540, top: 530, maxW: 780, maxH: 960 };
const params = new URLSearchParams(location.search);
const STAGE = document.getElementById("stage");
STAGE.dataset.style = params.get("style") || "dark";

// ------------------------------------------------------------------ easing
const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
const lerp = (a, b, u) => a + (b - a) * u;
const prog = (t, a, b) => clamp((t - a) / (b - a));
const easeOut = u => 1 - Math.pow(1 - u, 3);
const easeIn = u => u * u * u;
const easeInOut = u => (u < 0.5 ? 4 * u * u * u : 1 - Math.pow(-2 * u + 2, 3) / 2);
const easeOutBack = u => { const s = 1.70158; return 1 + (s + 1) * Math.pow(u - 1, 3) + s * Math.pow(u - 1, 2); };

// ------------------------------------------------------------------ timeline
// pieces: stretches of a recorded clip played back to back (clip seconds, optional speed-up)
// cam: [time in segment, centre x, centre y, zoom] in clip pixels, eased between keys
// ch: card height for this segment (landscape content needs less), hud: where key caps and toasts go
const SEGMENTS = [
  { id: "hook", from: B(0), to: B(8), cursor: true, pieces: [{ clip: "hook", from: 0.70, to: 4.45 }],
    cam: [[0, 389, 560, 1.0], [0.45, 389, 560, 1.0], [1.75, 382, 520, 1.16], [3.75, 382, 520, 1.2]] },
  { id: "brand", from: B(8), to: B(12) },
  { id: "select", from: B(12), to: B(20), cursor: true, pieces: [{ clip: "notes", from: 0.60, to: 4.90, speed: 1.15 }],
    cam: [[0, 389, 560, 1.0], [0.5, 389, 560, 1.03], [2.1, 389, 570, 1.15], [3.75, 389, 570, 1.17]] },
  { id: "extract", from: B(20), to: B(28), cursor: true, hud: "top",
    pieces: [{ clip: "notes", from: 4.75, to: 6.00 }, { clip: "notes", from: 7.20, to: 9.70 }],
    cam: [[0, 389, 480, 1.0], [0.6, 389, 480, 1.0], [1.05, 389, 669, 0.717], [1.55, 389, 669, 0.717], [2.15, 389, 909, 1.12],
          [3.75, 389, 909, 1.15]] },
  { id: "art", from: B(28), to: B(36), cursor: true, ch: 720,
    pieces: [{ clip: "landscape", from: 0.90, to: 2.30 }, { clip: "landscape", from: 3.55, to: 6.37, speed: 1.2 }],
    cam: [[0, 352, 690, 1.25], [1.35, 352, 690, 1.25], [1.75, 430, 700, 1.25], [3.75, 430, 700, 1.3]] },
  { id: "menu", from: B(36), to: B(44), cursor: true, ch: 800,
    pieces: [{ clip: "menu", from: 1.95, to: 2.95 }, { clip: "menu", from: 3.95, to: 7.40, speed: 1.25 }],
    cam: [[0, 325, 800, 1.2], [0.72, 325, 800, 1.2], [1.15, 372, 1000, 1.42], [3.75, 372, 1000, 1.46]] },
  { id: "m_nav", from: B(44), to: B(46), pieces: [{ clip: "nav", from: 0.35, to: 1.29 }], cam: [[0, 389, 669, 0.717]], punch: true },
  { id: "m_zoom", from: B(46), to: B(48), cursor: true, pieces: [{ clip: "zoom", from: 1.15, to: 2.09 }], cam: [[0, 389, 700, 0.8]], punch: true },
  { id: "m_rot", from: B(48), to: B(50), pieces: [{ clip: "rotate", from: 0.35, to: 1.29 }], cam: [[0, 389, 669, 0.717]], punch: true },
  { id: "m_gif", from: B(50), to: B(52), ch: 760, pieces: [{ clip: "gif", from: 0.20, to: 1.14 }], cam: [[0, 389, 693, 1.0]], punch: true },
  // the context menu's file-association entries (same recording as the right-click highlight)
  { id: "m_assoc", from: B(52), to: B(54), ch: 560, pieces: [{ clip: "menu", from: 4.75, to: 5.69 }], cam: [[0, 290, 1176, 1.55]], punch: true,
    ring: [179, 1142, 205, 64], ringAt: 0.15 },
  // the installer's component page: crop away the screenshot's shadow, enlarge a little, ring the two ticked languages
  { id: "m_inst", from: B(54), to: B(56), punch: true, still: { crop: [36, 36, 676, 520], zoom: 1.12, ring: [112, 270, 146, 46], ringAt: 0.3 } },
  { id: "end", from: END, to: B(64) },
];

const LOCK = '<svg width="30" height="30" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"><rect x="4.5" y="10.5" width="15" height="10" rx="2.5"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/></svg>';

// caption elements; each has its own [in, out] so a badge can stay while titles change
const CAPTIONS = [
  { at: 0.0, out: 1.95, title: ["截图里的地址", "还在一个字一个字敲？"], srt: "截图里的地址，还在一个字一个字敲？" },
  { at: 1.95, out: B(8), title: ["<em>划一下</em>，直接复制"], sub: "像选网页上的字一样简单" },
  { at: B(12), out: B(20), badge: "亮点 <b>01</b>", title: ["像网页文字一样", "<em>划选</em>图里的字"], sub: "逐词选中 · 跨行连选 · Ctrl+C 复制" },
  { at: B(20), out: B(28), badge: "亮点 <b>02</b>", title: ["<em>一键提取</em>", "整张图的文字"], sub: LOCK + "本地识别，图片不上传" },
  { at: B(28), out: B(28) + 1.4, badge: "亮点 <b>03</b>", badgeOut: B(36), title: ["插画、海报上的字", "<em>也能选</em>"], sub: "艺术字标题照样识别" },
  { at: B(28) + 1.4, out: B(36), title: ["<i>Shift</i> + 拖动", "<em>框选</em>复制成图片"], srt: "Shift + 拖动，框选复制成图片", sub: "识别不准时也有兜底" },
  { at: B(36), out: B(44), badge: "亮点 <b>04</b>", title: ["右键就能", "<em>搜索 · 翻译</em>"], sub: "切换识别语言，常用操作都在这" },
  { at: B(44), out: B(46), badge: "看图 <b>基本功</b>", badgeOut: B(54), title: ["<i>← →</i> 同文件夹切图"], sub: "自然排序，img2 在 img10 前" },
  { at: B(46), out: B(48), title: ["<i>滚轮</i>缩放，放大不糊"], sub: "以鼠标位置为中心" },
  { at: B(48), out: B(50), title: ["<i>R</i> 键一键旋转"], sub: "只转显示，不改原图" },
  { at: B(50), out: B(52), title: ["<i>GIF</i> 动图直接播"], sub: "PNG · JPG · WebP · TIFF 都能看" },
  { at: B(52), out: B(54), title: ["<i>设为默认</i>看图软件"], sub: "右键菜单也能直接打开" },
  { at: B(54), out: B(56), badge: "安装 <b>向导</b>", title: ["勾选<em>识别语言包</em>"], sub: "中文 · English · 日本語 等 27 种" },
];

// how each recorded clip's events read on screen
const TOASTS = {
  hook: e => ["已复制", e.text],
  select: e => ["已复制", `${e.text.length} 个字符`],
  extract: e => ["已复制全部文字", `${e.text.length} 个字符`],
  art: e => (e.image ? ["已复制为图片", ""] : null),
};
const KEY_LABELS = { "Ctrl+C": ["Ctrl", "C"], "Shift": ["Shift"], "→": ["→"], "R": ["R"] };

// highlight rectangles (clip pixels) for marks in the logs
const RINGS = {
  extract: [462, 36, 100, 36],
  "copy-all": [17, 1259, 744, 36],
  "hover:用 Bing 搜索": [179, 842, 205, 32],
  "hover:翻译所选文字": [179, 874, 205, 32],
  "hover:识别语言": [179, 1107, 205, 32],
};

// ------------------------------------------------------------------ data + DOM
let D = null;
const $ = id => document.getElementById(id);
const el = (tag, cls, html) => { const n = document.createElement(tag); if (cls) n.className = cls; if (html != null) n.innerHTML = html; return n; };

function loadScript(src) {
  return new Promise((resolve, reject) => { const s = document.createElement("script"); s.src = src; s.onload = resolve; s.onerror = reject; document.head.appendChild(s); });
}

const ICON_HTML = size => `<img src="${window.PROMO_DATA.icon}" width="${size}" height="${size}" alt="">`;
const CURSORS = {
  arrow: { hx: 4, hy: 4, svg: '<path d="M4 4 L4 31 L10.8 24.6 L15.4 35.2 L20.2 33.1 L15.7 22.8 L24.6 22.8 Z" fill="#fff" stroke="#111" stroke-width="1.8" stroke-linejoin="round"/>' },
  ibeam: { hx: 32, hy: 32, svg: '<g fill="none" stroke-linecap="round"><path d="M26 16.5 Q32 16.5 32 21 Q32 16.5 38 16.5 M32 21 V43 M26 47.5 Q32 47.5 32 43 Q32 47.5 38 47.5" stroke="#fff" stroke-width="6"/><path d="M26 16.5 Q32 16.5 32 21 Q32 16.5 38 16.5 M32 21 V43 M26 47.5 Q32 47.5 32 43 Q32 47.5 38 47.5" stroke="#111" stroke-width="2.4"/></g>' },
  cross: { hx: 32, hy: 32, svg: '<g stroke-linecap="round"><path d="M32 14 V50 M14 32 H50" stroke="#fff" stroke-width="6"/><path d="M32 14 V50 M14 32 H50" stroke="#111" stroke-width="2.2"/></g>' },
  sizeall: { hx: 32, hy: 32, svg: '<path d="M32 11 L39 19 H35 V29 H45 V25 L53 32 L45 39 V35 H35 V45 H39 L32 53 L25 45 H29 V35 H19 V39 L11 32 L19 25 V29 H29 V19 H25 Z" fill="#fff" stroke="#111" stroke-width="1.8" stroke-linejoin="round"/>' },
};

const state = { capEls: [], ripples: [], rings: [], keys: [], toasts: [], cursor: null, brand: {}, end: {}, stillRing: null };

function buildCaptions() {
  const box = $("captions");
  for (const c of CAPTIONS) {
    const node = el("div", "cap");
    if (c.badge) { const b = el("div", "line", `<span class="badge">${c.badge}</span>`); node.appendChild(b); state.capEls.push({ n: b.firstChild, at: c.at, out: c.badgeOut || c.out, k: 0 }); }
    else node.appendChild(el("div", "line", '<span class="badge" style="visibility:hidden">x</span>'));
    const title = el("div", "title");
    c.title.forEach((line, i) => { const l = el("div", "line", `<span>${line}</span>`); title.appendChild(l); state.capEls.push({ n: l.firstChild, at: c.at, out: c.out, k: i + 1 }); });
    node.appendChild(title);
    if (c.sub) { const s = el("div", "sub line", `<span>${c.sub}</span>`); node.appendChild(s); state.capEls.push({ n: s.firstChild, at: c.at, out: c.out, k: c.title.length + 1 }); }
    box.appendChild(node);
  }
}

function eventsFor(seg) {
  // every logged event inside the segment's pieces, with its time in the video
  const out = [];
  let offset = 0;
  for (const p of seg.pieces || []) {
    const speed = p.speed || 1;
    for (const e of D.clips[p.clip].events) {
      if (e.t >= p.from && e.t < p.to) out.push({ ...e, vt: seg.from + offset + (e.t - p.from) / speed, seg });
    }
    offset += (p.to - p.from) / speed;
  }
  return out;
}

function buildOverlays() {
  const fx = $("fx"), cardfx = $("cardfx");
  for (const seg of SEGMENTS) {
    for (const e of eventsFor(seg)) {
      if (e.type === "ldown" || e.type === "rclick") {
        const r = el("div", "ripple"), d = el("div", "dot");
        cardfx.appendChild(r); cardfx.appendChild(d);
        state.ripples.push({ r, d, e });
      } else if (e.type === "key" && KEY_LABELS[e.label]) {
        const k = el("div", "keys");
        KEY_LABELS[e.label].forEach((label, i) => {
          if (i) k.appendChild(el("span", "plus", "+"));
          k.appendChild(el("span", "key", label));
        });
        if (e.label === "Shift") k.appendChild(el("span", "plus", "+ 拖动"));
        fx.appendChild(k);
        state.keys.push({ k, e });
      } else if (e.type === "copied" && TOASTS[seg.id]) {
        const text = TOASTS[seg.id](e);
        if (!text) continue;
        const t = el("div", "toast", `<span class="ok"><svg width="26" height="26" viewBox="0 0 24 24" fill="none" stroke="#fff" stroke-width="3.2" stroke-linecap="round" stroke-linejoin="round"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg></span><span>${text[0]}${text[1] ? `<small>　${text[1]}</small>` : ""}</span>`);
        fx.appendChild(t);
        state.toasts.push({ t, e });
      } else if (e.type === "mark" && RINGS[e.name]) {
        const g = el("div", "ring");
        cardfx.appendChild(g);
        state.rings.push({ g, e, rect: RINGS[e.name] });
      }
    }
  }
  // a ring lasts until the next ring of the same segment (menu hover walk) or 0.9 s
  for (const ring of state.rings) {
    const next = state.rings.find(o => o !== ring && o.e.seg === ring.e.seg && o.e.vt > ring.e.vt);
    ring.until = next ? next.e.vt : Math.min(ring.e.seg.to, ring.e.vt + 0.9);
  }
  // a key cap gives way to the toast that follows it
  for (const key of state.keys) {
    const toast = state.toasts.find(o => o.e.vt > key.e.vt && o.e.vt - key.e.vt < 1.2);
    key.until = toast ? toast.e.vt - 0.05 : Math.min(key.e.seg.to, key.e.vt + 0.85);
  }
  state.stillRing = el("div", "ring");
  cardfx.appendChild(state.stillRing);
  const c = el("div", "cursor");
  c.innerHTML = `<svg width="64" height="64" viewBox="0 0 64 64"></svg>`;
  cardfx.appendChild(c);
  state.cursor = c;
}

function buildBrand() {
  const b = $("brand");
  b.innerHTML = `
    <div class="brand-icon" style="width:300px;height:300px;margin-left:-150px;top:520px">${ICON_HTML(300)}</div>
    <div class="line big-name" style="top:880px"><span>简壹图片浏览器</span></div>
    <div class="line latin" style="top:1022px"><span>JIANYIIMAGE</span></div>
    <div class="line slogan" style="top:1092px"><span>会<em>认字</em>的看图软件</span></div>`;
  state.brand = { icon: b.querySelector(".brand-icon"), lines: [...b.querySelectorAll(".line > span")] };
  $("tag").innerHTML = `${ICON_HTML(40)}简壹图片浏览器<span>JIANYIIMAGE</span>`;
  const e = $("end");
  e.innerHTML = `
    <div class="brand-icon" style="width:240px;height:240px;margin-left:-120px;top:380px">${ICON_HTML(240)}</div>
    <div class="line big-name" style="top:660px"><span>简壹图片浏览器</span></div>
    <div class="line latin" style="top:796px"><span>JIANYIIMAGE</span></div>
    <div class="chips" style="top:890px">
      <div class="chip"><i></i>划选即复制</div><div class="chip"><i></i>本地识别不上传</div><div class="chip"><i></i>免费开源</div>
    </div>
    <div class="cta" style="top:1020px">GitHub 搜索 <span>JianYiImage</span></div>
    <div class="line fine" style="top:1184px"><span>Windows 10 1809+ · 安装版 / 免安装版 · MIT 开源</span></div>
    <div class="line fine" style="top:1230px"><span>PNG · JPG · GIF · WebP · TIFF · BMP 等常见格式</span></div>`;
  state.end = {
    icon: e.querySelector(".brand-icon"), lines: [...e.querySelectorAll(".line > span")],
    chips: [...e.querySelectorAll(".chip")], cta: e.querySelector(".cta"),
  };
}

// ------------------------------------------------------------------ helpers per frame
const byId = id => SEGMENTS.find(s => s.id === id);
function segmentAt(t) { return SEGMENTS.find(s => t >= s.from && t < s.to) || SEGMENTS[SEGMENTS.length - 1]; }

// what the card shows: the brand sting keeps the hook's last frame while the card leaves and the next segment's first
// frame while it comes back; the end card keeps the installer page while the card drops away
function contentAt(seg, t) {
  if (seg.id === "brand") return t < B(10) ? { seg: byId("hook"), t: B(8) - 0.001 } : { seg: byId("select"), t: B(12) };
  if (seg.id === "end") return { seg: byId("m_inst"), t: END - 0.001 };
  return { seg, t };
}

function cameraAt(seg, t) {
  const keys = seg.cam || [[0, 389, 669, 1]];
  const lt = t - seg.from;
  let k = keys[0];
  if (lt >= keys[keys.length - 1][0]) k = keys[keys.length - 1];
  else if (lt > keys[0][0]) {
    for (let i = 0; i < keys.length - 1; i++) {
      const a = keys[i], b = keys[i + 1];
      if (lt >= a[0] && lt <= b[0]) {
        const u = easeInOut(prog(lt, a[0], b[0]));
        k = [lt, lerp(a[1], b[1], u), lerp(a[2], b[2], u), lerp(a[3], b[3], u)];
        break;
      }
    }
  }
  let [, cx, cy, z] = k;
  const W = D.region.w, H = D.region.h;
  const cw = Math.min(CARD.maxW, W * z), ch = Math.min(seg.ch || CARD.maxH, H * z);
  const vw = cw / z, vh = ch / z;
  cx = vw >= W ? W / 2 : clamp(cx, vw / 2, W - vw / 2);
  cy = vh >= H ? H / 2 : clamp(cy, vh / 2, H - vh / 2);
  return { cx, cy, z, card: { w: cw, h: ch, x: CARD.cx - cw / 2, y: CARD.top + (CARD.maxH - ch) / 2 } };
}

// clip pixel -> position inside the card
function toCard(cam, px, py) { return { x: cam.card.w / 2 + (px - cam.cx) * cam.z, y: cam.card.h / 2 + (py - cam.cy) * cam.z }; }

function footageAt(seg, t) {
  let lt = t - seg.from;
  for (const p of seg.pieces || []) {
    const len = (p.to - p.from) / (p.speed || 1);
    if (lt < len || p === seg.pieces[seg.pieces.length - 1]) {
      const ct = p.from + clamp(lt, 0, len) * (p.speed || 1);
      const clip = D.clips[p.clip];
      let lo = 0, hi = clip.t.length - 1;
      while (lo < hi) { const mid = (lo + hi) >> 1; if (clip.t[mid] < ct) lo = mid + 1; else hi = mid; }
      if (lo > 0 && Math.abs(clip.t[lo - 1] - ct) < Math.abs(clip.t[lo] - ct)) lo -= 1;
      return { clip, i: lo };
    }
    lt -= len;
  }
  return null;
}

// text line: rises in from below its mask, leaves upwards
function lineAnim(node, t, at, out, k) {
  const tin = at + 0.06 * k, u = at === 0 ? 1 : easeOut(prog(t, tin, tin + 0.45));
  const v = easeIn(prog(t, out - 0.26, out));
  node.style.transform = `translateY(${(1 - u) * 110 - v * 110}%)`;
  node.style.opacity = (t < tin && at !== 0) || t >= out ? 0 : 1;
}

let lastSrc = "";
async function setShot(src) {
  if (src === lastSrc) return;
  lastSrc = src;
  const img = $("shot");
  img.src = src;
  try { await img.decode(); } catch (err) { /* keep the previous frame */ }
}

function place(n, x, y, w, h, extra = {}) { Object.assign(n.style, { left: `${x}px`, top: `${y}px`, width: `${w}px`, height: `${h}px`, ...extra }); }

// ------------------------------------------------------------------ the frame
async function render(t) {
  const seg = segmentAt(t);
  const show = contentAt(seg, t);
  const cseg = show.seg, ct = show.t;

  // background drift
  const g = document.querySelectorAll(".glow");
  g[0].style.transform = `translate(${Math.sin(t * 0.35) * 60}px, ${Math.cos(t * 0.27) * 40}px)`;
  g[1].style.transform = `translate(${Math.cos(t * 0.3) * 70}px, ${Math.sin(t * 0.22) * 50}px)`;
  g[2].style.transform = `translate(${Math.sin(t * 0.25 + 1) * 50}px, ${Math.cos(t * 0.33) * 40}px)`;

  for (const p of state.capEls) lineAnim(p.n, t, p.at, p.out, p.k);

  // card: leaves for the brand sting and comes back, drops away for the end card, small punch on cuts
  const card = $("card");
  let scale = 1, opacity = 1, dy = 0;
  if (t >= B(8) - 0.08 && t < B(12) + 0.05) {
    const out = easeIn(prog(t, B(8) - 0.08, B(8) + 0.2)), back = easeOut(prog(t, B(12) - 0.3, B(12) + 0.05));
    if (t < B(10)) { scale = lerp(1, 0.86, out); opacity = 1 - out; } else { scale = lerp(0.9, 1, back); opacity = back; }
  }
  if (t >= END - 0.1) { const u = easeIn(prog(t, END - 0.1, END + 0.25)); dy = u * 160; opacity = 1 - u; scale = lerp(1, 0.92, u); }
  if (seg.punch || ["extract", "art", "menu"].includes(seg.id)) {
    const u = prog(t, seg.from, seg.from + 0.28);
    if (u < 1) scale *= lerp(1.035, 1, easeOut(u));
  }
  card.style.opacity = opacity;

  const shot = $("shot"), still = $("still"), camEl = $("cam");
  let rect, cam = null;
  if (cseg.still) {
    const s = cseg.still, z = s.zoom;
    rect = { w: Math.round(s.crop[2] * z), h: Math.round(s.crop[3] * z) };
    rect.x = CARD.cx - rect.w / 2; rect.y = CARD.top + (CARD.maxH - rect.h) / 2;
    shot.style.display = "none"; still.style.display = "block";
    camEl.style.transform = `scale(${z}) translate(${-s.crop[0]}px, ${-s.crop[1]}px)`;
    card.style.background = "#f0f0f0";
    const u = easeOut(prog(ct, cseg.from + s.ringAt, cseg.from + s.ringAt + 0.25));
    const r = s.ring;
    place(state.stillRing, (r[0] - s.crop[0] - 6) * z, (r[1] - s.crop[1] - 6) * z, (r[2] + 12) * z, (r[3] + 12) * z,
      { opacity: u, transform: `scale(${lerp(1.15, 1, u)})` });
  } else {
    cam = cameraAt(cseg, ct);
    rect = cam.card;
    shot.style.display = "block"; still.style.display = "none";
    camEl.style.transform = `translate(${rect.w / 2 - cam.cx * cam.z}px, ${rect.h / 2 - cam.cy * cam.z}px) scale(${cam.z})`;
    card.style.background = "#161616";
    if (cseg.ring) {
      const r = cseg.ring, u = easeOut(prog(ct, cseg.from + cseg.ringAt, cseg.from + cseg.ringAt + 0.25));
      const a = toCard(cam, r[0] - 5, r[1] - 5), b = toCard(cam, r[0] + r[2] + 5, r[1] + r[3] + 5);
      place(state.stillRing, a.x, a.y, b.x - a.x, b.y - a.y, { opacity: u, transform: `scale(${lerp(1.15, 1, u)})` });
    } else state.stillRing.style.opacity = 0;
  }
  place(card, rect.x, rect.y + dy, rect.w, rect.h, { transform: `scale(${scale})` });

  // footage frame + cursor (inside the card, so the card clips them)
  const frame = cam ? footageAt(cseg, ct) : null;
  const pending = [];
  if (frame) pending.push(setShot(`${frame.clip.dir}${String(frame.i + 1).padStart(5, "0")}.jpg`));
  const cur = state.cursor;
  if (frame && cseg === seg && seg.cursor) {
    const shape = { a: "arrow", i: "ibeam", c: "cross", s: "sizeall" }[frame.clip.c[frame.i]] || "arrow";
    const p = toCard(cam, frame.clip.x[frame.i], frame.clip.y[frame.i]);
    const s = Math.max(1.15, cam.z * 1.35), c = CURSORS[shape];
    if (cur.dataset.shape !== shape) { cur.firstChild.innerHTML = c.svg; cur.dataset.shape = shape; }
    cur.style.transform = `translate(${p.x - c.hx * s}px, ${p.y - c.hy * s}px) scale(${s})`;
    cur.style.opacity = 1;
  } else cur.style.opacity = 0;

  for (const { r, d, e } of state.ripples) {
    const u = prog(t, e.vt, e.vt + 0.55);
    if (!cam || t < e.vt || u >= 1 || e.seg !== cseg) { r.style.opacity = 0; d.style.opacity = 0; continue; }
    const p = toCard(cam, e.x, e.y), rad = lerp(10, 50, easeOut(u)), dr = 12;
    place(r, p.x - rad, p.y - rad, 2 * rad, 2 * rad, { opacity: 1 - u });
    place(d, p.x - dr, p.y - dr, 2 * dr, 2 * dr, { opacity: 0.55 * (1 - easeOut(prog(t, e.vt, e.vt + 0.3))) });
  }
  for (const ring of state.rings) {
    const { g: n, e, rect: rr } = ring;
    if (!cam || t < e.vt || t >= ring.until || e.seg !== cseg) { n.style.opacity = 0; continue; }
    const a = toCard(cam, rr[0] - 5, rr[1] - 5), b = toCard(cam, rr[0] + rr[2] + 5, rr[1] + rr[3] + 5);
    const u = easeOut(prog(t, e.vt, e.vt + 0.25)), v = prog(t, ring.until - 0.12, ring.until);
    place(n, a.x, a.y, b.x - a.x, b.y - a.y, { opacity: u * (1 - v), transform: `scale(${lerp(1.12, 1, u)})` });
  }

  // key caps and toasts: near the bottom of the card, or near its top when the action happens at the bottom
  const hudY = (seg.hud === "top" ? rect.y + 150 : rect.y + rect.h - 120) + dy;
  for (const key of state.keys) {
    const { k, e } = key;
    if (t < e.vt - 0.04 || t >= key.until || e.seg !== seg) { k.style.opacity = 0; continue; }
    const u = easeOutBack(prog(t, e.vt - 0.04, e.vt + 0.24)), v = prog(t, key.until - 0.15, key.until);
    Object.assign(k.style, { left: `${CARD.cx - k.offsetWidth / 2}px`, top: `${hudY - k.offsetHeight / 2}px`, opacity: clamp(u * 1.4) * (1 - v), transform: `scale(${lerp(0.6, 1, u)})` });
  }
  for (const { t: n, e } of state.toasts) {
    const until = Math.min(e.seg.to - 0.05, e.vt + 1.6);
    if (t < e.vt || t >= until) { n.style.opacity = 0; continue; }
    const u = easeOutBack(prog(t, e.vt, e.vt + 0.32)), v = prog(t, until - 0.2, until);
    Object.assign(n.style, { left: `${CARD.cx - n.offsetWidth / 2}px`, top: `${hudY - n.offsetHeight / 2 + (1 - u) * 40}px`, opacity: clamp(u * 1.3) * (1 - v), transform: `scale(${lerp(0.85, 1, u)})` });
  }

  // brand sting
  const br = state.brand;
  if (t >= B(8) - 0.1 && t < B(12)) {
    const u = easeOutBack(prog(t, B(8) + 0.02, B(8) + 0.55)), out = easeIn(prog(t, B(12) - 0.32, B(12) - 0.02));
    br.icon.style.opacity = clamp(u * 2) * (1 - out);
    br.icon.style.transform = `translateY(${-out * 80}px) scale(${lerp(0.3, 1, u)}) rotate(${lerp(-14, 0, u)}deg)`;
    br.lines.forEach((n, i) => lineAnim(n, t, B(8) + 0.22 + i * 0.1, B(12) - 0.02, 0));
  } else { br.icon.style.opacity = 0; br.lines.forEach(n => (n.style.opacity = 0)); }

  // end card
  const en = state.end, E = END;
  if (t >= E - 0.05) {
    const u = easeOutBack(prog(t, E + 0.05, E + 0.6));
    en.icon.style.opacity = clamp(u * 2);
    en.icon.style.transform = `scale(${lerp(0.3, 1, u) * lerp(1, 1.04, prog(t, E + 0.6, DURATION))}) rotate(${lerp(-14, 0, u)}deg)`;
    en.lines.forEach((n, i) => lineAnim(n, t, E + 0.25 + (i < 2 ? i * 0.1 : 0.85 + (i - 2) * 0.1), DURATION + 1, 0));
    en.chips.forEach((n, i) => {
      const v = easeOutBack(prog(t, E + 0.55 + i * 0.09, E + 0.95 + i * 0.09));
      n.style.opacity = clamp(v * 1.5); n.style.transform = `translateY(${(1 - v) * 40}px) scale(${lerp(0.8, 1, v)})`;
    });
    const w = easeOutBack(prog(t, E + 0.9, E + 1.35));
    const pulse = t > E + 1.4 ? 1 + 0.025 * Math.max(0, Math.sin((t - E - 1.4) * Math.PI * 2 / (BEAT * 2))) : 1;
    en.cta.style.opacity = clamp(w * 1.5);
    en.cta.style.transform = `translateX(-50%) translateY(${(1 - w) * 40}px) scale(${lerp(0.8, 1, w) * pulse})`;
  } else {
    en.icon.style.opacity = 0; en.lines.forEach(n => (n.style.opacity = 0)); en.chips.forEach(n => (n.style.opacity = 0)); en.cta.style.opacity = 0;
  }

  // the name tag rests while the brand sting and the end card show the name in full
  const tagOn = t < B(8) - 0.2 ? 1 : t < B(12) - 0.3 ? 1 - prog(t, B(8) - 0.2, B(8)) : t < END - 0.2 ? prog(t, B(12) - 0.3, B(12)) : 1 - prog(t, END - 0.2, END);
  $("tag").style.opacity = 0.9 * tagOn;

  // soft flash on the two big beats
  let flash = 0;
  if (t >= B(8) && t < B(8) + 0.22) flash = 1 - prog(t, B(8), B(8) + 0.22);
  if (t >= END && t < END + 0.22) flash = 1 - prog(t, END, END + 0.22);
  $("flash").style.opacity = 0.16 * flash;

  await Promise.all(pending);
}

// ------------------------------------------------------------------ boot
window.ready = (async () => {
  await loadScript(params.get("data"));
  D = window.PROMO_DATA;
  $("still").src = D.installer.src;
  await document.fonts.ready;
  buildCaptions();
  buildBrand();
  buildOverlays();
  await Promise.all([...document.images].map(i => (i.src ? i.decode().catch(() => {}) : null)));
})();
window.renderFrame = async n => { await window.ready; await render(n / FPS); return true; };
window.renderTime = async t => { await window.ready; await render(t); return true; };
window.PROMO_INFO = { fps: FPS, duration: DURATION, frames: Math.round(DURATION * FPS) };
const plain = html => { const n = document.createElement("div"); n.innerHTML = html; return n.textContent.trim(); };
window.promoCues = async () => {
  await window.ready;
  const cues = [];
  for (const r of state.ripples) cues.push({ t: r.e.vt, kind: r.e.type === "rclick" ? "rclick" : "click" });
  for (const k of state.keys) cues.push({ t: k.e.vt, kind: "key", label: k.e.label });
  for (const s of state.toasts) cues.push({ t: s.e.vt, kind: "copy" });
  for (const seg of SEGMENTS) for (const e of eventsFor(seg)) if (e.type === "wheel") cues.push({ t: e.vt, kind: "wheel" });
  for (const seg of SEGMENTS) cues.push({ t: seg.from, kind: "segment", id: seg.id });
  for (const c of CAPTIONS) cues.push({ t: c.at, kind: "caption" });
  const captions = CAPTIONS.map(c => ({ at: c.at, out: c.out, text: c.srt || c.title.map(plain).join(""), sub: c.sub ? plain(c.sub) : "" }));
  captions.push({ at: B(8), out: B(12), text: "简壹图片浏览器", sub: "会认字的看图软件" });
  captions.push({ at: END, out: DURATION, text: "简壹图片浏览器 · GitHub 搜索 JianYiImage", sub: "划选即复制 · 本地识别不上传 · 免费开源" });
  captions.sort((a, b) => a.at - b.at);
  return { bpm: BPM, beat: BEAT, duration: DURATION, cues: cues.sort((a, b) => a.t - b.t), captions };
};
if (params.get("t")) window.renderTime(parseFloat(params.get("t")));
