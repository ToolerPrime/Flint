# Fireworks feature discovery — 2026-11-08

## Recommendation

The most valuable direction is retained editable objects, with individual object
selection and properties. Build editable rectangles/ellipses/text first, then
parameter-driven rounded rectangles, stars and polygons. This enables alignment,
style reuse, symbols and connectors without flattening the document.

Useful small additions can proceed independently: align the active layer to the
canvas, clone-and-nudge, repeat the last transform, and a click-to-click polygon
lasso. These are independent implementation proposals; their exact Fireworks
behavior has not been exercised in this investigation.

## What was inspected

1. Verified the installed English Fireworks resource DLL against REA's saved
   artifact SHA256 and verified every inspected string-table payload hash.
   Parsed 3,262 nonempty resource strings and retained 376 family-matched rows
   (a row can appear in more than one family). Exact strings, IDs and byte
   offsets are in `fireworks-feature-clues.json`. Resource declarations identify
   candidate features; they do not establish native handler behavior.
2. Analyzed three shipped Auto Shape scripts through pinned REA 6.8.0:
   Star, Rounded Rectangle and Connector Line. The installed `.jsf` files were
   copied byte-for-byte into an ignored research snapshot with `.js` filenames
   for parser admission. The original paths and SHA256 hashes are retained in
   `fireworks-shapes-manifest.json`. Nothing was executed or copied into Flint.
3. REA reported all three sources parsed, 92,941 source bytes, 20,623 visited
   AST nodes and zero parse failures. Its semantic graph contains 42 function
   nodes, 391 call-site nodes and 4,615 unknown records. Overall semantic coverage
   is **unknown**, with partial call/data-flow families. Static function names
   and source ranges are usable; host callbacks and dynamic behavior are not
   fully resolved.
4. REA's requested evidence-file export failed with `execution_failure` after
   parsing. The error response included `partial_observation`; this was retained
   intact under `research/runtime/fireworks-shapes-evidence.json`, alongside
   the raw response. The small `fireworks-shapes-summary.json` records the
   failure and statistics. This is a recovered partial observation, not a
   successful native decompilation or a successful REA export. No unchanged
   retry was made.
5. Compared candidate features with current Flint implementation files, rather
   than relying on its stale parity/limitations documents. No UI, source or
   executable changes were made to Flint during this research pass.

## Candidates worth building

| Candidate | Fireworks evidence | What it adds to Flint | Dependency / implementation size |
|---|---|---|---|
| Editable Auto Shapes | Script redraw functions read stored geometry parameters; resource IDs 12159–12160 expose insertion/editing commands | Change shape dimensions, corners, star points and radii after drawing | First retain primitive source parameters plus render cache, undo/save and object properties. Medium-to-large vertical slice. |
| Align and distribute | Resource IDs 10411–10412; Align.swf is also shipped | Place objects precisely and space them evenly | Small first slice: active-layer content aligned to canvas edges/centre. General selected-object alignment needs object selection; distribution needs at least three selectable items and an explicit spacing rule. |
| Clone-and-nudge / repeat transform | Resource IDs 10239–10246 and 10722/10971 | Quickly create repeated details at consistent offsets | Small-to-medium: clone independent object wrappers while sharing immutable source data; retain transform command parameters, apply one undo step per invocation. Separate from the existing clone-stamp brush. |
| Copy appearance / style presets | Resource IDs 10471, 10501 and 12112 | Reuse shadows, glow, colours and other appearance settings | Existing effect lists give a useful starting point. Define which properties are copied; deep-copy mutable effect parameter dictionaries. Start with effect presets before promising complete stroke/fill styles. |
| Editable text properties | Text-on-path label 10936; kerning/leading/baseline labels 14618/14619/14885 | Reopen and change words/font/spacing without rebuilding a text bitmap | High user value. Retain text/font/formatting source with transformed render cache; native format migration and undo coverage. Text-on-path comes later. |
| Reusable symbols | Resource IDs 10979, 11034, 11040 and 11088 | Update a master graphic and reflect it in multiple instances | Large: master definitions, instance transforms, cycle prevention, shared undo semantics and persistence. Native symbols can be built before importing Fireworks symbol relationships. |
| Connector lines | Shipped Connector Line.jsf functions getElementUnderCP, attachLine, redrawLine | Diagram and UI mockup connections | Script inspection shows a control point can be placed on an element's boundary. It does **not** prove persistent follow-on-move behavior. Stable object identity and explicit attachment rules should precede linked connectors. |
| Polygon lasso | Resource IDs 10776 and 12083 | Precise selections from clicked vertices | Small-to-medium. Existing Selection.FromPolygon can supply the mask; add vertex editing/preview, close, cancel and modifier handling. |
| Export area | Resource IDs 10854 and 12062 | Export a region while keeping the canvas intact | Medium. Define output bounds independently of crop and apply them consistently across preview/still/animation exports. |
| Tween instances | Resource ID 10984 and descriptive action label 35188 | Generate intermediate positions/transforms across frames | Larger. Define interpolation and duration, then build on editable objects or symbols. Do not infer easing or interpolation rules from the label. |

Curves, soft brush tips and feathering also appear in the resources. They are
useful later, but feathering requires soft selection coverage instead of Flint's
current hard bit mask, and curves should have an explicit channel/alpha policy.
Legacy rollovers, hotspot HTML export and selective JPEG are lower priority for
the user's editable-graphics workflow. Sprite-sheet slicing remains intentionally
absent from Properties as requested.

## Concrete Auto Shape observations

These are observations of inert shipped script, not a claim that the script was
run or that REA recovered the native host implementation:

- **Star:** `RedrawSmartShape`, lines 428–446, reads retained outer/inner radii,
  two corner-rounding parameters and point count, then calls its geometry
  builder. `createStar` begins at line 500. This supports a parameter-model and
  redraw design for a new Flint shape implementation.
- **Rounded Rectangle:** `RedrawSmartShape`, lines 794–880, reads four corner
  sizes, four corner types and width/height. `createRectangle` is at 939–970;
  `switchType` starts at 972. This supports independent corner properties and
  resizing separately from corner size. It does not justify copying the shipped
  script into Flint.
- **Connector Line:** `getElementUnderCP`, 1129–1138, queries elements at a
  control point. `attachLine`, 1140–1165, calculates a point on a target element's
  edge/centre according to orientation. `redrawLine` starts at 1167. Following
  targets after future moves is still unverified.

Source ranges are the same in the original `.jsf` files and byte-identical
analysis copies. `fireworks-shape-function-index.json` retains the corresponding
REA node IDs and exact source ranges for all 42 functions.

## Existing Flint features excluded from the gap list

- Masks: MainForm.Mask.cs already has add/paint/enable/apply/delete operations.
- Live effects: MainForm.Effects.cs already has add/edit/remove/reorder UI and
  preserves original source through effect changes.
- Export preview and still formats: MainForm.Optimize.cs, OptimizeDialog.cs and
  StillExport.cs already implement these.
- Rulers/guides/snapping: MainForm.Rulers.cs and Guides.cs; pixel snapping and
  grouped movable workspace hosts were added in the previous increment.
- Bitmap clone stamp, clipboard, gradients, image library and frame copying
  already exist. They are not replacements for object cloning, reusable symbol
  instances or retained editable text.

Flint evidence also confirms the important remaining gap: MainForm.Edit.cs
AddText renders wording to a bitmap; drawing primitives are raster operations;
Document.cs's ObjectKind.Text entry alone does not implement editable text.

## Local worker review

The free Ollama Qwen worker completed a six-item triage proposal in
`runs/fireworks-feature-triage-20261108-011752`. Its recommendations were reviewed
against source. Accepted as candidate ideas, not proof of behavior. One dependency
was corrected: native symbol support does not require Fireworks import changes
first. Its polygon-lasso comparison to clone stamp was irrelevant. The final
priorities and implementation prerequisites above are coordinator judgments.

## Next useful implementation order

1. Individual object selection/properties plus persistent editable rectangles
   and ellipses; preserve exact raster data and existing transforms.
2. Editable text source/rendering, with save/reload and undo.
3. Per-corner rectangles, stars/polygons; align/distribute selected objects.
4. Appearance presets, reusable symbols and linked connectors.

Clone-and-nudge, active-layer-to-canvas alignment and polygon lasso can be delegated
as separate small tasks alongside the retained-object work. Shared document/UI
integration remains coordinated by one writer at a time.
