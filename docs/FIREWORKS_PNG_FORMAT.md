# Fireworks 8 editable PNG — format notes

Our own notes, written by examining Fireworks PNG files byte by byte so Flint
can open a user's own Fireworks documents (interoperability). No Fireworks code
or assets are used or redistributed. Adobe's sample files were only examined on
the user's machine and are never copied into this repository.

Status markers: **[verified]** checked against real files with a byte-exact
or visual comparison; **[observed]** seen consistently in files but meaning
inferred; **[guess]** unconfirmed.

## Container

A normal PNG. The ordinary `IHDR`/`IDAT` chunks hold the flattened picture, so
any viewer shows it. Fireworks adds private chunks after `IDAT`:

| Chunk | Contents |
|---|---|
| `mkBF` | 72 bytes, starts `FA DE CA FE`, then a big-endian int (`4` seen). [observed] |
| `mkTS` | zlib-compressed **document tree** (the editable document). [verified] |
| `mkBS` | zlib-compressed small tree with a single bitmap; not the document. [observed] |
| `mkBT` | one compressed **pixel tile** each. [verified] |

## Tag tree (`mkTS`, also `mkBS`)

zlib-decompress the chunk body. The result is a tree of nodes, no whitespace:

```
TAG t { value }
```

- `TAG` — 3 ASCII letters.
- `t` — one ASCII letter giving the value type:
  - `v` — a list: child nodes follow until the matching `}`.
  - `i` — integer in **hex** ASCII (e.g. `HPXi{7e}` = 126). Colours are
    `AARRGGBB` hex (`ff003366`). [verified]
  - `f` — decimal ASCII float (`643.000061`). [verified]
  - `b` — `0` or `1`. [verified]
  - `s` — a **2-byte big-endian character count**, then that many UTF-16BE
    characters, then `}`. Strings may contain `{` and `}`; always read by
    length, never by scanning. [verified]
- Repeated tags are allowed and meaningful (lists of `LAYv`, `TRNs`, ...).
- Unknown tags must be skipped; the format is self-delimiting.

Root is `MKBv`. Top-level children seen:

| Tag | Meaning |
|---|---|
| `FRCi` | frame count [observed] |
| `WIDf`, `HITf` | canvas width, height in pixels [verified] |
| `XLCf`, `YLCf` | canvas origin (0,0 seen) [observed] |
| `RESf` | resolution, dpi [observed] |
| `BGCi` | canvas colour (`ffffff`; 8 hex digits when alpha given) [observed] |
| `PLLv`, `FLDv`, `GLDv`, `EFDv`, `PATv` | the user's style/brush/fill/gradient/effect libraries and default attributes — not document content |
| `MSLv` | symbol library (symbol definitions) [observed, empty in most files] |
| `EXPv` | export (optimise) settings [observed] |
| `LYLv` | **layers** [verified] |
| `GRDv`, `GDSv` | grid and guides [observed] |

## Layers and frames

```
LYLv{
  LAYv{                       one per layer, bottom-most first [verified]
    LNMs{name}
    URLb{1}                   the special "Web Layer" (slices, hotspots); skip
    MSLb{1}                   layer shared across frames [observed]
    DISb                      expanded in the Layers panel (UI only)
    CLLv{                     cells, one CELv per frame [observed]
      CELv{
        LCKb                  locked
        ELMv{ ...elements... } objects on this layer in this frame, FRONT to back [verified]
      }
    }
    VIFv{ VISb{1} }           visibility, one VISb per frame [observed]
  }
}
```

Layers are listed bottom first (the Web Layer comes last, as it sits on top in
the panel). Verified by compositing every sample both ways and comparing with
the flattened picture: bottom-first scored 95-100% on one sample, reversed 25%.

**The elements inside an `ELMv` are listed front to back** (the first one is on
top). Verified the same way (one sample went from 95% to 100%, several others
rose by 10-70 points when imported in reverse), and by a sample whose first
path is a vignette that visibly lies over the bitmap listed after it. [verified]

A layer hidden in a frame has `VISb{0}` at that frame's position in `VIFv`.

**Frame delays** are not on the frames (verified again with a Fireworks-made 3-frame file: delays 25/50/100 hundredths
are stored as `FNDi` 19/32/64 hex, and open as 250/500/1000 ms). They are in the export settings:
`EXPv{ FNLv{ FNMv{ FNPi FNDi{7} VISb } ... } }`, one `FNMv` per frame, `FNDi`
in 1/100 s (`7` = 70 ms, Fireworks' default). [observed in one two-frame file]

The canvas colour (`BGCi`, usually white) is not a layer: the flattened picture
has it behind everything, the layers do not.

## Elements (children of `ELMv`)

| Tag | Object | Notes |
|---|---|---|
| `IMGv` | bitmap | see below [verified] |
| `PTHv` | vector path | see below [observed] |
| `TXTv` | text | carries a **cached rendering** as a child `IMGv` [observed] |
| `GRPv` | group | children under its own `ELMv`; also `GRTi`, `LCKb`, `DTAv` |
| `ALSv` | symbol instance (alias) | see Symbols |
| `EFLv` | live effects list on an object | see Live effects |
| `OPAi` | object opacity, hex, 0-1000: `3e8` (1000) is opaque, `320` (800) is 80% [verified] |

### Bitmap `IMGv` [verified]

```
IMGv{
  XLCf, YLCf        position of the bitmap's top-left on the canvas
  XOFf, YOFf        offset (0 seen)
  WPXi, HPXi        width, height in pixels (hex)
  TSZi              tile size (hex 80 = 128)
  TILv{ TIDi{id} ... }  tile ids, row-major: left to right, then down [verified]
  DTAv{}
}
```

Each id names an `mkBT` chunk.

### Pixel tile `mkBT` [verified]

- Bytes 0–3: `FA CE CA FE`.
- Bytes 4–7: tile id, big-endian (matches `TIDi`).
- Bytes 8–11: flag; `0` = colour tile, `1` = seen on 8-bit texture masks [guess].
- Bytes 12–75: zero.
- Bytes 76–: zlib stream. A colour tile decompresses to 128 × 128 × 4 =
  65,536 bytes, **A, R, G, B** per pixel, row-major, top row first.
  Byte-exact against the flattened picture of a single-bitmap document.
- Pixels outside the bitmap's `WPX × HPX` inside the last tile are padding.
- Colour is **straight (not premultiplied) alpha** [verified]: in a sample
  with soft edges one bitmap has the constant colour (0,51,102) at alpha 16,
  32, 48 ... 192; premultiplied data would vary the colour with alpha. The
  composite over the picture below it matches the flattened picture.
- Multi-tile bitmaps are **row-major** [verified]: a 480x214 bitmap (4x2 tiles)
  reassembled row-major is pixel-identical to the flattened picture where
  nothing lies on top (102,720 of 102,720 pixels); column-major matched under 30%.
  Same for 257x374 (3x3) and 357x94.
- Some `mkBT` tiles are not 128x128 colour (texture masks, `TSZi{100}`); they
  are not 65,536 bytes after inflating and are ignored.

### Vector path `PTHv` [verified by rendering]

```
PTHv{
  PATv{                    attributes
    BCLi                   stroke colour (AARRGGBB)
    FCLi                   fill colour
    BPLv{ ... BDIi ... }   brush (stroke); absent = no stroke; BDIi = width in pixels [verified]
    FPLv{ CATs{fc_Solid} } fill type; absent = no fill. Others seen: fc_Linear,
                           fc_Circular, fc_Elliptical (gradients), fc_Pattern
    TXFv / TXBv            fill / stroke texture (masks in mkBT tiles)
  }
  PSXf PSYf PEXf PEYf PFXf PFYf   gradient handle points [verified, see Gradient fills]
  PBLv{                    path = list of sub-paths
    PBPv{
      ISCb{1}              closed
      PPCi                 point count
      PBTv{                one per point
        XLCf, YLCf         anchor
        XPCf, YPCf         control handle before the anchor
        XSCf, YSCf         control handle after the anchor
                           a handle coordinate that is omitted equals the anchor's
        NSDf, RNDi, BZLv   internal (segment length, ids, brush data)
      }
    }
  }
}
```

Stroke width: scaling the width by 0.5, 1 and 2 against a sample with strokes
scored 97.9%, 100% and 99.0%, so `BDIi` is the stroke width in pixels, centred
on the path. [verified] Fills of an open sub-path close implicitly. A path with
several `PBPv` sub-paths fills them together (holes work with even-odd).

Gradients: `FPLv{ FGLv{ FGVv{ FGIv{FGPf FGCi} ... } FGYv{ FG0v{colour stops}
FG1v{opacity stops} } } }`. `FGPf` is the position 0..1, `FGCi` an AARRGGBB
colour (an opacity stop uses only its alpha). Geometry and rendering: see Gradient fills.

Segment from point *i* to *i+1* is a cubic Bézier: P(i).anchor,
P(i).after-handle, P(i+1).before-handle, P(i+1).anchor. A closed sub-path also
joins the last point back to the first.

### Text `TXTv` [observed]

`LFTf TOPf RITf BOTf` box; `FONs` font; `PTSf` size; `BOLb ITLb UNDb`;
`LEDf` leading; `JSTi` alignment; `TFSv{ TRNs{...} TRNs{...} }` the text as
runs; `PATv` fill/stroke like a path; **`IMGv` cached rendering** of the
text with its own position and tiles — use it for an exact import.

### Blend modes `BLDi` [verified]

`BLDi` (hex) on a path, bitmap or text; absent = Normal. Verified with a Fireworks-made file
(blends.png: an orange `#ff6600` square under a blue `#3366ff` one, one pair per mode):
`2` Multiply, `3` Difference, `5` Screen, `9` Darken, `a` Lighten, `b` Hue, `c` Saturation, `d` Color,
`e` Luminosity, `11` Invert, `1d` Tint, `19` Erase. The code `4`, seen only in Adobe's Buttons/Themes library
symbols (no page uses them, so nothing could be scored), is not one of these: it is counted, warned
about and drawn Normal. Fireworks blends against its canvas colour as well (a Screen square on a white canvas
vanishes outside the object below it), so when an imported file uses any known mode Flint adds a bottom layer
holding the canvas colour. Results against the flattened picture: Normal, Multiply, Screen, Darken, Lighten, Difference, Invert,
Erase and Tint 100%; Hue, Saturation, Luminosity 91%; Color 68%. Those four follow the W3C formulas in
Flint but Fireworks' own are HSV-based (measured: Hue keeps the backdrop's HSV saturation and value, Saturation keeps the
backdrop's hue, Luminosity gives the backdrop's hue and saturation at the source's luma, Color is nearly the
plain source). Flint's Tint is fitted to this sample: half-way to the source, in proportion to the backdrop's saturation.

### Opacity scale

`OPAi` sits on paths, text, groups and bitmaps. Read as 0-1000 (value/1000) it
raised the match to the flattened picture (Pocket_PC 75% to 90%); a 0-800 or
0-500 scale was no better than ignoring it, and 0-1200 was worse. A group's
opacity multiplies into its children. [verified]

### Symbols (`MSLv`, `ALSv`) [verified by rendering]

`MSLv{ MSTv{ UNMs name, MSNi id, MSYi type, MOXf MOYf, LYLv{...} } ... }` holds
the definitions. `MSYi` absent = graphic, `1` = button (4 frames: Up, Over,
Down, Over-while-down), `2` = animation. The `LYLv` has the same layer / cell /
element structure as the document; one cell per symbol frame. Symbols nest
(instances inside definitions): guard against cycles.

An instance is `ALSv{ MSNi id, MTXv{M00..M22}, ANNi, ANXf, ANYf, ASCf, XFMi, ... }`.
`ANNi` equals the symbol's frame count; `ANX/ANY/ASC` were 0/0/100 everywhere.
The matrix is row-vector form: x' = x*M00 + y*M10 + M20, y' = x*M01 + y*M11 + M21
(Flint's Affine: A=M00, B=M01, C=M10, D=M11, Tx=M20, Ty=M21). Symbol content is
drawn around its own origin, often with negative coordinates; the translation
M20/M21 places it. Verified on Halo Lite (symbols spanning -88..88 x -104..104 with
M20/M21 = 88/104 exactly fill the 176x208 canvas; Halo Lite scores 97.7%) and global.png
(buttons at M20 58/166/274/382). `MOX/MOY` are 0 in every sample, so whether they shift
the origin is not known; Flint ignores them. Flint shows frame 0 of graphic and button
symbols (a button's Up state; the flattened picture agrees) and, for an animation symbol,
the frame equal to the document frame (clamped). Not verifiable: no sample has an animation
symbol placed in a multi-frame document.

`BUTs` on an instance is its own label text (the symbol's cached text picture says
"TEXT"). Flint draws the label itself from the symbol's text element: `FON` font, `PTS` size in
pixels, `BOL`/`ITL`, the fill colour `PAT>FCL`, `JST` alignment (0 left, 1 centre, 2 right)
inside the box `LFT/TOP/RIT/BOT`, all in symbol space and carried by the instance matrix.
Horizontal placement (centred in the box using the typographic text width) matched the
flattened picture of global.png (Complete) exactly; vertically the line sits 1 px above where
GDI+ would draw it at `TOP`, and plain `AntiAlias` rendering matched the picture's smoothing best
(calibrated on that one sample: four labels, one font). Effects on the text element are applied as
live effects. Only a label that cannot be drawn (empty text) is left out with a warning.

The library files Animations, Bullets, Buttons and Themes hold every
instance at the identity matrix while their flattened picture is a centred preview, so those
cannot be matched by position; Flint places them as the file says (at the origin).

### Gradient fills [verified by rendering]

`FPLv{ CATs fc_Linear | fc_Circular | fc_Elliptical | fc_Rectangular | fc_Conical | fc_Bars | fc_Ripple | fc_Pinch | fc_Waves | fc_Satin | fc_Folds | ... }`, stops in
`FGLv>FGYv>FG0v` (colour) and `FG1v` (opacity; alpha in the top byte); `FGVv` is an
older colour list. Geometry on the `PTHv`, in the path's own coordinates:
`PSX,PSY` start or centre; `PEX,PEY` end (linear) or radius end (circular, radius =
|end - start|); `PFX,PFY` the second radius end (elliptical: radius vectors
end-start and f-start). Verified on FlashLite_Japan: with these the radial vignette
matches the flattened picture (28.7% to 87% with symbols and effects, vignette visibly right).
Colour and opacity stops are merged into one list and interpolated in premultiplied
space; interpolating them independently scored far worse (87% to 55%).

The real category names are `fc_Rectangular`, `fc_Conical`, `fc_Ripple`, `fc_Pinch` (there is no
starburst in Fireworks 8). From Fireworks-made files (gradients.png, gradients2.png: one 60x60 square per
kind, red-yellow-blue stops) these kinds were fitted. Using the frame (a, b) = coordinates of a point
along the first axis end-start and the second axis f-start, both measured from the start point:
Rectangular t = max(|a|,|b|); Conical t = |angle|/pi (angle from the first axis); Bars t = zig(|a|);
Ripple t = zig(radius); Pinch t = |a*b|; zig folds x back and forth (0..1 up, 1..2 down). All
of them score 100% (tolerance 8) against Fireworks' pictures, once the gradient is sampled at the
pixel corner instead of the pixel centre (this also raised the older Linear/Circular/Elliptical
scores: FlashLite_Japan 93.4% to 94.2%). `fc_Waves`, `fc_Satin` and `fc_Folds` are real but more complex than
Flint's approximations (Satin and Folds have hyperbolic and fan-like structure, Waves a diagonal band
whose edge is not a sine): they score about 3-6% and are reported as approximated. `fc_ContourGrad`
failed to apply in Fireworks' scripting, so no sample exists; `fc_Contour*` and `fc_Pattern`
stay flat (stops averaged) with a warning.
Textures (`TXFv`, `TXBv`) and gradient strokes are ignored.

### Live effects (`EFLv`) [partly verified]

`EFLv{ EPSv{ DCEv{DCKs key, DCVs value} ... } EPSv{...} }`: **one `EPSv` per
effect, applied in order** (an object often has a bevel then a drop shadow in the
same `EFLv`). The effect is identified by `EffectMoaID` (the `name` key is often absent;
each bag also carries unrelated keys of other effects). `EffectIsVisible` `false` = off.

| MoaID prefix | Effect | Keys used |
|---|---|---|
| `a7944db8` | Drop Shadow | ShadowColor `#rrggbbaa`, ShadowBlur, ShadowDistance, ShadowAngle |
| `5600f702` | Inner Shadow | same |
| `2ba87123` | Glow | GlowWidth, MaskSoftness, BevelContrast (opacity %), OuterBevelColor (glow colour) |
| `7fe61102` | bevel family | BevelType (0 inner bevel, 1 outer bevel, 2 inset emboss, 3 raised emboss, 4 inner glow [guess]), BevelWidth, BevelContrast, LightAngle, HiliteColor, ShadowColor, GlowWidth |
| `d04ef8c0` | Gaussian Blur | gaussian_blur_radius |
| `f1cfce41` / `f1cfce42` | Blur / Blur More | (none) |
| `538016b6` | Motion Blur | motion_blur_angle, motion_blur_distance |
| `3439b08d` | Hue/Saturation | hue_amount, saturation_amount, lightness_amount, hls_colorize |
| `3439b08c` | Brightness/Contrast | brightness_amount, contrast_amount |
| `dd54adc0` | Color Fill | Color, Opacity, Blendmode (only 0 = normal handled) |

Verified: the shadow angle is counter-clockwise from the right, so 315 is down and
right (Drop shadows lifted Symbian from 86.5% to 93.7%). The cached `IMGv` of a text
object **already contains its effects** (adding them again lowered Sample Template from
100% to 93%), so effects on text are not applied again. Effect lists inside `EFDv` (the
style library) are not on objects. Shadow blur, glow softness, bevel strength are
approximations tuned on the samples, not Fireworks' own filters.
Effects on a group, `Add Noise`, radial/zoom blur, unsharp mask, curves and levels, and
colour fills with another blend mode are skipped with a warning.

### Not decoded

Masks (no sample could be made; skipped with a warning), textures, `fc_ContourGrad`/`fc_Pattern`
fills, exact Waves/Satin/Folds gradients, and text styling beyond font, size, bold, italic, colour and
alignment of an instance label. Flint skips these and says how many it skipped.
