# Flint

A native Windows image editor and frame animator inspired by Fireworks 8.
Built with C#/.NET 8 and WinForms. This is an independent project, with no
affiliation with Adobe or Macromedia.

Flint includes editable rectangles, ellipses and text; multiple object
selection and nested grouping; layers and bitmap masks; pixel snapping;
movable workspace panels; animation timing; export preview; and GIF, APNG,
WebP, PNG and other image exports. Video export requires a separate ffmpeg.
Fireworks PNG import has partial editable support; see FEATURE_PARITY.md.

## Build and run

Requires Windows and the .NET 8 SDK.

```powershell
dotnet build desktop -v q --nologo
dotnet run --project desktop
```

Self-contained Windows x64 executable:

```powershell
dotnet publish desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist-desktop
```

## Tests

The full suite additionally uses Python, Node.js and ffmpeg; some Fireworks
import checks use optional locally installed reference samples.

```powershell
cd test
powershell -ExecutionPolicy Bypass -File ./run_all.ps1
```

Success ends with ALL SUITES PASSED. Focused editor checks can be run using
the executable's --productivitytest, --editabletest and --workspacetest flags.

## Keyboard shortcuts

Flint uses Fireworks 8's default shortcuts ("Macromedia Standard").

| Action | Shortcut |
|---|---|
| Pointer / Move layer | V or 0 (press again for Move) |
| Scale (free transform) | Q, Ctrl+T |
| Marquee / oval marquee | M (press again to switch) |
| Lasso, magic wand, crop | L, W, C |
| Brush, pencil, eraser, rubber stamp | B, Y, E, S |
| Line, rectangle / oval, text | N, U (press again for oval), T |
| Paint bucket / gradient | G (press again to switch) |
| Eyedropper, hand | I, H |
| Default colours / swap colours | D / X |
| Import / export / export preview | Ctrl+R / Ctrl+Shift+R / Ctrl+Shift+X |
| Undo / redo | Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z |
| Clone / duplicate objects | Ctrl+Shift+D / Ctrl+Alt+D |
| Clone and nudge | Alt+arrow (Shift: 10 pixels) |
| Group / ungroup / flatten selection | Ctrl+G / Ctrl+Shift+G / Ctrl+Alt+Shift+Z |
| Numeric transform | Ctrl+Shift+T |
| Rotate 90° clockwise / anticlockwise | Ctrl+Shift+9 / Ctrl+Shift+7 |
| Align left, centre, right, top, middle, bottom | Ctrl+Alt+1 to 6 |
| Bring forward / send backward | Ctrl+Up / Ctrl+Down |
| Bring to front / send to back | Ctrl+Shift+Up / Ctrl+Shift+Down |
| Paste attributes | Ctrl+Alt+Shift+V |
| Fit canvas / trim canvas | Ctrl+Alt+F / Ctrl+Alt+T |
| Merge down | Ctrl+E |
| Zoom 100/200/300/400/800/1600%, fit | Ctrl+1/2/3/4/8/6, Ctrl+0 |
| Rulers, show / lock / snap guides | Ctrl+Alt+R, Ctrl+; / Ctrl+Alt+; / Ctrl+Shift+; |
| Previous / next frame, play | Page Up / Page Down, Ctrl+Alt+P |
| Image size and quality (Flint) | Ctrl+Alt+I |
| Show / hide Layers, Timeline, Tools, Properties, History, Library | F2, Shift+F2, Ctrl+F2, Ctrl+F3, Shift+F10, F11 |
| Hide / show all panels | F4 |

Not available yet, because Flint does not have these features: pen, knife,
blur, slice and hotspot tools; text style keys; panel keys (F2 and so on);
distribute; Paste Inside (Ctrl+Shift+V pastes frames).

See [the full shortcut and behavior notes](local-team/research/productivity-integration.md).
Fit retains off-canvas sources; explicit clipping removes them and rasterizes
partial intersections. All frames share one canvas. Project format 10 stores
group membership and retains compatibility with older files.

## Project notes

- [Architecture](ARCHITECTURE.md), [feature parity](FEATURE_PARITY.md),
  [limitations](KNOWN_LIMITATIONS.md), and [changelog](CHANGELOG.md).
- [Local workers](local-team/README.md) provide bounded Ollama jobs and REA
  research notes. Existing interactive AI launchers are in scripts/.
- src/ and build.mjs retain the earlier browser prototype.
- [Credits and third-party terms](CREDITS_LICENSES.md). The quantizer's
  redistribution notice is preserved in its source. No overall project license
  has been selected; this snapshot does not invent one.

This repository starts with a source snapshot. Earlier local history is kept
on the development workstation because it contains machine-local agent sessions.
Downloaded research assets, credentials, session logs and build output are excluded.
