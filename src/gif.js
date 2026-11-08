/* Minimal GIF89a encoder: global palette, animation, transparency.
 * Written for Flint. No dependencies.
 *
 * One global colour table shared by every frame is the point: a per-frame
 * palette is what makes exported sprite animations shimmer. */

class ByteSink {
  constructor() { this.buf = new Uint8Array(1 << 16); this.len = 0; }
  _room(n) {
    if (this.len + n <= this.buf.length) return;
    let cap = this.buf.length;
    while (cap < this.len + n) cap *= 2;
    const next = new Uint8Array(cap);
    next.set(this.buf.subarray(0, this.len));
    this.buf = next;
  }
  byte(b) { this._room(1); this.buf[this.len++] = b & 0xff; }
  bytes(arr) { this._room(arr.length); this.buf.set(arr, this.len); this.len += arr.length; }
  short(v) { this.byte(v); this.byte(v >> 8); } // GIF integers are little-endian
  ascii(s) { for (let i = 0; i < s.length; i++) this.byte(s.charCodeAt(i)); }
  done() { return this.buf.slice(0, this.len); }
}

/* LZW as GIF specifies it: LSB-first codes, variable width, flushed in
 * sub-blocks of at most 255 bytes. */
function lzwCompress(indices, minCodeSize, out) {
  const clearCode = 1 << minCodeSize;
  const eoiCode = clearCode + 1;

  let dict = new Map();
  let codeSize = minCodeSize + 1;
  let next = eoiCode + 1;

  let acc = 0, accBits = 0;
  const block = [];

  const flushBlock = () => {
    if (!block.length) return;
    out.byte(block.length);
    out.bytes(Uint8Array.from(block));
    block.length = 0;
  };
  const emit = (code) => {
    acc |= code << accBits;
    accBits += codeSize;
    while (accBits >= 8) {
      block.push(acc & 0xff);
      acc >>= 8;
      accBits -= 8;
      if (block.length === 255) flushBlock();
    }
  };
  const resetDict = () => { dict = new Map(); codeSize = minCodeSize + 1; next = eoiCode + 1; };

  emit(clearCode);
  if (indices.length === 0) { emit(eoiCode); }
  else {
    let cur = indices[0];
    for (let i = 1; i < indices.length; i++) {
      const k = indices[i];
      const key = (cur << 8) | k;
      const found = dict.get(key);
      if (found !== undefined) { cur = found; continue; }
      emit(cur);
      if (next < 4096) {
        dict.set(key, next++);
        // Lockstep with the decoder, which is the subtle part. A decoder only
        // forms its first table entry on the SECOND data code, so its table is
        // always one entry behind ours. It widens when ITS next code reaches
        // 1<<codeSize, so we must test next-1, not next. Testing `next` widens
        // one code early and every decoder then misreads the stream.
        if (next - 1 >= (1 << codeSize) && codeSize < 12) codeSize++;
      } else {
        emit(clearCode);
        resetDict();
      }
      cur = k;
    }
    emit(cur);
    emit(eoiCode);
  }

  if (accBits > 0) {
    block.push(acc & 0xff);
    if (block.length === 255) flushBlock();
  }
  flushBlock();
  out.byte(0); // block terminator
}

/**
 * frames: [{ indices: Uint8Array (width*height), delay: ms, disposal?: 0..3 }]
 * palette: [[r,g,b], ...] up to 256 entries
 * transparentIndex: palette slot to treat as transparent, or null
 * loop: 0 = forever, n = repeat n times, null = no loop block (play once)
 */
export function encodeGif({ width, height, frames, palette, transparentIndex = null, loop = 0 }) {
  if (!frames.length) throw new Error("no frames");
  if (!palette.length) throw new Error("empty palette");
  if (palette.length > 256) throw new Error("palette over 256 entries");
  for (const f of frames) {
    if (f.indices.length !== width * height) {
      throw new Error(`frame has ${f.indices.length} indices, expected ${width * height}`);
    }
  }

  // Colour table size must be a power of two, at least 2.
  let tableBits = 1;
  while (1 << tableBits < palette.length) tableBits++;
  const tableSize = 1 << tableBits;

  const out = new ByteSink();
  out.ascii("GIF89a");
  out.short(width);
  out.short(height);
  out.byte(0x80 | ((tableBits - 1) & 7)); // global table present, size
  out.byte(transparentIndex === null ? 0 : transparentIndex); // background
  out.byte(0); // pixel aspect ratio

  for (let i = 0; i < tableSize; i++) {
    const c = palette[i] || [0, 0, 0];
    out.byte(c[0]); out.byte(c[1]); out.byte(c[2]);
  }

  if (loop !== null && frames.length > 1) {
    out.byte(0x21); out.byte(0xff); out.byte(11);
    out.ascii("NETSCAPE2.0");
    out.byte(3); out.byte(1); out.short(loop); out.byte(0);
  }

  // GIF delays are in hundredths of a second. Sub-10ms rounds to 0, which
  // many viewers silently re-interpret as 100ms, so clamp to 1.
  for (const f of frames) {
    const cs = Math.max(1, Math.round((f.delay ?? 100) / 10));
    const disposal = f.disposal ?? (transparentIndex === null ? 1 : 2);
    out.byte(0x21); out.byte(0xf9); out.byte(4);
    out.byte((disposal << 2) | (transparentIndex === null ? 0 : 1));
    out.short(cs);
    out.byte(transparentIndex === null ? 0 : transparentIndex);
    out.byte(0);

    out.byte(0x2c);
    out.short(0); out.short(0);
    out.short(width); out.short(height);
    out.byte(0); // no local table, not interlaced

    const minCodeSize = Math.max(2, tableBits);
    out.byte(minCodeSize);
    lzwCompress(f.indices, minCodeSize, out);
  }

  out.byte(0x3b);
  return out.done();
}
