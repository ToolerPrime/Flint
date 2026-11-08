# Feature parity with Macromedia Fireworks 8

Baseline: Fireworks 8 (2005) as documented in its own manual, not later Adobe
versions. A feature is **Working** only if it functions end to end and has a
test or a verified check. A button that exists is not a feature.

| Status | Meaning |
|---|---|
| Working | Does the job, tested |
| Partial | Some of the job; the note says what is missing |
| Missing | Not built |
| Blocked | Cannot be built as specified; the note says why |

**Where it stands:** Flint is a capable layered pixel-art editor and animator
with audio. New rectangles/ellipses and text retain editable source properties,
and Object (Q) selects objects and nested groups. Bitmap layer masks, live effects, guides,
export preview and movable workspace groups also exist. It is not a complete
Fireworks replacement: pen/node editing, object distribution,
linked symbols and advanced Auto Shapes remain missing.

**Testing caveat:** every "Working" below is verified by automated tests that
drive the real code headless, plus rendered screenshots of the real window.
Core flows were also clicked through the live window once by hand-driven
input (see CHANGELOG 0.6.1); that is not a repeatable suite.

## Document management

| Feature | Status | Notes |
|---|---|---|
| New document | Working | Any size, transparent or filled |
| Open / Save / Save As | Working | Native `.flint`, atomic, round trip tested |
| Autosave and crash recovery | Working | Every 60 s while unsaved; tested by killing a real process |
| Multiple open documents | Missing | One document per window |
| Canvas size | Working | Anchor centre or top-left |
| Image size (resample) | Working | Nearest-neighbour (exact) or smooth |
| Transparent background | Working | |
| Import and placement | Working | Drop one image on the canvas to place it there; dialog for frames, layers, sheets |
| Rulers | Working | Toggle through View; guide suite verifies painting and coordinates |
| Guides, snapping | Working | Ruler guides, canvas edges/centre and optional pixel snapping; audio clips snap to frame starts |
| Pixel grid | Working | From 8× zoom |
| Canvas rotation | Missing | |
| Zoom | Working | Integer 1–32×; Ctrl+wheel around the cursor; actual pixels; fit on screen |
| Clipboard (cut/copy/paste pixels) | Working | Cut, copy, copy merged, paste to a new layer; PNG with alpha for other apps |
| Export area | Missing | |
| Preferences | Missing | |

## Bitmap editing

| Feature | Status | Notes |
|---|---|---|
| Pencil | Working | Pixel-exact, square nib 1–64 |
| Brush (soft, textured) | Missing | |
| Eraser | Working | |
| Paint bucket | Working | Tolerance, respects selection |
| Gradient fill | Working | Linear, radial, or fading to transparent |
| Rectangular marquee | Working | Clips every tool |
| Elliptical marquee | Working | |
| Lasso / polygon lasso | Partial | Freehand lasso; no click-to-click polygon lasso |
| Magic wand | Working | Tolerance, contiguous or global |
| Feather, transform selection | Partial | Selected pixels move and transform; add, subtract, intersect, invert, grow, shrink; no feather |
| Clone stamp | Working | Alt+click source |
| Blur, sharpen, dodge, burn, smudge | Partial | Blur and sharpen as filters; no brushes for them; no dodge, burn, smudge |
| Replace colour | Working | Background removal to transparent; replace background colour with foreground |
| Crop | Working | Crop tool, crop to selection, trim, fit canvas to content |
| Hue/saturation, levels, curves | Partial | Hue/saturation/lightness, levels, brightness/contrast, colourise, posterize, threshold, invert, desaturate with live preview; no curves |
| Eyedropper | Working | Reads the exact composite |
| Move tool | Working | Layer content or selected pixels; arrow-key nudge |
| Free transform (scale, rotate, skew) | Working | Handles plus numeric dialog; Nearest, RotSprite or Smooth; lossless re-transform from the original |
| Rotate / flip 90°, 180° | Working | Layer, selection or whole canvas; exact |
| Filters (blur, sharpen, outline, shadow, pixelate, noise) | Working | Live preview, this frame or all frames, inside the selection |
| Text | Working | New text retains editable source and hard/smooth rendering, transforms, undo and save/reload; live typing check remains limited by automation |
| Library | Working | Beyond Fireworks 8's simple drag-in: images kept with the document, drag back onto the canvas |

## Vector editing

| Feature | Status | Notes |
|---|---|---|
| Line, rectangle, ellipse | Partial | New rectangles/ellipses retain editable geometry/colour/fill/stroke; line remains raster. Object (Q) selects one object |
| Pen, Bézier paths, node editing | Missing | Model reserves an object kind for it |
| Polygon, freeform | Missing | |
| Stroke / fill properties, gradients, patterns | Missing | |
| Align, distribute, group | Partial | Multi-object canvas alignment and nested per-cel grouping; distribution and isolated group blending remain missing |
| Boolean path operations | Missing | |
| Trace bitmap to vectors | Working | Colours, minimum area, smoothing, simplify, smooth or hard edges, live preview; exact at smoothing 0; shapes stay sharp when scaled or rotated |
| Auto Shapes | Missing | |

## Text

| Feature | Status | Notes |
|---|---|---|
| Editable text, fonts, spacing | Partial | New text retains words/font/size/bold/italic/colour/edge settings; no kerning/leading controls. Existing bitmap text stays bitmap |
| Text on path, convert to paths | Missing | |

## Objects and layers

| Feature | Status | Notes |
|---|---|---|
| Multiple layers | Working | |
| Multiple objects per layer | Partial | Objects tab and Q pick/edit/move one object; shapes/text retain source. Painting merges them into pixels; no multi-selection yet |
| Layer visibility, rename | Working | |
| Layer lock | Working | Padlock on each row; enforced by every tool |
| Layer opacity | Working | Undoable |
| Blend modes | Partial | Normal, Multiply, Screen, Add, Subtract |
| Shared layer across frames | Working | Fireworks' "share across frames" |
| Merge down, flatten | Working | |
| Layer groups | Missing | |
| Masks, clipping | Partial | Bitmap cel/layer masks can be added, painted, enabled and applied; no vector clipping groups |
| Symbols, instances, live effects, styles | Partial | Live effects have add/edit/remove/reorder controls; copy/paste appearance exists. No saved style preset library or symbol masters; imported symbol instances expand into ordinary objects |

## Optimisation and export

| Feature | Status | Notes |
|---|---|---|
| Original / Preview / 2-Up / 4-Up | Working | Export preview dialog compares settings |
| Indexed palette, colour count, dither | Working | One palette across all frames, no flicker |
| Animated GIF | Working | Verified with Pillow |
| Animated PNG (APNG) | Working | Exact pixels, full alpha, exact per-frame delays; verified with PIL |
| Animated WebP | Working | Lossless, exact delays incl. last frame; verified with PIL |
| WebM (VP9 + alpha + Opus audio) | Working | Audio sync verified to 0.0 ms |
| MP4 (H.264 + AAC) | Working | No alpha, flattened onto background; sync verified |
| PNG frame, sprite sheet, PNG sequence | Working | Pixel-exact |
| Texture atlas + JSON | Working | Trimmed, deduplicated, Aseprite JSON; rebuild test exact |
| WAV audio | Working | 48 kHz stereo |
| Export a selection or tag only | Working | |
| Progress and cancel | Working | Cancel kills a running ffmpeg |
| File-size preview before export | Working | Export preview encodes candidate settings |
| JPEG, TIFF, still WebP export | Working | StillExport and export suite |
| SVG export | Partial | Traced shapes of the active layer, with their position, rotation, scale and opacity; bitmaps are not exported as SVG |
| Layered PSD / OpenRaster | Missing | |

## Animation

| Feature | Status | Notes |
|---|---|---|
| Frames, per-frame delay | Working | |
| Time-scaled timeline | Working | Frame width = duration, so audio lines up |
| Drag to reorder, drag edge to retime | Working | |
| Insert, duplicate, copy, paste, delete frames | Working | |
| Frame names | Working | Double-click |
| Animation tags (Idle, Walk…) | Working | Follow their frames when frames move |
| Onion skin | Working | Before/after counts, opacity, red/blue tint |
| Loop, ping-pong, once; 0.25×–4× speed | Working | Timing tested in the real form |
| Timeline zoom and scroll | Working | Ctrl+wheel, wheel |
| Tweening, motion paths | Missing | |
| Animation symbols | Missing | |

## Sprite tools (beyond Fireworks)

| Feature | Status | Notes |
|---|---|---|
| Grid slicing (margin, spacing) | Working | |
| Automatic slicing by transparency | Working | Join gap keeps detached pieces together |
| Consistent alignment | Working | Bottom-centre, centre, top-left |
| Pivot points | Working | Pivot tool; exported in atlas JSON |
| Hitboxes | Working | Hitbox tool; exported in atlas JSON |
| Palette locking / swapping | Partial | Import can convert to the project palette; no palette editor |

## Audio (beyond Fireworks)

| Feature | Status | Notes |
|---|---|---|
| Import WAV, MP3, FLAC, OGG, AAC | Working | Drag onto window or lane |
| Multiple tracks, mute, solo, volume | Working | |
| Waveforms | Working | |
| Move, trim, split, duplicate clips | Working | Split is sample-identical |
| Gain, fades, loop | Working | |
| Synchronised playback | Working | Picture follows the audio device clock |
| Microphone recording | Partial | Count-in, level meter, take lands at playhead. Not exercised with a real microphone by automation |
| Latency compensation | Partial | Offset setting exists, default 0, not measured on hardware |

## Web features (lower priority)

| Feature | Status | Notes |
|---|---|---|
| Slices, hotspots, buttons, rollovers | Missing | |
| HTML export | Missing | |
| Batch processing, commands | Missing | |
| History panel | Working | Click any step to jump back or forward to it |

## Fireworks compatibility

| Feature | Status | Notes |
|---|---|---|
| Open Fireworks `.png` flattened | Working | Detected by its private chunks; the import dialog warns that layers will be flattened and the original is untouched |
| Open Fireworks `.png` editable | Partial | Layers, frames and delays, exact bitmaps, vector paths with solid and linear/circular/elliptical gradient fills and strokes, text as its saved picture, groups, symbol instances, and live effects (shadows, glow, bevel, blurs, hue/saturation, colour fill). Not yet: masks, blend modes, other gradient kinds, symbol label overrides; a hidden reference layer keeps the exact saved picture |
| Photoshop `.8bf` plugins | Blocked | A 32-bit plugin cannot load into a 64-bit process |

## Modern architecture

| Feature | Status | Notes |
|---|---|---|
| 64-bit | Working | PE32+ x64 verified |
| Memory-efficient undo | Working | Adding a layer to a 337 MB project costs 512 bytes |
| Empty frames cost nothing | Working | |
| Exact pixels everywhere | Working | Every alpha level through every path, checked against PIL |
| Exports off the UI thread | Working | |
| Memory indicator | Working | Status bar |
| Dark theme, docking, saved workspaces | Working | Dark/light theme; 8 panels plus the main toolbar and tool options bar; drag to dock on any side, several columns per side, tab groups (Group with, rename), dividers, fold a panel or a whole side, float, hide (Window menu, F2 etc., F4); built-in layouts (Default, Compact, Painting, Animation) and named saved layouts; last layout restored on start. Not done: several documents open at once in their own windows |

Productivity update: Image includes fit/trim/clip and resize quality commands for animation content. Group paths use format 10. See the workspace research/productivity-integration.md for verified Fireworks shortcut provenance and Flint extensions.
