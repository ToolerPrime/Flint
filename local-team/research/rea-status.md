# REA installation and first evidence

Verified 2026-11-07. REA 6.8.0 is installed locally in `.tools/rea`.
Run `./rea.ps1 --version` or `./rea.ps1 COMMAND ...` from this workspace.
No user-wide agent configuration, Fireworks installation, or Flint source was changed.
No paid model API was used.

## Provenance and runtime

Official sources: https://github.com/morluto/rea and npm package `rea-agents`.
The npm repository field matches `git+https://github.com/morluto/rea.git`.
Pinned package integrity: `sha512-9QDeRnGqEDzABZyr04eLnBXBv/c6I9/TWu/6M0MD4pEfkySI/BIgwTODIbAReX3C1x6hjw9+Jlx4A2yUSPZ9ww==`.
Installed with `npm install --prefix .tools/rea --ignore-scripts --no-audit --no-fund rea-agents@6.8.0`.
Optional native modules requiring install scripts have not been built; the tested static PE operation does not need them.

The system Node 24.19.0 is older than REA's required 24.22.1.
A separate Node 24.22.1 Windows x64 runtime was downloaded from nodejs.org into `.tools/rea`.
Its archive matched the official SHASUMS256.txt:
`d2046d28c2c3dcd211ac5145c0bf30e2047ef9dfb306693c8c6e2eccd8b95fa5`.
System Node was not replaced.

## Completed analysis

Command: `./rea.ps1 inspect-pe-imports 'C:\Program Files (x86)\Macromedia\Fireworks 8\Fireworks.exe' --json`.
Exit status: 0. Complete result: `research/fireworks-pe-imports.json`.
Provider readiness: `research/rea-providers.json`.

Fireworks.exe is a 32-bit x86 PE, 16,175,104 bytes.
SHA256: `38c9a84fd77b2070d788cc7b47221153a709ed0e875b3696f3d7119f7b69c47f`.
The import table has 25 DLL descriptors, including `jslib.dll`, `python.dll`,
`libpng.dll`, `giflib.dll`, `zlib.dll`, `gdiplus.dll`, and the VC7.1/MFC runtime.
These are static declarations, not proof that any particular import is reached at runtime.
This first pass identifies useful boundaries for scripting, PNG/GIF handling, and rendering.
It does not recover source code or identify Fireworks' internal editable-PNG chunk structures.

PNG-specific projection: `research/fireworks-png-imports.json` retains all 42 PNG
imports and their IAT coordinates from the original evidence. In particular,
`png_set_custom_chunk_read_fn` appears at IAT RVA 10,534,320, alongside standard
read functions and `png_write_chunk` / `png_write_chunk_start` / `png_write_chunk_data`
/ `png_write_chunk_end`. The custom callback registration is a useful candidate
for tracing editable-PNG handling, not proof of what its callback does.

## Native provider installed

Ghidra 12.1.4 and Temurin JDK 21.0.13+9 are now installed under `.tools/rea`.
Use `./rea-native.ps1` for the configured native provider. This sets process-only
environment variables, a 2 GB JVM heap, a 330-second startup limit, and a
30-second per-function decompilation limit. `WindowsResourceReference` is disabled
to bound analysis cost; resource-reference coverage is therefore reduced.

Official archives, verified before extraction:

- https://github.com/NationalSecurityAgency/ghidra/releases/download/Ghidra_12.1.4_build/ghidra_12.1.4_PUBLIC_20260921.zip
  SHA256 `ddac49f903da9d5bac833e5cc79395098b9c33cfd3279be5f31bd00387d2d4db`
- https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.13%2B9/OpenJDK21U-jdk_x64_windows_hotspot_21.0.13_9.zip
  SHA256 `a4dbaa05fca4bd89d84584e58c459bd732efebb1722af80ab13c6516832004f9`

Total archive downloads: 775,070,879 bytes. Runtime settings and temporary analysis
files remain within this workspace. No user-wide Java/Ghidra installation was made.

`./rea-native.ps1 doctor --provider ghidra --json` returned exit 0 and `healthy: true`.
All scoped checks passed, including Java, Ghidra native decompiler, and Windows
process/DACL controls. Full report: `research/rea-ghidra-doctor.json`.
Its broader `environment_healthy: false` concerns unconfigured optional providers
and global agent integrations, not the selected Ghidra provider.

## Integration status

The first native `search_procedures` request for PNG-related procedures timed out
at the configured 330,000 ms startup deadline. The complete error and partial
diagnostics are saved in `research/fireworks-png-procedures.json`; despite the
filename, this is a timeout report, not a successful procedure inventory.
No recovered functions or callback behavior can be claimed from this attempt.
A subsequent process check found no REA/Ghidra worker still running. No second
long analysis attempt was launched. A future attempt needs a deliberately revised
analysis budget/profile; reopening repeats the import from scratch.

CLI use works now without restarting Codex. MCP registration was deliberately not applied to
global agent profiles; a later isolated local-agent profile can point at this pinned runtime and entry script.
The latest CLI and native-provider instructions are at
https://github.com/morluto/rea/blob/main/docs/getting-started/cli.md.
