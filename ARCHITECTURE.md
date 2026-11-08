# Flint architecture

Native Windows app: C# on .NET 8, WinForms UI, one self-contained x64
executable. One NuGet dependency (NAudio, for sound devices). ffmpeg is an
optional external tool for video and some decoding.

## Why WinForms

It was already in place and working, and nothing built so far needed what it
lacks. Rewriting the UI for looks alone is out of scope. Revisit if docking
panels, theming or high-DPI become a real obstacle — record the reason here
first.

## Systems

```
UI ─ MainForm (wiring), LayersPanel, TimelinePanel, ImportDialog,
     NewCanvasDialog, ProgressDialog
 │
 ├─ Document model ─ Document, Layer, Cel, RasterObject, FrameSpec, AnimTag
 │     ├─ Audio model ─ AudioProject, AudioTrack, AudioClip, AudioSource
 │     ├─ Undo ─ PixelEdit, StructureEdit, PropertyEdit, AudioEdit, UndoStack
 │     └─ Project file ─ FlintFormat (.flint), Recovery (autosave)
 │
 ├─ Pixels ─ ImageOps (exact transforms, compositor), Drawing (tools),
 │           Quantizer, Onion
 ├─ Time ─ Playback (loop/ping-pong/once, speed), AudioMixer, AudioEngine
 ├─ Import ─ Importer, AudioIO, Sprites (slicing)
 └─ Export ─ Exporter → GifEncoder, ApngEncoder, WebpWriter,
                        WebmExport (ffmpeg: WebM, MP4), Sprites (atlas)
```

Nothing below the UI line depends on a window, which is why every system has
headless tests.

## Document model — `Document.cs`

```
Document ─ Width, Height, Frames[], Tags[], Audio, Library[], Layers[] (bottom to top)
  Layer ─ name, visible, locked, shared, opacity, blend
    Cels: Dictionary<frame, Cel>   (sparse; shared layer uses key 0)
      Cel ─ Objects[]   RasterObject today; vector/text reserved
        RasterObject ─ Pixels, X, Y, and optionally Original or Vector, + Matrix + Mode
FrameSpec ─ delay, name, pivot, hitboxes
```

Frames are an axis, not a container. A missing cel is an empty frame and costs
nothing. A shared layer has one cel that every frame shows.

**Lossless objects.** A placed, pasted or lifted image keeps its untouched
`Original` and the accumulated `Affine` matrix; `Pixels` is a rendering of
that. Every further move, rotate or scale composes the matrix and re-renders
from the original (`RasterObject.Retransform`), so rotating ten times costs one
resample, and rotating back lands on the exact original bytes (matrix entries
are snapped to remove floating noise). Paint tools write only a plain
canvas-sized bitmap, so painting on a layer first merges its objects
(`EnsurePaintable`, one undo step). `EnsureRaster` never returns a transformed
object, because painting it would make it disagree with its original.

**Traced vectors — `Trace.cs`.** `RasterObject.Vector` holds a `VectorArt`
(per-colour shapes of closed polygons, even-odd filled) in place of an
`Original` bitmap. `Pixels` is `Tracer.Render(Vector, Matrix)`, re-rendered
from the shapes on every transform, so vectors stay sharp at any scale. The
object's own `Mode` (Smooth = antialiased) wins over the transform tool's
resample setting. Tracing labels same-colour regions and walks their
pixel-edge boundaries (region kept on the right, right turn first at
corner-touching vertices), so with no smoothing the 1× render is
byte-exact. Smoothing is Douglas-Peucker plus Chaikin corner cutting with
sharp corners kept. Antialiased rendering gives each 4×4 sub-sample to one
shape and averages once per pixel, so touching opaque shapes leave no
see-through seams. `VectorArt` is never changed after tracing, so undo
snapshots can share it.

**Library.** `Document.Library` is a list of reusable images. It is outside
undo on purpose: snapshots share the same list, so undoing a placement never
removes the image from the library.

## Image-editor tools — `MainForm.Edit.cs`, `Transform.cs`, `Selection.cs`, `Adjust.cs`

- `Selection` is one bit per pixel. Rectangle, ellipse, lasso and wand all
  produce the same mask; Shift adds, Alt subtracts, both intersect. Every
  drawing primitive clips through it.
- `Transform.Render` maps a bitmap through an affine matrix. Nearest copies
  source pixels; RotSprite samples an 8× Scale2x upscale (only source colours,
  cleaner diagonals); Smooth is bilinear in premultiplied alpha. Whole-pixel
  moves and quarter turns are exact.
- A transform session (`XformSession`) snapshots the document, lifts the
  selection into its own object if there is one, re-renders on every mouse
  move, and commits as one structure edit (Esc or Ctrl+Z cancels).
- `Adjust` filters are pure functions on bytes; the editor applies them
  through `MapRasters`, inside the selection, padding placed objects so blur,
  outline and shadow are not cut off.

## Two rules everything depends on

**1. Pixels are exact.** GDI+ drawing converts through premultiplied alpha even
with `SourceCopy`; measured, it changed about 92% of semi-transparent pixels on
a plain copy. So clone, crop, reframe, flip, nearest scaling, image reading
and compositing all work on raw BGRA bytes (`ImageOps`, `Document.RenderPixels`).
GDI+ drawing is used only where resampling is the point (smooth scaling) and
for on-screen display. `--fidelitytest` holds this.

**2. Shared objects are never changed in place.** Undo snapshots share pixel
objects with each other and the live document, which is what makes structural
undo nearly free. That is only safe because:

- the only in-place pixel change is a recorded paint stroke (`PaintRecorder`
  → `PixelEdit`), and strokes are always undone before older snapshots;
- every other operation builds new objects through `Document.MapRasters` /
  `RasterObject.With` and swaps them in;
- nothing disposes a bitmap that a snapshot may still hold.

`--memtest` interleaves strokes and structural edits and checks undo returns
the exact original.

## Undo — `Undo.cs`

| Edit | Stores | Used for |
|---|---|---|
| `PixelEdit` | dirty rectangle, before and after | paint tools |
| `StructureEdit` | two shared snapshots | layers, frames, tags, transforms |
| `PropertyEdit` | two closures | opacity, blend, delay, name, pivot |
| `AudioEdit` | two audio snapshots (sources shared) | tracks and clips |

`StructureEdit.Bytes` counts only pixels its before-state holds that its
after-state does not. The stack is bounded by a byte budget (512 MB), not a
step count.

## Project file — `FlintFormat.cs`

Zip: `project.json` plus `images/*.png` (lossless) and `audio/*.wav` (32-bit
float). Saved beside the target and moved into place, so a crash mid-save
leaves the old file intact. Version 5 (4 added `library/*.png` and, per object,
`original` + `matrix` + `resample`; 5 adds a `vector` node for traced shapes);
versions 1–4 still load. A newer
version is refused with a message. Unknown object kinds load with a warning.

`Recovery.cs` autosaves to `%LOCALAPPDATA%\Flint\Recovery` and offers back any
copy whose process is gone.

## Time and audio

All audio is converted at import to 48 kHz stereo float, so the mixer never
resamples and positions are exact sample offsets. `AudioMixer.MixFrames` is a
pure function of the project and a sample range; the player reads consecutive
blocks of it, addressed in samples so they join seamlessly.

During playback with audio, the picture follows the output device's reported
position, not a timer, so it cannot drift from the sound. `Playback` answers
"which frame is showing at time t" rather than counting ticks, so a late timer
tick skips to the right frame instead of slowing the animation.

## Timeline — `TimelinePanel.cs`

Owner-drawn. Frame cells are as wide as their duration, so audio waveforms
line up under the frames they play against. Thumbnails are cached and built
only for visible frames.

## Export — `Exporter.cs`

Runs on a worker thread behind a modal `ProgressDialog`. Modal on purpose: it
blocks edits, so reading the document's bitmaps from another thread is safe.

| Format | How | Verified by |
|---|---|---|
| GIF | `GifEncoder` + `Quantizer`, one global palette | Pillow |
| APNG | each frame via the PNG encoder, re-filed as fcTL/fdAT | PIL |
| WebP | ffmpeg makes lossless stills; Flint writes the ANMF container | PIL + chunk parse |
| WebM, MP4 | ffmpeg; CFR at the gcd of delays; audio muxed | ffprobe + decoded click timing |
| Atlas | trimmed, deduplicated, shelf-packed; Aseprite JSON | rebuild from atlas + JSON |
| PNG, sheet, sequence, WAV | direct | PIL |

Why the container writers exist: ffmpeg's WebM and animated-WebP writers both
gave the final frame the wrong duration (~30 ms and 192 ms). Video uses a
constant frame rate to avoid it; WebP and APNG write per-frame delays directly.

## Tests

Built into the exe as `--` switches, so the shipped binary checks itself:

| Switch | Covers |
|---|---|
| `--doctest` | model, compositing, shared layers, undo deltas, `.flint` |
| `--animtest` | playback modes, speed, onion planning |
| `--audiotest` | mixer, decoding, storage, sync in exported video |
| `--memtest` | large project, undo cost, snapshot integrity |
| `--fidelitytest` | every alpha level through every path, against PIL |
| `--crashtest` | kills a real Flint process, recovers its work |
| `--formattest` | APNG, WebP, sheet, sequence via PIL; cancellation |
| `--spritetest` | slicing, atlas rebuild, pivots, hitboxes |
| `--importtest` | import modes, size handling, palette, notes |
| `--drawtest` | drawing primitives |
| `--canvastest` | real mouse handlers on a real form; timeline and audio wiring |
| `--edittest` | transforms, RotSprite colours, lossless re-transform, selections, filters, library in the file |
| `--tracetest` | exact trace round trip, holes, colours, smoothing, seams, SVG, vector transforms, v5 file, dialog preview/cancel/apply, speed |
| `--fwtest` | Fireworks PNG parser, synthetic document, user's own files exact, sample scores |
| `--toolstest` | move, transform, crop, selections, clipboard, filters, gradient, clone, text through the real form |
| `--shot` | renders the window; fails on clipped, overlapping or collapsed controls |
| `--opstest`, `--selftest`, `--webmtest` | image ops, GIF encoder, WebM |

`test/run_all.ps1` runs them all. Any `--` launch sets `Program.Headless`,
which suppresses modal prompts; an unknown `--` switch exits with an error
rather than opening a window.

## Movable workspace increment

MainForm.Workspace.cs and WorkspacePanel.cs wrap the existing Tools, Inspector and Timeline controls. Each host reparents its original controls into an owned tool window or the canvas surround. Native WinForms docking keeps the canvas protected, and reset restores default z-order and document-dependent timeline height. The Inspector remains one group; panel locations are session-only. This adds docking without replacing the UI toolkit. Canvas colour is a normal locked shared bottom layer; no new project-file fields are required. PixelSnap.cs was generated by a bounded local Ollama worker and verified by the coordinator.

## Retained source objects (format 9)

EditableSources.cs supplies immutable PrimitiveSpec and TextSource records. RasterObject stores optional source metadata alongside the existing Vector/Original and Matrix, so existing compositing, masks, live effects and source-preserving transforms still apply. Editing replaces source geometry/cache and metadata; destructive With(bitmap) drops them. Format 9 serializes primitive/textSource with cached images; older formats load without these fields. MainForm.Objects.cs owns per-cel individual selection, property editing, cloning, canvas alignment and appearance clipboard; Object (Q) operates on one picked object, while Move (V) retains whole-layer behavior. The selection is a guarded UI index/context, not a persistent symbol identity or multi-object selection system. Symbols/connectors need a later identity model.

## Object groups and canvas editing (format 10)

ObjectGroups validates bounded immutable-by-convention GroupPath copies on RasterObject. Nested membership lives on the flat cel stack, preserving existing compositing order; it is not an isolated rendering container. Selection holds multiple indices in one active layer/cel. Clone remaps group IDs. Format 10 adds optional groups arrays; missing arrays in older files mean no membership.

CanvasEdits prepares replacement objects, masks and marker coordinates before committing changes. Fit translates all cels without discarding off-canvas source. Clip explicitly cuts cached pixels and clears editable sources on partial intersections. Current-frame clipping of shared content prepares independent cels under a memory guard. Resize composes affine transforms and uses the chosen resampler while keeping editable sources and frame timing. ResizePlan checks dimensions before allocation. MainForm.Productivity and MainForm.Grouping connect these operations to one-step structural undo and shortcut routing.
