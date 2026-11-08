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
dotnet publish desktop -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o dist-desktop
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

## Editing shortcuts

| Action | Shortcut |
|---|---|
| Object selection | Q; Shift-click adds, Alt-click picks one group member |
| Group / ungroup | Ctrl+G / Ctrl+Alt+G or Ctrl+U |
| Flatten selected objects | Ctrl+Alt+Shift+Z |
| Free / numeric transform | Ctrl+T / Ctrl+Alt+T |
| Fit canvas to whole animation | Ctrl+Shift+F |
| Trim empty canvas | Ctrl+Shift+T |
| Image size and quality | Ctrl+Alt+I |
| Selected objects 50% / 200% | Ctrl+Alt+5 / Ctrl+Alt+2 |

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
