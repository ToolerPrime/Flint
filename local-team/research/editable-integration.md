# Editable objects increment — 2026-11-08

Implemented in C:/Users/Techn/Flint. This builds on the prior workspace preview.

## User-visible behavior

- **Object (Q)** picks and moves one visible, unlocked object. Empty space clears
  the pick. Transparent pixels are not hit targets. Linked layer masks stay put
  during an individual object move. **Move (V)** continues to move the whole
  active layer or its pixel selection.
- The Inspector has an **Objects** tab with the active cel's objects. Select one
  there or with Q, then double-click, press Enter with Q active, or choose Edit.
- Rectangle and Ellipse tools default to **editable**. Their dimensions, colour,
  filled/outline choice and stroke width remain source properties. The render
  cache and vector contours are replaced on edits. The pixel-mode checkbox,
  existing pixel selections and mask painting retain raster drawing behavior.
- New text retains its wording, font family, pixel size, bold/italic, colour and
  smooth-edge setting. The Object properties dialog changes these later while
  keeping the accumulated transform. Existing bitmap text and imported cached
  Fireworks text do not acquire recoverable wording automatically.
- Objects > Actions includes clone-and-nudge (+10,+10), delete, explicit
  rasterization, alignment to canvas edges/centres, and copy/paste appearance.
  Appearance copying preserves target shape geometry or target text wording;
  effect parameter dictionaries are copied. This is a session clipboard, not
  a saved style preset library.
- Alignment targets one selected object and the canvas. There is no multi-object
  selection/distribution yet. Rounded corners, stars, symbols and connectors
  remain future increments.

## Model and persistence

PrimitiveSpec and TextSource are immutable records. RasterObject remains the
rendered storage wrapper, with optional primitive/text source alongside Vector
or Original and Matrix. Moves, rotations, clones and effect changes retain
source metadata; destructive bitmap operations intentionally drop it.
Properties create replacement objects; shared undo pixel data is not modified.

Format **9** adds `primitive` and `textSource` object fields while retaining
cached images and existing source/matrix data. Older versions still load. Older
Flint builds reject version 9 instead of silently losing editable metadata.
New source dimensions, text size/length and measured render area are bounded.
The ellipse is a 256-vertex approximation rendered through the existing tracer.

Painting still uses Flint's existing raster merge, followed by the paint stroke.
Undoing both steps restores the editable sources. Use live effects to retain
editable content; destructive pixel filters remain destructive.

## Local coding agents

- Qwen/Ollama generated PrimitivePaths.cs in
  runs/primitive-paths-20261108-094838. The coordinator reviewed it and removed
  verbose/misleading comments; the geometry algorithm was accepted. It creates
  fresh contours, filled geometry or an even-odd outline ring, and valid solid
  geometry when a thick stroke consumes the interior.
- Qwen/Ollama generated CanvasAlignment.cs in
  runs/canvas-alignment-20261108-095206. Its arithmetic was reviewed and accepted.
- No paid worker fallback or Claude API call was used. Document/format/UI
  integration and acceptance review were performed by the coordinator.

## Validation

The initial model tests failed for missing retained-source APIs (editable-red-build.log).
The UI tests then failed for missing object-selection/property actions
(editable-ui-red.log), before those actions were integrated.

`--editabletest` covers retained geometry, outline holes, consumed strokes,
transform/effect/clone source preservation, editing text, exact cache persistence,
format-9 round trip, version-8 loading, real canvas shape handlers, individual
versus whole-layer moves, properties, appearance, cloning, alignment, rasterize,
delete, undo, layer lock, raster painting/undo and actual fractional matrices.

The existing TestStroke observer called EnsureRaster and created a blank paint
object when observing vector output. It now observes layer pixels without
mutating the document. The guide test now inspects the canvas composite instead
of assuming every drawn shape owns a canvas-sized bitmap. Its pixel assertion
is unchanged. The transform matrix also now honours disabled pixel snapping;
the earlier workspace test checked session parameters but not the stored matrix.

Full suite: research/editable-suite.log. Build has zero warnings/errors.

Live checks: drew a retained rectangle, switched to Q, opened Object properties
and changed width from 28 to 40 pixels; observed the changed geometry. The new
text dialog and its retained-editing instructions were inspected. Attempts to
enter wording through the Windows automation adapter failed: its multiline
field target was unavailable and keyboard/paste input went to the owner window.
Thus **live text typing/editing was not verified**. Text render/edit/undo and
save/reload passed the real-form automated tests. Do not describe those tests
as a completed manual text flow. Scratch documents/windows were closed; the
user's original running release was not replaced.

## Review and delivery

Reviewed metadata propagation in every source-preserving RasterObject path;
With(bitmap) deliberately rasterizes. Effects follow the selected object;
otherwise the previous topmost-object behavior remains. Object actions enforce
layer locks, pause playback before dialogs/edits, and use structural undo.
No new package dependency or native Fireworks code/assets were added.

The separate self-contained preview lives at build/editable-preview/Flint.exe.
No commit, push, or replacement of dist-desktop/Flint.exe was made.
