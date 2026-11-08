---
name: fireworks-parity
description: Use when implementing or checking a Flint feature intended to match Fireworks 8 behaviour, names, menus or keyboard shortcuts.
---

# Fireworks 8 parity

Read `FEATURE_PARITY.md`, `KNOWN_LIMITATIONS.md` and the relevant Flint source
from the repo root. `desktop/MainForm.Shortcuts.cs`, `MainForm.Ui.cs` and
`MainForm.Workspace.cs` contain shortcut, tool and panel wiring.

Locate the installed Fireworks 8 through the local app/installation inventory;
do not hard-code a machine path. Open it and try the feature first on a
disposable document when live interaction is safe and allowed. Record the
starting state, exact steps, result, keyboard behaviour and undo/redo. Do not
overwrite Tyler's files or dismiss an unsaved-work prompt. If interaction is
unavailable, say "not observed"; source/static evidence is not a live test.

Under that installation, read `English/Keyboard Shortcuts/*.xml`, specifically
the **Macromedia Standard** set Tyler uses. Record the matching command and
modifier keys. XML establishes declared bindings, not runtime behaviour.
Check repeated tool-letter cycling and text-entry focus in the actual app.

Concrete Flint examples: BuildWindowMenu maps Layers to F2, Properties to
Ctrl+F3, Timeline to Shift+F2 and hide/show panels to F4. BuildToolbox labels
Pointer as V/0 and Move as V twice. Verify these against installed Fireworks
before changing them, rather than assuming Photoshop bindings.

Compare the same steps in Flint, including cancel, undo/redo, persistence and
edge cases relevant to the feature. Report observed match, deliberate
difference and unverified behaviour separately. Existing workspace tests
exercise programmatic docking; they do not prove Fireworks drag parity.
Update parity/limitations documentation only within the assigned task scope.

Implement the behaviour independently. Never copy Adobe/Macromedia artwork,
icons, logos, assets or proprietary implementation into Flint. Observing a
feature or reading shortcut declarations does not grant redistribution rights.
