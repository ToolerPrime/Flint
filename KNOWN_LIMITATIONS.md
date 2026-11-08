# Known limitations

What is wrong or missing *now*, and what it means in practice. Feature gaps
against Fireworks are in `FEATURE_PARITY.md`.

## Testing

- **Dragging files from File Explorer onto the canvas has not been tested
  live**: screen control may only click Explorer, not drag from it. Dragging
  from Flint's own library onto the canvas was tested live and uses the same
  drop handler; placing a dropped file (`TryPlaceFile`, which a file drop
  calls) is covered by `--toolstest`.
- **Live-window testing is manual, not repeatable.** On 2026-11-07 the
  published app was driven by real mouse and keyboard input (see CHANGELOG
  0.6.1), which found two bugs. That was a one-off session, not an automated
  suite; the timeline edge-drag, audio import and recording, tags, onion skin
  and every export format except GIF were not clicked through. The automated
  suites still call the form's handlers directly.
- Crash recovery restores the document but not its undo history.
- **Microphone recording is untested with a real microphone.** The recording
  path is tested with synthetic samples; device capture is not.

## Audio

- Recording latency compensation defaults to 0 ms and has not been measured
  on real hardware. If takes land consistently early or late, the offset
  will need setting.
- Audio plays only in loop and once modes at 1× speed. Ping-pong and other
  speeds play silently, and say so.
- Sources are held as 32-bit float at 48 kHz: about 23 MB per minute of
  audio. Fine for dialogue; heavy for long music beds.
- The timeline shows audio lanes in a fixed height; with more than about four
  tracks the lower ones are cut off (no vertical scroll yet).

## Memory and speed

- Operations that genuinely replace every image (flip, scale, trim, colour
  key on all frames) cost their full size in undo, because undo has to keep
  the originals. Layer and frame structure edits are nearly free.
- Compositing reads each bitmap fresh every time a frame renders. Fine for
  sprites; slow for many large layers.
- Autosave runs on the UI thread (GDI+ bitmaps cannot be read safely from
  another thread while the editor may draw on them). On a very large project
  it can pause the window briefly every minute while there are unsaved
  changes. It never runs during a stroke or playback.

## Editing

- Newly drawn editable rectangles/ellipses and text retain source properties. Older bitmap text and imported cached text remain images. Lines remain raster; there is no node editor, rounded-corner primitive or star tool yet.
- Object selection is one object at a time. Canvas alignment and appearance clipboard exist; multi-object distribution, persistent style presets and linked symbols do not.
- Painting still merges the layer into pixels; destructive pixel filters also drop editable source. Undo restores it. Live effects retain source properties.
- Painting on a layer that holds placed or transformed images merges them
  into plain pixels first (one undo step). Anything past the canvas edge is
  cut off at that point.
- Free-transform handles do not account for skew; skew is set only in the
  numeric transform dialog.
- Selections are hard-edged: no feathering or anti-aliased edges.
- No brush softness, smudge, dodge/burn, blur brush or layer groups yet. Bitmap layer masks already have editing controls.
- No dedicated zoom tool; zoom is the View menu, the +/− buttons and Ctrl+wheel.
- Layer lock is not an undoable step.
- The library is outside undo on purpose: deleting a library image cannot be
  undone (you are asked first), and undoing a placement keeps the image in
  the library.
- RotSprite falls back to Nearest for images over 1 megapixel (the 8×
  upscale would need too much memory) and says so.

## Trace bitmap

- With smoothing above 0, each colour is smoothed on its own, so tiny
  transparent pinholes can appear where two colours meet. Smoothing 0 has
  none.
- Smoothing rounds corners and shrinks shapes very slightly (about 2–3% of
  the area on a small circle).
- Reducing colours flattens soft edges: semi-transparent pixels become solid
  or are dropped.
- Tracing a photo-like image with every colour kept makes tens of thousands
  of shapes; the dialog starts at 16 colours when the image has more than
  1024 and says why.
- Traced shapes cannot be edited point by point yet, and painting on them
  turns them back into pixels.

## Opening Fireworks documents

- Masks and blend modes are not imported; the file opens with a list of what
  was skipped.
- Gradient kinds other than linear, circular and elliptical (rectangle, cone,
  starburst, bars, ripples, waves, satin, folds, pattern) come in as one flat
  colour.
- Symbol instances become ordinary objects (the link to the symbol is not
  kept). Button label overrides cannot be drawn and are skipped. Animated
  symbols in multi-frame documents are unverified.
- Live effects are close approximations, not Fireworks' exact maths. Effects
  on groups, Add Noise, radial/zoom blur, unsharp mask, curves/levels and
  colour fill with non-normal blending are skipped. Only one bevel type was
  checked against a real file.
- Effects can't yet be added or edited in Flint; they only come from imports.
- Fireworks' own symbol-library files (Buttons, Themes, Bullets, Animations)
  open with every symbol at the origin, so they look nothing like their
  saved preview.
- Text comes in as the picture Fireworks saved, so the words cannot be edited.
- Open paths always get round stroke caps; brush softness and textures are
  ignored.
- Layers hidden in only some frames lose their cells in those frames (with a
  warning).
- The Fireworks canvas colour is not painted; Flint's canvas stays transparent.
- Frame delays were verified on only one multi-frame file.

## Blend modes, masks, gradients (0.11)

- Hue, Saturation, Color and Luminosity follow the W3C definitions; Fireworks'
  own versions differ in overlaps (91%, 91%, 68%, 91% match).
- Waves, Satin and Folds gradients only approximate Fireworks' patterns;
  Contour and Pattern fills come in flat.
- Fireworks masks are not imported yet (no sample file could be made by
  script; needs one made by hand in Fireworks).
- A linked mask follows only whole-pixel moves of a whole layer, not
  selections, scaling or rotation.
- Merging a layer that differs per frame into a shared layer is refused with
  a message.

## Import

- Animated GIF frames come from GDI+, which does not always apply GIF
  disposal methods correctly; unusual GIFs may import with traces of earlier
  frames.
- Palette conversion uses the document's current colours; there is no
  palette editor or palette file import yet.

## Export

- Video and WebP need ffmpeg (`winget install Gyan.FFmpeg`).
- Video frame rate is the gcd of all frame delays, capped at 100 fps; delays
  that are not multiples of 10 ms are rounded.
- GIF: 256 colours and one-bit transparency; partial alpha is thresholded at
  50%. Use APNG or WebP for soft edges.
- MP4 has no alpha; transparency is flattened onto the background colour.

## Format

- `.flint` is at version 3. Builds older than version 3 refuse a version-3
  file with a clear message instead of opening it with data missing.
- Vector and text objects do not exist yet; a future file containing them
  would open in this build with those objects dropped and a warning shown.

## Export preview, rulers and guides

- Export preview: for lossy animated WebP the decoded picture is the current frame only (Play holds it); GIF, APNG and lossless WebP preview every frame. PNG-8, JPEG, BMP, TIFF and WebP stills export the current frame. WebP needs ffmpeg.
- Guides do not follow a canvas crop, rotate or flip; snapping is off until View > Guides > Snap to guides is ticked. Dragging near a visible, unlocked guide grabs it whatever the tool (lock guides to paint beside one).
- Move/transform snap the dragged box (left/centre/right, top/middle/bottom); scale and rotate do not snap. Lasso, wand and text do not snap.

## Productivity commands

- Groups work within one layer/cel and have no isolated blend/opacity container. Flatten selection accepts adjacent objects with Normal blending only.
- All frames share one canvas. Fitting to the current frame can place other frames' content outside it; source data remains available. Choose whole-animation fit to include every frame.
- Fit bounds do not subtract layer masks. Partly clipped objects become bitmaps; undo restores editable source. Current-frame clipping may need to separate shared cels and refuses estimated duplication above 256 MB.
- Image resizing is limited to 16,384 pixels per side and 16,777,216 total pixels. Live shortcut checks covered basic editing; all-frame clipping, timing preservation and resize undo are automated checks.
