# Fireworks productivity commands — 2026-11-08

Implemented in `C:/Users/Techn/Flint/desktop`. Standalone build:
`build/productivity-preview/Flint.exe`.

## Verified shortcut evidence

The installed Fireworks 8 `English/Keyboard Shortcuts/Fireworks.xml` and
`Macromedia Standard.xml` were read directly. Command names, bindings and SHA-256
digests are in `fireworks-shortcut-evidence.json`. Modifiers 4/5/6/7 mean
Ctrl / Ctrl+Shift / Ctrl+Alt / Ctrl+Alt+Shift.

| Action | Shortcut | Origin |
|---|---|---|
| Group selected objects | Ctrl+G | Both Fireworks profiles |
| Ungroup one level | Ctrl+Alt+G; Ctrl+U | Standard; legacy profile |
| Flatten selected objects | Ctrl+Alt+Shift+Z | Both profiles |
| Numeric transform | Ctrl+Alt+T | Both profiles |
| Free transform | Ctrl+T | Both profiles, already present |
| Fit canvas to whole animation | Ctrl+Shift+F | Standard Fit Canvas binding |
| Trim empty canvas across frames | Ctrl+Shift+T | Standard Trim Canvas binding |
| Fit canvas to current frame | Ctrl+Alt+Shift+F | New Flint extension |
| Cut outside canvas, current frame | Ctrl+Alt+Shift+X | New Flint extension |
| Cut outside canvas, whole animation | Ctrl+Alt+Shift+K | New Flint extension |
| Image size and sampling quality | Ctrl+Alt+I | New Flint extension |
| Scale selected objects 50% / 200% | Ctrl+Alt+5 / Ctrl+Alt+2 | New Flint extensions |

Ctrl+Shift+G is an additional ungroup alias, not attributed to Fireworks.
Q picks objects; Shift-click adds or removes selections; Alt-click picks an
individual group member. Ctrl+A in Q selects all objects in the active cel.
Grouping can nest and is saved in project format 10. Older project formats load.

Installed help pages `01_bas49.htm` and `01_bas50.htm`, extracted privately
under ignored `research/runtime/fireworks-help`, distinguish fitting/trimming
canvas from deleting off-canvas objects. This batch uses those observable
contracts; it does not claim successful native decompilation by REA.

## Behavior and limits

- Fit expands or contracts around visible source content, including off-canvas
  pixels. It shifts all frames together and retains source data. Trim uses only
  rendered content already inside the canvas.
- An animation has one shared canvas. Current-frame fit therefore changes the
  whole document's dimensions; other frames retain off-canvas source data.
  Whole-animation fit includes all frames for the common export canvas.
- Cut outside deletes off-canvas data. Partly clipped objects become bitmaps;
  undo restores them. Current-frame clipping separates a changed shared cel
  so other frames retain their content; large separation over 256 MB is refused.
- Image size resizes every frame, preserves frame delays and retained editable
  sources, and offers Nearest, RotSprite and Smooth. Quick selected-object
  scaling uses the tool's Resample setting. Whole-animation 50%/200% actions
  are also available in Image. Size limit: 16,384 per side and 16,777,216 pixels.
- Groups are logical nested memberships within one layer/cel. There is no
  isolated group blend/opacity container or cross-layer grouping. Flatten
  requires adjacent selected objects with Normal blending to preserve appearance.
- Fit uses cached object alpha without subtracting layer masks; masked objects
  can leave extra fitted space. Arbitrary vector/node editing and distribution
  remain separate work.

## Local work and verification

The bounded local Ollama worker proposed ResizePlan. The first response had an
invalid numeric conversion and was rejected. A second proposal was reviewed,
integrated and exercised by invalid-size and half-pixel rounding checks. Raw
runs are under `runs/resize-plan-*` and `runs/resize-plan-fix-*`. The runner's
`needs_review` status is generation status, not acceptance. No paid helper or
Claude API was used for this increment.

`--productivitytest`: all 24 checks pass in Debug and the packaged executable.
The complete regression run ends `ALL SUITES PASSED`; see
`productivity-suite.log`. An initially intermittent snapping test passed in
isolation, then was made independent of physical keyboard modifiers by setting
its synthetic modifier state explicitly. Assertions remain intact.

Live window checks: created two retained rectangles, Q/Ctrl+A/Ctrl+G grouped
them, dragging one moved both, Ctrl+Alt+G ungrouped, Ctrl+Alt+Shift+Z produced
one `Flattened selection · Bitmap` list entry, Ctrl+Shift+F fitted to 44×38,
and Ctrl+Alt+5 scaled the selection. Ctrl+Alt+I opened the resize dialog and
its sampling dropdown showed all three choices. Actual sampling changes and
multi-frame clipping were verified by automated code tests, not a live export.
