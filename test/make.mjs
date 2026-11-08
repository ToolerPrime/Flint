import { writeFileSync } from "node:fs";
import { encodeGif } from "../src/gif.js";
import { buildHistogram, quantize, makeIndexer } from "../src/quant.js";

const cases = {};

// 1. Tiny, exact. Four colours, two frames, distinct delays.
{
  const palette = [[255,0,0],[0,255,0],[0,0,255],[255,255,255]];
  const f0 = Uint8Array.from([0,1,2,3, 1,2,3,0, 2,3,0,1, 3,0,1,2]);
  const f1 = Uint8Array.from([3,3,3,3, 2,2,2,2, 1,1,1,1, 0,0,0,0]);
  cases.tiny = {
    gif: encodeGif({ width:4, height:4, palette,
      frames:[{indices:f0, delay:120},{indices:f1, delay:500}], loop:0 }),
    expect: { width:4, height:4, palette, frames:[[...f0],[...f1]], delays:[120,500] },
  };
}

// 2. Gradient of distinct indices: walks the code width up through 4..9 bits.
{
  const W = 64, H = 64, n = 256;
  const palette = Array.from({length:n}, (_,i)=>[i, (i*7)&255, (i*13)&255]);
  const idx = new Uint8Array(W*H);
  for (let i = 0; i < idx.length; i++) idx[i] = (i * 31) % n; // long non-repeating runs
  cases.widths = {
    gif: encodeGif({ width:W, height:H, palette, frames:[{indices:idx, delay:100}], loop:null }),
    expect: { width:W, height:H, palette, frames:[[...idx]], delays:[100] },
  };
}

// 3. Big pseudo-random field: drives the dictionary to 4096 and forces a reset.
{
  const W = 220, H = 220, n = 256;
  const palette = Array.from({length:n}, (_,i)=>[(i*5)&255, i, (255-i)&255]);
  const idx = new Uint8Array(W*H);
  let s = 12345;
  for (let i = 0; i < idx.length; i++) { s = (s*1103515245 + 12345) & 0x7fffffff; idx[i] = (s>>>7) & 255; }
  cases.reset = {
    gif: encodeGif({ width:W, height:H, palette, frames:[{indices:idx, delay:40}], loop:0 }),
    expect: { width:W, height:H, palette, frames:[[...idx]], delays:[40] },
  };
}

// 4. Transparency: index 0 transparent across two frames.
{
  const palette = [[0,0,0],[200,30,40],[20,180,90]];
  const f0 = Uint8Array.from([0,1,1,0, 0,2,2,0, 0,1,1,0, 0,0,0,0]);
  const f1 = Uint8Array.from([1,1,0,0, 2,2,0,0, 1,1,0,0, 0,0,0,0]);
  cases.alpha = {
    gif: encodeGif({ width:4, height:4, palette, transparentIndex:0,
      frames:[{indices:f0, delay:80},{indices:f1, delay:80}], loop:0 }),
    expect: { width:4, height:4, palette, frames:[[...f0],[...f1]], delays:[80,80], transparent:0 },
  };
}

// 5. Quantizer: 2 synthetic frames, one shared palette, then map back.
{
  const W = 48, H = 48;
  const mk = (phase) => {
    const px = new Uint8ClampedArray(W*H*4);
    for (let y=0;y<H;y++) for (let x=0;x<W;x++) {
      const o=(y*W+x)*4;
      px[o]   = (x*5 + phase) & 255;
      px[o+1] = (y*5) & 255;
      px[o+2] = ((x+y)*3 + phase*2) & 255;
      px[o+3] = 255;
    }
    return px;
  };
  const a = mk(0), b = mk(60);
  const h = buildHistogram([a,b], 5);
  const palette = quantize(h, 64);
  const nearest = makeIndexer(palette, 5);
  const toIdx = (px) => { const o = new Uint8Array(W*H);
    for (let i=0;i<o.length;i++) o[i]=nearest(px[i*4],px[i*4+1],px[i*4+2]); return o; };
  const ia = toIdx(a), ib = toIdx(b);
  cases.quant = {
    gif: encodeGif({ width:W, height:H, palette, frames:[{indices:ia,delay:100},{indices:ib,delay:100}], loop:0 }),
    expect: { width:W, height:H, palette, frames:[[...ia],[...ib]], delays:[100,100],
              source:[[...a],[...b]] },
  };
  console.log(`quant: palette=${palette.length} colours from 2 frames`);
}

const manifest = {};
for (const [name, c] of Object.entries(cases)) {
  writeFileSync(`out/${name}.gif`, c.gif);
  manifest[name] = c.expect;
  console.log(`${name}.gif  ${c.gif.length} bytes`);
}
writeFileSync("out/manifest.json", JSON.stringify(manifest));
