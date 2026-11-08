---
name: flint-icons
description: Use when drawing or wiring a new Flint tool or toolbar icon in ToolIcons and ToolIconsArt; keep original, theme-tinted code art.
---

# Flint icons

Read `desktop/ToolIcons.cs`, `desktop/ToolIconsArt.cs` and the caller in
`desktop/MainForm.Ui.cs` (paths relative to repo root).

Draw original, one-colour line art on the 24x24 design grid with a 2-unit
margin. `ToolIcons.Get` scales by `size / 24f`, uses a 2-unit pen with round
caps/joins and a same-colour brush. Account for stroke width at the edges.
Simple solid shapes are appropriate where existing icons use them (Play,
Stop). No copied/traced Adobe, Macromedia or third-party artwork or logos.

1. Add a unique descriptive key to `ToolIcons.Keys` in the matching group.
2. Add a `case` in `ToolIconsArt.TryDraw`, draw with supplied `p` and `b`,
   and return true. For example the existing `rect` case is:
   `g.DrawRectangle(p, 4f, 6f, 16f, 12f); return true;`.
3. Dispose temporary cloned pens/paths, not the supplied pen or brush.
   `select-rect` clones the pen to set a dash pattern without affecting others.
4. Set `IconButton.IconKey` or add the toolbar item to `_tsIcons` so
   `RefreshToolStripIcons` regenerates its theme-coloured image.
5. Give the action a tooltip and AccessibleName; an icon alone is not a label.

`ToolIcons.Get(key, size, colour)` caches shared bitmaps: callers must not
dispose them. Unknown keys render a dot, so a non-null bitmap is not proof
that registration worked. Check both Keys and TryDraw, then inspect the real
icon at 18/20px and scaled DPI in dark/light, active, hover and disabled states.
GDI+ antialiasing is for these UI images only, never stored document pixels.
