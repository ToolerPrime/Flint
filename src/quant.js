/* Variance-based color quantization.
 *
 * Ported to JavaScript from colorquant.c as shipped in Macromedia Fireworks 8
 * (Configuration/Third Party Source Code/Color Quantization). Original notice,
 * preserved as its license requires:
 *
 *   This software is copyrighted as noted below. It may be freely copied,
 *   modified, and redistributed, provided that the copyright notice is
 *   preserved on all copies.
 *   There is no warranty or other guarantee of fitness for this software,
 *   it is provided solely "as is".
 *   Author: Craig Kolb, Department of Mathematics, Yale University
 *   Date:   Tue Aug 22 1989
 *   Copyright (C) 1989 Craig E. Kolb
 *
 * Ported to JS 2026-11-06 for Flint; source of this port is this file.
 *
 * Deviation from the original: UpdateFrequencies() incrementally maintained
 * each box's projected frequency arrays. Here projectFreq() recomputes them
 * from the histogram over the box extent. Same math, less state; the cost is
 * O(colormax^3) per cut, which is ~32k iterations at the default 5 bits.
 */

const R = 0, G = 1, B = 2;

function newBox(colormax) {
  return {
    weightedvar: 0,
    mean: [0, 0, 0],
    weight: 0,
    freq: [new Float64Array(colormax + 1), new Float64Array(colormax + 1), new Float64Array(colormax + 1)],
    low: [0, 0, 0],
    high: [colormax, colormax, colormax],
  };
}

function copyBox(src, colormax) {
  const d = newBox(colormax);
  d.weightedvar = src.weightedvar;
  d.mean = src.mean.slice();
  d.weight = src.weight;
  for (let c = 0; c < 3; c++) d.freq[c].set(src.freq[c]);
  d.low = src.low.slice();
  d.high = src.high.slice();
  return d;
}

/* Accumulate RGBA pixel runs into one histogram in reduced colour space.
 * Passing every frame here is what gives an animation a single shared
 * palette instead of one palette per frame (the original's accum_hist). */
export function buildHistogram(framePixelArrays, bits = 5, alphaThreshold = 128) {
  const shift = 8 - bits;
  const side = 1 << bits;
  const hist = new Float64Array(side * side * side);
  let total = 0;
  for (const px of framePixelArrays) {
    for (let i = 0; i < px.length; i += 4) {
      if (px[i + 3] < alphaThreshold) continue; // transparent pixels get their own index
      const idx = ((px[i] >> shift) << (2 * bits)) | ((px[i + 1] >> shift) << bits) | (px[i + 2] >> shift);
      hist[idx]++;
      total++;
    }
  }
  return { hist, bits, side, total };
}

function projectFreq(h, box) {
  const { hist, bits, side } = h;
  for (let c = 0; c < 3; c++) box.freq[c].fill(0);
  let weight = 0;
  for (let r = box.low[R]; r < box.high[R]; r++) {
    for (let g = box.low[G]; g < box.high[G]; g++) {
      const base = (r << (2 * bits)) | (g << bits);
      for (let b = box.low[B]; b < box.high[B]; b++) {
        const n = hist[base | b];
        if (n === 0) continue;
        box.freq[R][r] += n;
        box.freq[G][g] += n;
        box.freq[B][b] += n;
        weight += n;
      }
    }
  }
  box.weight = weight;
  void side;
}

function boxStats(box, sumPixels) {
  if (box.weight === 0) { box.weightedvar = 0; return; }
  box.weightedvar = 0;
  for (let color = 0; color < 3; color++) {
    let mean = 0, varsum = 0;
    for (let i = box.low[color]; i < box.high[color]; i++) {
      const f = box.freq[color][i];
      mean += i * f;
      varsum += i * i * f;
    }
    box.mean[color] = mean / box.weight;
    box.weightedvar += varsum - box.mean[color] * box.mean[color] * box.weight;
  }
  box.weightedvar /= sumPixels;
}

function findCutpoint(h, box, color, sumPixels, colormax) {
  if (box.low[color] + 1 === box.high[color]) return null;
  const minindex = Math.floor((box.low[color] + box.mean[color]) * 0.5);
  const maxindex = Math.floor((box.mean[color] + box.high[color]) * 0.5);

  let cutpoint = minindex, optweight = box.weight, curweight = 0;
  for (let i = box.low[color]; i < minindex; i++) curweight += box.freq[color][i];

  let u = 0, max = -1;
  for (let i = minindex; i <= maxindex; i++) {
    curweight += box.freq[color][i];
    if (curweight === box.weight) break;
    u += (i * box.freq[color][i]) / box.weight;
    const d = box.mean[color] - u;
    const v = (curweight / (box.weight - curweight)) * d * d;
    if (v > max) { max = v; cutpoint = i; optweight = curweight; }
  }
  cutpoint++;
  const clip = Math.min(Math.max(cutpoint, box.low[color]), box.high[color]);

  const b1 = copyBox(box, colormax), b2 = copyBox(box, colormax);
  b1.high[color] = clip;
  b2.low[color] = clip;
  projectFreq(h, b1); projectFreq(h, b2);
  boxStats(b1, sumPixels); boxStats(b2, sumPixels);
  void optweight;
  return [b1, b2];
}

function cutBox(h, box, sumPixels, colormax) {
  if (box.weightedvar === 0 || box.weight === 0) return null;
  let best = null, bestVar = Infinity;
  for (let c = 0; c < 3; c++) {
    const pair = findCutpoint(h, box, c, sumPixels, colormax);
    if (!pair) continue;
    const tv = pair[0].weightedvar + pair[1].weightedvar;
    if (tv < bestVar) { bestVar = tv; best = pair; }
  }
  return best;
}

/* Returns an array of [r,g,b] at most maxColors long. */
export function quantize(h, maxColors = 256) {
  const colormax = h.side;
  const sumPixels = Math.max(h.total, 1);
  const boxes = [newBox(colormax)];
  projectFreq(h, boxes[0]);
  boxStats(boxes[0], sumPixels);

  while (boxes.length < maxColors) {
    let whichbox = 0, max = -1;
    for (let i = 0; i < boxes.length; i++) {
      if (boxes[i].weightedvar > max) { max = boxes[i].weightedvar; whichbox = i; }
    }
    const pair = cutBox(h, boxes[whichbox], sumPixels, colormax);
    if (!pair) break;
    boxes[whichbox] = pair[0];
    boxes.push(pair[1]);
  }

  const scale = 256 / colormax;
  return boxes
    .filter((b) => b.weight > 0)
    .map((b) => b.mean.map((m) => Math.min(255, Math.max(0, Math.round(m * scale + scale / 2)))));
}

/* Nearest-colour lookup, memoised over the reduced cube (the original's
 * inv_cmap / rgbmap, computed on demand instead of up front). */
export function makeIndexer(palette, bits = 5) {
  const shift = 8 - bits;
  const side = 1 << bits;
  const cache = new Int16Array(side * side * side).fill(-1);
  return function nearest(r, g, b) {
    const key = ((r >> shift) << (2 * bits)) | ((g >> shift) << bits) | (b >> shift);
    const hit = cache[key];
    if (hit >= 0) return hit;
    let best = 0, bestD = Infinity;
    for (let i = 0; i < palette.length; i++) {
      const dr = r - palette[i][0], dg = g - palette[i][1], db = b - palette[i][2];
      const d = dr * dr + dg * dg + db * db;
      if (d < bestD) { bestD = d; best = i; if (d === 0) break; }
    }
    cache[key] = best;
    return best;
  };
}
