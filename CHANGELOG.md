# Changelog

## Unreleased — Fireworks 8 keyboard shortcuts

- Keyboard shortcuts now follow Fireworks 8's default set (Macromedia Standard). Tool letters match Fireworks, and pressing a letter again switches to the next tool in its group (V, M, U, G). Changed: Import Ctrl+R, Export Ctrl+Shift+R, Export preview Ctrl+Shift+X, numeric transform Ctrl+Shift+T, fit canvas Ctrl+Alt+F, trim canvas Ctrl+Alt+T, ungroup Ctrl+Shift+G, merge down Ctrl+E.
- New: clone (Ctrl+Shift+D), duplicate (Ctrl+Alt+D), clone-and-nudge (Alt+arrows), align (Ctrl+Alt+1–6), arrange (Ctrl+Up/Down, with Shift for front/back), paste attributes, rotate 90°, zoom levels (Ctrl+1–8), guide toggles, Page Up/Down frames, play (Ctrl+Alt+P), D and X colours, Ctrl+Shift+Z redo, Backspace delete, Ctrl+Q exit.
- Quick 50%/200% object scaling has no shortcut now, because Fireworks uses those keys for alignment.
- Flint opens a file given on launch, so double-clicking or "Open with" works for Fireworks PNGs.

## Unreleased — Fireworks productivity commands

- Added multi-object selection, nested grouping (Ctrl+G), ungroup (Ctrl+Alt+G / Ctrl+U), and flatten selection (Ctrl+Alt+Shift+Z). Groups are stored in format 10; older formats load.
- Added canvas fit and destructive off-canvas clipping for the current frame or whole animation. Fit preserves source data; clipping partly intersecting objects rasterizes them. Undo restores the originals.
- Added all-frame image size with Nearest, RotSprite and Smooth quality, numeric transform (Ctrl+Alt+T), and quick selected-object or whole-animation 50%/200% scaling. Frame delays and editable sources survive resizing.
- Added --productivitytest; all 24 checks and the complete suite pass. Live grouping, movement, ungroup, flatten, fit and quick-scale shortcuts were exercised.

## Unreleased — retained objects, editable shapes and text

- Object (Q) picks and moves one object; Objects tab lists the active layer/frame's objects. Double-click or Edit opens properties.
- Rectangles and ellipses retain dimensions, fill/outline, stroke and colour. New text retains wording, font, size, bold/italic, colour and edge settings.
- Added clone-and-nudge, selected-object canvas alignment, copy/paste appearance, explicit rasterize and undoable object delete.
- Source properties survive transforms/effects/undo and version-9 save/reload. Versions 1–8 still load; old bitmap text remains bitmap.
- Whole-layer Move (V) stays separate from individual Object (Q). Pixel snap off now preserves fractional stored matrices.
- New --editabletest passes with the full suite. Live shape creation/properties passed; live text typing was blocked by the automation adapter and is covered by automated tests instead.

## Unreleased — movable workspace and pixel snapping

- Removed sprite-sheet slicing controls from Properties.
- Canvas background colour now has its own control: undoable, saved, shared across frames, with a Transparent action. Paint background remains a secondary brush colour.
- View > Snap to pixels controls whole-pixel movement and straight resize edges; off permits fractional transforms.
- Tools, Inspector and Timeline can float, resize and dock back. Panel menus and View > Workspace include reset. Panel positions are session-only.
- Added --workspacetest for background persistence, undo, pixel snapping and panel lifetime/reset. Full suite and live background/float checks passed.

## 0.12 — dark mint look, icons, Frames view, export preview, rulers

- **New look**: dark grey/black interface with mint green highlights (a light
  theme is under View > Theme). Menus, panels, dialogs and the timeline all
  follow the theme.
- **Icons** for every tool, file action, playback control and layer action
  (54 original line icons drawn in code), with tooltips naming each tool
  and its shortcut. Tools are grouped Select / Bitmap / Vector / Web-Game /
  View.
- **Options bar** above the canvas shows only the current tool's settings.
  Transform, slicing, background removal and export settings moved into a
  collapsible Properties area.
- **Timeline | Frames** switch: the time-scaled timeline with audio, or a
  Fireworks-style Frames view with equal-size frames, names and delays.
- **Export preview** (File > Export preview, Ctrl+Shift+E): compare 1, 2 or
  4 export settings side by side with the real file size of each.
- **New export formats**: JPEG, WebP (lossless and lossy), BMP, TIFF, PNG-8.
- **Rulers and guides** (View > Rulers, Ctrl+Alt+R): drag guides out of the
  rulers, snap to guides and canvas edges, saved in the project (format 8).
  Fireworks documents' guides import too.
- **Fix:** the test runner could not report a failing suite; it now waits
  for each one and fails properly.

## 0.11 — blend modes, layer masks, more gradients, effects panel

- **Blend modes**: 21 modes (W3C formulas) per layer and per object,
  including Overlay, Soft/Hard Light, Dodge/Burn, Hue/Saturation/Color/
  Luminosity, plus Fireworks' Invert, Tint and Erase.
- **Layer masks**: add (reveal all, hide all, from selection), paint the mask
  with the normal tools, disable, apply, delete. Masks follow canvas rotate,
  flip, crop, trim and scale, and merge/flatten/duplicate keep the look.
- **Gradient kinds**: Rectangle, Cone, Bars, Ripples, Pinch, Waves, Satin,
  Folds, plus Flint's own Starburst and Diamond, in the Gradient tool and in
  vector shapes.
- **Effects tab**: add, edit (live preview), reorder, switch off or remove
  live effects on an object.
- **Fireworks import**: all 13 Fireworks blend modes (codes verified with
  files made in Fireworks 8 itself), Rectangular/Conical/Bars/Ripple/Pinch
  gradients exact, button label overrides drawn, gradients sampled the way
  Fireworks does (pixel corners). Frame delays re-verified.
- **Fixes** from review: merge down kept masks/blends of both layers and no
  longer multiplies a shared layer once per frame; mask editing turns off
  when switching layer or frame.
- Project format version 7 (masks); older files still open.

## 0.10 — Fireworks symbols, gradients and effects

- Opening a Fireworks document now brings in **symbol instances** (drawn from
  the file's symbol library through each instance's position and scale;
  nested symbols work), **linear, circular and elliptical gradient fills**
  with colour and opacity stops, and **live effects**: drop shadow, inner
  shadow, glow, inner bevel, Gaussian blur, motion blur, hue/saturation and
  colour fill.
- Effects are live on Flint objects: move, rotate or scale an object and its
  shadow or glow is redrawn to match. Painting on the object bakes them in.
- Vector shapes can carry gradient fills, and they stay sharp when scaled.
- Pixel match against Fireworks' own saved picture, before → after:
  Halo Lite 15 → 98%, FlashLite 29 → 93%, Symbian 72 → 94%, UIQ 78 → 96%,
  Pocket PC 90 → 97%. Tyler's own files remain exact.
- Project format version 6 (gradients and effects); older files still open.

## 0.9 — open Fireworks documents with their layers

- **File > Open** (or dropping the file on the window) now opens a Fireworks
  8 editable PNG as a real Flint document: its layers, frames and frame
  timings, bitmaps with exact pixels, vector paths as sharp vector objects
  (solid fills and strokes), and text as the exact picture Fireworks saved.
  Groups are unpacked. The Web Layer is skipped.
- A hidden, locked "Fireworks flattened (reference)" layer at the bottom
  keeps the exact picture Fireworks saved, so nothing is lost.
- Anything that cannot be brought across is listed when the file opens:
  symbols, live effects (bevel, shadow, glow), masks and blend modes are
  skipped; gradient fills are drawn as one flat colour.
- Tyler's own Fireworks files reproduce their picture exactly. Layered
  documents examined so far reproduce 72–100% of pixels; symbol-heavy
  library files much less.
- The format is documented in `docs/FIREWORKS_PNG_FORMAT.md`, worked out by
  examining files. No Fireworks code or assets are used.
- Saving goes to a new `.flint` file; the original `.png` is never changed.

## 0.8 — trace bitmap

- **Trace bitmap** (Image menu), like Adobe Animate's: turns a layer's pixels
  (or just the selection) into vector shapes. Settings: number of colours
  (0 keeps every colour exactly), minimum area (merges specks), smoothing,
  simplify, and smooth or hard edges, with a live preview that runs in the
  background so the window does not freeze.
- With smoothing at 0 the traced shapes reproduce the picture exactly, pixel
  for pixel, including soft edges.
- Traced shapes stay sharp however far you scale or rotate them, because
  every transform redraws them from the shapes, not from pixels.
- **Export layer as SVG** saves the shapes as a vector file other programs
  can open, matching what is on screen (position, rotation, scale, opacity).
- Project format version 5 stores traced shapes; older files still open.
- **Fix:** pressing Enter after clicking a side-panel button clicked that
  button again instead of applying an open transform. Enter and Esc now
  always go to an open transform or crop.
- **Fix:** another command used while a transform was open could change the
  document underneath it. The open transform is now applied first.

## 0.7 — image-editor tools and the library

- **Drop to canvas:** one image dropped on the canvas lands where you let go,
  on a new layer, and is kept in the new **Library** tab. Shift-drop, several
  files or an animated GIF still open the import dialog. Images imported
  through the dialog are added to the library too.
- **Library:** thumbnails of the document's images, saved inside the
  `.flint`. Drag one onto the canvas to place a copy where you drop it,
  double-click to place it in the middle, add the current selection, import
  files, rename, delete.
- **Move tool** (V): drag a layer's content, or the selected pixels. Arrow
  keys nudge by 1 pixel, Shift+arrow by 10.
- **Free transform** (Ctrl+T): drag inside to move, handles to scale (Shift
  keeps proportions), outside to rotate (Shift snaps to 15°). Enter applies,
  Esc cancels. Numeric transform for exact width, height, angle and skew.
  Rotate 90°/180° and flip, all exact.
- **Lossless transforms:** placed, pasted and moved images keep their
  original pixels, so rotating or scaling again re-renders from the original
  instead of degrading. Rotating 30° and back returns the exact pixels.
- **Resampling choice:** Nearest, RotSprite (pixel-art rotation that keeps
  only the sprite's own colours with cleaner edges) or Smooth.
- **Crop tool** (C), crop to selection, fit canvas to content, rotate and flip
  the whole canvas.
- **Selections:** ellipse, lasso and magic wand (tolerance, contiguous or
  not); Shift adds, Alt subtracts, both intersect. Select menu: all, none,
  invert, layer content, grow, shrink. Marching ants follow any shape.
- **Clipboard:** cut, copy, copy merged, paste (onto a new layer, back where
  it was copied from). Copies keep alpha for other apps (PNG on the
  clipboard).
- **Filters menu** with live preview: invert, desaturate,
  brightness/contrast, hue/saturation/lightness, levels, colourise,
  posterize, threshold, replace colour, blur, sharpen (unsharp mask),
  outline, drop shadow, pixelate, add noise. This frame or all frames.
- **Gradient tool** (linear, radial, or fading to transparent), **clone
  stamp** (Alt+click the source), **text tool** (hard pixels or smooth edges).
- **View menu:** zoom in/out, actual pixels, fit on screen.
- Project format version 4.

## 0.6.1 — first live-window test

The published app was driven by real mouse and keyboard input for the first
time. Tested this way: pencil, rectangle, add frame, add layer (menu, and the
"+" button through Windows UI Automation), History tab and jumping, Ctrl+Z,
play/stop, dragging a frame to reorder, GIF export through the save dialog
(checked with Pillow), the import dialog with palette preview, sprite-sheet
import, Save, and crash recovery after killing the process.

- **Fix:** the History list did not update after a paint stroke or a property
  change; it only refreshed on layer or frame changes.
- **Fix:** automatic sprite-sheet slicing found nothing on a sheet with a solid
  background (no transparency), and imported the whole sheet as one frame. It
  now ignores the top-left colour on fully opaque sheets, keeps it in the
  frames, and says so in the import dialog.
- "1 frames" and "1 colours" now read "1 frame" and "1 colour".

## 0.6 — import, sprite tools, formats, safety

- History panel: every step listed; click to jump back or forward.
- Layer lock toggle on each Layers row.
- Ctrl+wheel zooms the canvas around the cursor, up to 32×.
- Fireworks `.png` documents are detected and the import dialog warns that
  their layers will come in flattened.
- Import dialog: "Preserve original pixels" on by default; palette
  conversion off by default with a before/after preview; plain warnings for
  JPEG and embedded colour profiles.
- Dropped images become successive frames by default (they used to stack as
  layers on one frame). Layers, one image and sprite-sheet modes available.
- Sprite sheets slice on a grid or automatically, aligned bottom-centre.
- Texture atlas export: trimmed, deduplicated, Aseprite-style JSON with
  durations, tags, pivots and hitboxes; WAV and audio cues alongside.
- Pivot and Hitbox tools. Project format v3.
- Animated PNG and animated WebP export, both with exact pixels and exact
  per-frame delays.
- Exports run in the background with a progress bar and Cancel; Cancel stops
  a running ffmpeg. Large saves show progress too.
- Autosave every 60 s and crash recovery on next launch.
- **Fix:** GDI+ was altering about 92% of semi-transparent pixels on copy,
  crop, flip, scale, import, load and compositing. All of these now work on
  raw bytes and are exact.
- **Fix:** undo snapshots shared pixels instead of copying the whole project;
  adding a layer to a 337 MB project costs 512 bytes instead of ~674 MB.
- **Fix:** an unknown command-line switch no longer opens a window that an
  automated run would hang on.

## 0.5 — timeline and audio

- Time-scaled timeline: frame width equals duration, so audio lines up.
- Drag to reorder frames, drag a frame's edge to change its delay, name
  frames, tag ranges (Idle, Walk…); tags follow their frames.
- Copy, paste, insert and delete frame ranges.
- Loop, ping-pong and once playback; 0.25×–4× speed.
- Onion skin with before/after counts, opacity and red/blue tint.
- Audio tracks: import WAV/MP3/FLAC/OGG/AAC, waveforms, mute/solo/volume,
  move, trim, split, fade, gain, loop, snap to frames.
- Microphone recording with count-in and level meter.
- Video export carries audio (Opus in WebM, AAC in MP4); MP4 added; WAV export.
- **Fix:** an unbounded filter made MP4 export write hours of video, and a
  blocking read meant the ffmpeg timeout never fired.

## 0.4 — layers and projects

- Layered document model, shared layers, blend modes, Layers panel.
- Native `.flint` format, versioned and atomic.
- Delta undo with a memory budget, and redo.

## 0.2 — native desktop app

- Native WinForms app, self-contained x64 executable.
- Drawing tools, new canvas, frame animation, GIF/PNG/sheet/WebM export.

## 0.1 — browser prototype (retired)

- Kept in `src/` and `dist/` as reference only.
