import { buildHistogram, quantize, makeIndexer } from "./quant.js";
import { encodeGif } from "./gif.js";

const $ = (id) => document.getElementById(id);
const state = { w: 0, h: 0, frames: [], cur: 0, zoom: 4, playing: false, timer: null, onion: false };

/* ---------- frame helpers ---------- */

function blank(w, h) {
  const c = document.createElement("canvas");
  c.width = w; c.height = h;
  return c;
}

function addFrame(canvas, delay = 100) {
  state.frames.push({ canvas, delay });
}

/* Grow the document to fit, keeping existing frames anchored top-left. */
function fitDoc(w, h) {
  if (w <= state.w && h <= state.h) return;
  const nw = Math.max(w, state.w), nh = Math.max(h, state.h);
  for (const f of state.frames) {
    const next = blank(nw, nh);
    next.getContext("2d").drawImage(f.canvas, 0, 0);
    f.canvas = next;
  }
  state.w = nw; state.h = nh;
}

function placed(img, w, h) {
  const c = blank(w, h);
  c.getContext("2d").drawImage(img, 0, 0);
  return c;
}

function ctxOf(f) {
  return f.canvas.getContext("2d", { willReadFrequently: true });
}

/* ---------- import ---------- */

async function loadStill(file) {
  const bmp = await createImageBitmap(file);
  if (!state.w) { state.w = bmp.width; state.h = bmp.height; }
  fitDoc(bmp.width, bmp.height);
  addFrame(placed(bmp, state.w, state.h), 100);
  bmp.close?.();
}

/* Animated GIF/WebP split into frames. Uses WebCodecs ImageDecoder where the
 * browser has it; without it the file still imports, just as a single frame. */
async function loadAnimated(file) {
  if (!("ImageDecoder" in window)) {
    await loadStill(file);
    return;
  }
  const buf = await file.arrayBuffer();
  const dec = new window.ImageDecoder({ data: buf, type: file.type || "image/gif" });
  await dec.tracks.ready;
  const count = dec.tracks.selectedTrack?.frameCount ?? 1;
  for (let i = 0; i < count; i++) {
    const { image } = await dec.decode({ frameIndex: i });
    if (!state.w) { state.w = image.displayWidth; state.h = image.displayHeight; }
    fitDoc(image.displayWidth, image.displayHeight);
    // duration is microseconds; GIF stores centiseconds, so this divides clean.
    addFrame(placed(image, state.w, state.h), Math.max(10, Math.round((image.duration ?? 100000) / 1000)));
    image.close?.();
  }
}

async function importFiles(files) {
  for (const file of files) {
    const animated = /gif|webp|apng/i.test(file.type) || /\.(gif|webp|apng)$/i.test(file.name);
    if (animated) await loadAnimated(file);
    else await loadStill(file);
  }
  state.cur = Math.min(state.cur, state.frames.length - 1);
  redraw();
}

/* Slice the current frame into a grid of frames (sprite sheets). */
function sliceSheet(cols, rows) {
  const f = state.frames[state.cur];
  if (!f || cols < 1 || rows < 1) return;
  const cw = Math.floor(state.w / cols), ch = Math.floor(state.h / rows);
  if (cw < 1 || ch < 1) return;
  const made = [];
  for (let r = 0; r < rows; r++) {
    for (let c = 0; c < cols; c++) {
      const cell = blank(cw, ch);
      cell.getContext("2d").drawImage(f.canvas, c * cw, r * ch, cw, ch, 0, 0, cw, ch);
      made.push({ canvas: cell, delay: f.delay });
    }
  }
  state.frames.splice(state.cur, 1, ...made);
  state.w = cw; state.h = ch;
  // Other frames were sized to the old document; re-anchor them into the cell.
  state.frames = state.frames.map((fr) => {
    if (fr.canvas.width === cw && fr.canvas.height === ch) return fr;
    const c = blank(cw, ch);
    c.getContext("2d").drawImage(fr.canvas, 0, 0);
    return { canvas: c, delay: fr.delay };
  });
  state.cur = 0;
  redraw();
}

/* ---------- edits ---------- */

function eachTarget(allFrames, fn) {
  const list = allFrames ? state.frames : [state.frames[state.cur]];
  for (const f of list) if (f) fn(f);
}

function flip(horizontal, allFrames) {
  eachTarget(allFrames, (f) => {
    const next = blank(state.w, state.h);
    const c = next.getContext("2d");
    c.translate(horizontal ? state.w : 0, horizontal ? 0 : state.h);
    c.scale(horizontal ? -1 : 1, horizontal ? 1 : -1);
    c.drawImage(f.canvas, 0, 0);
    f.canvas = next;
  });
  redraw();
}

/* Make one colour transparent, within a tolerance. The usual first step on a
 * sprite ripped onto a flat background. */
function colorKey(hex, tolerance, allFrames) {
  const m = /^#?([0-9a-f]{6})$/i.exec(hex.trim());
  if (!m) return;
  const v = parseInt(m[1], 16);
  const kr = (v >> 16) & 255, kg = (v >> 8) & 255, kb = v & 255;
  const limit = tolerance * tolerance * 3;
  eachTarget(allFrames, (f) => {
    const c = ctxOf(f);
    const img = c.getImageData(0, 0, state.w, state.h);
    const d = img.data;
    for (let i = 0; i < d.length; i += 4) {
      if (d[i + 3] === 0) continue;
      const dr = d[i] - kr, dg = d[i + 1] - kg, db = d[i + 2] - kb;
      if (dr * dr + dg * dg + db * db <= limit) d[i + 3] = 0;
    }
    c.putImageData(img, 0, 0);
  });
  redraw();
}

/* Shrink the document to the tightest box holding any opaque pixel in ANY
 * frame, so the animation stays registered against itself. */
function trimTransparent() {
  if (!state.frames.length) return;
  let minX = state.w, minY = state.h, maxX = -1, maxY = -1;
  for (const f of state.frames) {
    const d = ctxOf(f).getImageData(0, 0, state.w, state.h).data;
    for (let y = 0; y < state.h; y++) {
      for (let x = 0; x < state.w; x++) {
        if (d[(y * state.w + x) * 4 + 3] === 0) continue;
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
      }
    }
  }
  if (maxX < 0) return; // nothing opaque anywhere
  const nw = maxX - minX + 1, nh = maxY - minY + 1;
  for (const f of state.frames) {
    const next = blank(nw, nh);
    next.getContext("2d").drawImage(f.canvas, -minX, -minY);
    f.canvas = next;
  }
  state.w = nw; state.h = nh;
  redraw();
}

function scaleAll(factor, smooth) {
  const nw = Math.max(1, Math.round(state.w * factor));
  const nh = Math.max(1, Math.round(state.h * factor));
  for (const f of state.frames) {
    const next = blank(nw, nh);
    const c = next.getContext("2d");
    c.imageSmoothingEnabled = smooth;
    c.drawImage(f.canvas, 0, 0, nw, nh);
    f.canvas = next;
  }
  state.w = nw; state.h = nh;
  redraw();
}

function resizeCanvas(nw, nh, anchorCenter) {
  nw = Math.max(1, nw | 0); nh = Math.max(1, nh | 0);
  const dx = anchorCenter ? Math.round((nw - state.w) / 2) : 0;
  const dy = anchorCenter ? Math.round((nh - state.h) / 2) : 0;
  for (const f of state.frames) {
    const next = blank(nw, nh);
    next.getContext("2d").drawImage(f.canvas, dx, dy);
    f.canvas = next;
  }
  state.w = nw; state.h = nh;
  redraw();
}

/* ---------- export ---------- */

function frameData() {
  return state.frames.map((f) => ctxOf(f).getImageData(0, 0, state.w, state.h).data);
}

/* Floyd-Steinberg, serpentine. Off by default: on pixel art the noise reads
 * as dirt rather than as extra colour. */
function ditherToIndices(px, w, h, palette, nearest, offset) {
  const err = new Float32Array(w * h * 3);
  const out = new Uint8Array(w * h);
  for (let y = 0; y < h; y++) {
    const leftToRight = (y & 1) === 0;
    for (let i = 0; i < w; i++) {
      const x = leftToRight ? i : w - 1 - i;
      const p = y * w + x, o = p * 4, e = p * 3;
      if (px[o + 3] < 128) { out[p] = 0; continue; }
      const r = Math.min(255, Math.max(0, px[o] + err[e]));
      const g = Math.min(255, Math.max(0, px[o + 1] + err[e + 1]));
      const b = Math.min(255, Math.max(0, px[o + 2] + err[e + 2]));
      const idx = nearest(r | 0, g | 0, b | 0);
      out[p] = idx + offset;
      const dr = r - palette[idx][0], dg = g - palette[idx][1], db = b - palette[idx][2];
      const spread = (nx, ny, f) => {
        if (nx < 0 || nx >= w || ny >= h) return;
        const ne = (ny * w + nx) * 3;
        err[ne] += dr * f; err[ne + 1] += dg * f; err[ne + 2] += db * f;
      };
      const fwd = leftToRight ? 1 : -1;
      spread(x + fwd, y, 7 / 16);
      spread(x - fwd, y + 1, 3 / 16);
      spread(x, y + 1, 5 / 16);
      spread(x + fwd, y + 1, 1 / 16);
    }
  }
  return out;
}

function buildGif() {
  if (!state.frames.length) throw new Error("nothing to export");
  const maxColors = Math.max(2, Math.min(256, parseInt($("colors").value, 10) || 256));
  const dither = $("dither").checked;
  const loopForever = $("loop").checked;
  const all = frameData();

  // Index 0 is reserved for transparency, so the image gets one colour fewer.
  const anyTransparent = all.some((px) => {
    for (let i = 3; i < px.length; i += 4) if (px[i] < 128) return true;
    return false;
  });
  const offset = anyTransparent ? 1 : 0;
  const bits = 5;
  const hist = buildHistogram(all, bits);
  const colors = quantize(hist, maxColors - offset);
  if (!colors.length) throw new Error("no opaque pixels to build a palette from");
  const nearest = makeIndexer(colors, bits);
  const palette = anyTransparent ? [[0, 0, 0], ...colors] : colors;

  const frames = all.map((px, i) => {
    let indices;
    if (dither) {
      indices = ditherToIndices(px, state.w, state.h, colors, nearest, offset);
    } else {
      indices = new Uint8Array(state.w * state.h);
      for (let p = 0; p < indices.length; p++) {
        const o = p * 4;
        indices[p] = px[o + 3] < 128 ? 0 : nearest(px[o], px[o + 1], px[o + 2]) + offset;
      }
    }
    return { indices, delay: state.frames[i].delay };
  });

  return encodeGif({
    width: state.w, height: state.h, frames, palette,
    transparentIndex: anyTransparent ? 0 : null,
    loop: loopForever ? 0 : null,
  });
}

function download(blob, name) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = name;
  a.click();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
}

function exportSheet() {
  const cols = Math.max(1, parseInt($("sheetCols").value, 10) || state.frames.length);
  const rows = Math.ceil(state.frames.length / cols);
  const sheet = blank(state.w * cols, state.h * rows);
  const c = sheet.getContext("2d");
  state.frames.forEach((f, i) => {
    c.drawImage(f.canvas, (i % cols) * state.w, Math.floor(i / cols) * state.h);
  });
  sheet.toBlob((b) => download(b, "sheet.png"));
}

/* ---------- rendering ---------- */

function redraw() {
  const view = $("view");
  const has = state.frames.length > 0;
  $("empty").style.display = has ? "none" : "grid";
  if (!has) {
    view.width = view.height = 0;
    drawTimeline();
    updateInfo();
    return;
  }
  state.cur = Math.max(0, Math.min(state.cur, state.frames.length - 1));
  const z = state.zoom;
  view.width = state.w * z;
  view.height = state.h * z;
  const c = view.getContext("2d");
  c.imageSmoothingEnabled = false;
  c.clearRect(0, 0, view.width, view.height);
  if (state.onion && state.frames.length > 1) {
    const prev = state.frames[(state.cur - 1 + state.frames.length) % state.frames.length];
    c.globalAlpha = 0.3;
    c.drawImage(prev.canvas, 0, 0, view.width, view.height);
    c.globalAlpha = 1;
  }
  c.drawImage(state.frames[state.cur].canvas, 0, 0, view.width, view.height);
  drawTimeline();
  updateInfo();
}

function updateInfo() {
  const n = state.frames.length;
  const total = state.frames.reduce((a, f) => a + f.delay, 0);
  $("info").textContent = n
    ? state.w + "x" + state.h + "  ·  " + n + " frame" + (n === 1 ? "" : "s")
      + "  ·  " + total + "ms  ·  " + state.zoom + "x"
    : "no document";
  $("delay").value = n ? state.frames[state.cur].delay : 100;
}

function drawTimeline() {
  const strip = $("strip");
  strip.innerHTML = "";
  state.frames.forEach((f, i) => {
    const cell = document.createElement("button");
    cell.className = "cell" + (i === state.cur ? " on" : "");
    const t = blank(48, 48);
    const tc = t.getContext("2d");
    tc.imageSmoothingEnabled = false;
    const s = Math.min(48 / state.w, 48 / state.h);
    const dw = Math.max(1, state.w * s), dh = Math.max(1, state.h * s);
    tc.drawImage(f.canvas, (48 - dw) / 2, (48 - dh) / 2, dw, dh);
    cell.appendChild(t);
    const tag = document.createElement("span");
    tag.textContent = i + 1;
    cell.appendChild(tag);
    cell.onclick = () => { state.cur = i; redraw(); };
    strip.appendChild(cell);
  });
}

/* ---------- playback ---------- */

function stop() {
  state.playing = false;
  clearTimeout(state.timer);
  $("play").textContent = "Play";
}

function play() {
  if (state.playing || state.frames.length < 2) return;
  state.playing = true;
  $("play").textContent = "Stop";
  const tick = () => {
    if (!state.playing) return;
    state.cur = (state.cur + 1) % state.frames.length;
    redraw();
    state.timer = setTimeout(tick, Math.max(10, state.frames[state.cur].delay));
  };
  state.timer = setTimeout(tick, Math.max(10, state.frames[state.cur].delay));
}

/* ---------- wiring ---------- */

function status(msg, bad) {
  const el = $("status");
  el.textContent = msg;
  el.className = bad ? "bad" : "";
  if (msg) setTimeout(() => { if (el.textContent === msg) el.textContent = ""; }, 6000);
}

export function init() {
  $("file").onchange = (e) => {
    const files = [...e.target.files];
    importFiles(files).then(() => status(state.frames.length + " frames loaded"));
    e.target.value = "";
  };
  const drop = $("stage");
  drop.addEventListener("dragover", (e) => { e.preventDefault(); drop.classList.add("over"); });
  drop.addEventListener("dragleave", () => drop.classList.remove("over"));
  drop.addEventListener("drop", (e) => {
    e.preventDefault();
    drop.classList.remove("over");
    importFiles([...e.dataTransfer.files]).then(() => status(state.frames.length + " frames loaded"));
  });

  $("play").onclick = () => (state.playing ? stop() : play());
  $("onion").onchange = (e) => { state.onion = e.target.checked; redraw(); };
  $("zoomIn").onclick = () => { state.zoom = Math.min(16, state.zoom + 1); redraw(); };
  $("zoomOut").onclick = () => { state.zoom = Math.max(1, state.zoom - 1); redraw(); };

  $("delay").onchange = (e) => {
    if (!state.frames.length) return;
    state.frames[state.cur].delay = Math.max(10, parseInt(e.target.value, 10) || 100);
    redraw();
  };
  $("delayAll").onclick = () => {
    if (!state.frames.length) return;
    const v = Math.max(10, parseInt($("delay").value, 10) || 100);
    state.frames.forEach((f) => { f.delay = v; });
    redraw();
    status("all frames set to " + v + "ms");
  };
  $("dup").onclick = () => {
    const f = state.frames[state.cur];
    if (!f) return;
    const c = blank(state.w, state.h);
    c.getContext("2d").drawImage(f.canvas, 0, 0);
    state.frames.splice(state.cur + 1, 0, { canvas: c, delay: f.delay });
    state.cur++;
    redraw();
  };
  $("del").onclick = () => {
    if (!state.frames.length) return;
    state.frames.splice(state.cur, 1);
    if (!state.frames.length) { state.w = 0; state.h = 0; }
    redraw();
  };
  $("left").onclick = () => {
    if (state.cur <= 0) return;
    const a = state.frames;
    [a[state.cur - 1], a[state.cur]] = [a[state.cur], a[state.cur - 1]];
    state.cur--;
    redraw();
  };
  $("right").onclick = () => {
    if (state.cur >= state.frames.length - 1) return;
    const a = state.frames;
    [a[state.cur + 1], a[state.cur]] = [a[state.cur], a[state.cur + 1]];
    state.cur++;
    redraw();
  };
  $("reverse").onclick = () => { state.frames.reverse(); redraw(); status("order reversed"); };

  const applyAll = () => $("applyAll").checked;
  $("flipH").onclick = () => flip(true, applyAll());
  $("flipV").onclick = () => flip(false, applyAll());
  $("key").onclick = () => {
    if (!state.frames.length) return;
    colorKey($("keyColor").value, Math.max(0, parseInt($("tol").value, 10) || 0), applyAll());
    status("colour keyed out");
  };
  $("pick").onclick = () => {
    if (!state.frames.length) return;
    status("click the canvas to pick the colour to remove");
    const view = $("view");
    view.style.cursor = "crosshair";
    const once = (ev) => {
      const r = view.getBoundingClientRect();
      const x = Math.floor((ev.clientX - r.left) / state.zoom);
      const y = Math.floor((ev.clientY - r.top) / state.zoom);
      if (x >= 0 && y >= 0 && x < state.w && y < state.h) {
        const d = ctxOf(state.frames[state.cur]).getImageData(x, y, 1, 1).data;
        const hex = "#" + [d[0], d[1], d[2]].map((n) => n.toString(16).padStart(2, "0")).join("");
        $("keyColor").value = hex;
        status("picked " + hex);
      }
      view.style.cursor = "";
      view.removeEventListener("click", once);
    };
    view.addEventListener("click", once);
  };
  $("trim").onclick = () => { trimTransparent(); status("trimmed to content"); };
  $("scale").onclick = () => {
    if (!state.frames.length) return;
    const f = parseFloat($("scaleBy").value);
    if (!(f > 0)) return;
    scaleAll(f, $("smooth").checked);
    status("scaled " + f + "x");
  };
  $("resize").onclick = () => {
    if (!state.frames.length) return;
    resizeCanvas(parseInt($("rw").value, 10), parseInt($("rh").value, 10), $("center").checked);
    status("canvas resized");
  };
  $("slice").onclick = () => {
    if (!state.frames.length) return;
    sliceSheet(parseInt($("sc").value, 10) || 1, parseInt($("sr").value, 10) || 1);
    status("sliced into " + state.frames.length + " frames");
  };

  $("exportGif").onclick = () => {
    try {
      const t0 = performance.now();
      const bytes = buildGif();
      download(new Blob([bytes], { type: "image/gif" }), "animation.gif");
      status("GIF written, " + (bytes.length / 1024).toFixed(1) + " KB in "
        + Math.round(performance.now() - t0) + "ms");
    } catch (err) {
      status(err.message, true);
    }
  };
  $("exportPng").onclick = () => {
    const f = state.frames[state.cur];
    if (!f) return status("nothing to export", true);
    f.canvas.toBlob((b) => download(b, "frame" + (state.cur + 1) + ".png"));
  };
  $("exportSheet").onclick = () => {
    if (!state.frames.length) return status("nothing to export", true);
    exportSheet();
  };

  document.addEventListener("keydown", (e) => {
    if (/input|select|textarea/i.test(e.target.tagName)) return;
    if (e.key === " ") { e.preventDefault(); if (state.playing) stop(); else play(); }
    if (e.key === "ArrowLeft") { state.cur = Math.max(0, state.cur - 1); redraw(); }
    if (e.key === "ArrowRight") { state.cur = Math.min(state.frames.length - 1, state.cur + 1); redraw(); }
  });

  if (!("ImageDecoder" in window)) {
    status("this browser cannot split animated GIFs; stills and sheets still work", true);
  }
  redraw();
}
