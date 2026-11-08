# Workspace integration, 2026-11-08

Implemented in C:/Users/Techn/Flint (existing .NET 8 WinForms editor).

- Removed the sprite-sheet slicing section from Properties. Slicing remains an internal import/test operation; existing sprite tests still exercise it.
- Added Canvas background > Colour / Transparent. This manages a locked, shared bottom layer using ordinary pixels and existing structural undo/persistence. Secondary paint colour remains a separate right-mouse brush colour. Recolouring does not replace artwork or flatten it. Opaque artwork still covers a bottom background.
- View > Snap to pixels, on by default and remembered. Move/transform translation rounds to whole pixels; straight, non-aspect-locked resize handles snap to pixel edges. Turning it off permits fractional transform positions. Rotated/aspect-locked resizing keeps its geometry. Guide snapping can take priority when moving.
- Tools, Inspector (its existing tabs and Properties together), and Timeline have movable headers, floating owned windows, panel menus, resize edges, and View > Workspace > Reset workspace. Tools/Inspector dock left or right; Timeline docks at bottom. Closing a floating panel docks it back. Positions are session-only; individual Inspector tabs are not independently dockable yet.

## Local agents

Ollama Qwen wrote PixelSnap.cs (accepted unchanged after reading and tests).
The local regression-suggestion worker supplied useful negative-coordinate and
control-lifetime cases, but its suggestion that reset should restore a floating
state was rejected: reset restores the default docked layout.
The panel review worker exhausted 800 output tokens and repeatedly contradicted
itself. Its result is rejected, not an approval. Coordinator review and real
regression tests are the acceptance evidence. No paid worker fallback was used.

## REA evidence

Ran pinned REA 6.8.0 inspect-pe-resources against Fireworks.exe and English/Resources/Fireworks Resources.dll.
Complete evidence is in fireworks-resources.json and fireworks-english-resources.json.
The English resource DLL contains 3,262 nonempty string-table entries. The bounded
workspace projection, fireworks-workspace-strings.json, records resource IDs for
docking, undocking, workspace layouts and panel-group commands with the source
evidence ID and target hash. These are observed resource declarations, not recovered
native handlers. The prior native-analysis timeout remains unresolved; it was not
rerun. Flint's new panel implementation is original managed code.

## ArtCraft assessment

Inspected https://github.com/storytold/photocraft and the docking/snapping modules:

- crates/ui-egui/src/panels/panel_docking.rs
- crates/engine/src/snap.rs

PhotoCraft uses Rust/egui and craft-ui docking models. Direct reuse would require
a cross-language UI integration or replacing Flint's UI. For this increment,
keeping WinForms controls and adding small native hosts is simpler. No ArtCraft
source was copied into Flint. Downloaded research reference modules retain their
upstream licence files under research/artcraft.

## Validation

New --workspacetest covers background render/recolour/undo/save/reload/transparency,
preserving artwork, floating ownership and content lifetime, closing to dock,
reset restoring exact default bounds, coordinate ties/negative values/nonfinite
rejection, real transform mouse-handler translation and resizing with snapping
on/off, and half-pixel canvas-centre guide precedence.

Live GUI check: opened the updated debug executable, floated Tools via its title,
closed it back to the dock, chose red through Canvas background's native picker,
observed a red canvas and shared background layer, then pressed Ctrl+Z and
observed transparency and swatch restoration. Only the scratch test document was
discarded. The original running editor/document was left open.

The first fast header-drag check exposed global Cursor.Position reads racing
ahead of queued mouse messages. Panel title and resize handlers now use each
event's local coordinates converted to screen coordinates. Live retest dragged
Tools into a floating window at the drop point, then dragged it back to the left
edge and observed its original controls restored. Temporary pointer diagnostics
were removed from the application before the final build.

Full suite output: workspace-suite.log. Preview build is separate from the running
dist-desktop executable. No commit or push was made.
