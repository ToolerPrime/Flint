---
name: photocraft-port
description: Use when adapting PhotoCraft colour-picker, brush or other source designs into Flint; follow the port plan, translate to C#/WinForms and preserve attribution.
---

# PhotoCraft ports

Read `docs/PhotoCraft-port-plan.md`, `CREDITS_LICENSES.md` and the relevant
Flint code from the repo root. The plan has historical baseline text: compare
it with current code before deciding what is missing.

Find the actual upstream source at https://github.com/storytold/photocraft.
Read the chosen revision's licence and file notices; record revision and
source files in the port evidence. The existing plan uses PhotoCraft's
MIT/Apache-2.0 choice and Flint's existing ports choose MIT. Check the exact
source being reused; do not assume every dependency or asset has that licence.

Useful source map from the plan:
- Picker layout: `crates/ui-egui/src/dialogs/color_picker_ui.rs`.
- Colour conversions (including prospective Lab): `crates/color/src/convert.rs`.
- Brush behaviour: `crates/paint/src/lib.rs`, `render.rs`, `dynamics.rs`,
  `presets.rs`; generated tips/textures: `procedural.rs`.

Translate the design and algorithms into Flint's C#/WinForms architecture,
theme controls and exact byte operations; do not paste Rust/egui UI code.
`desktop/ColourPickerDialog.cs` demonstrates a credited translated layout
and ColourMath; `desktop/BrushEngine.cs` demonstrates the brush port.
Preserve BGRA pixels and immutable shared undo objects. Use GDI+ for interface
art, not stored image edits. Apply format-version rules if persistence changes.

Keep the source copyright note in every translated file header and add/update
its entry in `CREDITS_LICENSES.md`, retaining required licence notices. The
current header pattern is "Copyright (c) 2026 ArtCraft Team and the PhotoCraft
contributors, MIT License. See CREDITS_LICENSES.md." Use the notice belonging
to the revision you actually read. Never use ArtCraft's name, wordmark or logos
as Flint branding; the required legal attribution must still name the owner.

Follow the plan's bounded phases. Phase 1's Lab/CMYK fields remain deferred
in the reviewed code; phase 3 leaves pen pressure open. Do not pull unrelated
brush phases into a colour-picker task. Add conversion/behaviour tests in the
matching `--colourtest` or `--brushtest` suite and apply
`flint-ui-verification`. Report incomplete features honestly.
