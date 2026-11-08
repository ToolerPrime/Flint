# Full test suite. Run from the test folder.
$ErrorActionPreference = "Stop"
$exe = "..\desktop\bin\Debug\net8.0-windows\Flint.exe"
$out = "out-cs"
New-Item -ItemType Directory -Force $out | Out-Null

Write-Host "--- building ---"
dotnet build ..\desktop -v q --nologo

Write-Host "`n--- workspace panels, canvas background, pixel snapping ---"
& $exe --workspacetest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "workspacetest failed" }

Write-Host "`n--- Fireworks shortcuts, groups and animation-aware canvas editing ---"
& $exe --productivitytest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "productivitytest failed" }

Write-Host "`n--- retained editable shapes, text and individual objects ---"
& $exe --editabletest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "editabletest failed" }

Write-Host "`n--- document model, undo, .flint format ---"
& $exe --doctest "$out\doc" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "doctest failed" }

Write-Host "`n--- playback timing, onion skin ---"
& $exe --animtest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "animtest failed" }

Write-Host "`n--- audio: mixer, decoding, storage, sync ---"
& $exe --audiotest "$out\audio" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "audiotest failed" }

Write-Host "`n--- large document memory, undo integrity ---"
& $exe --memtest "$out\mem" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "memtest failed" }

Write-Host "`n--- pixel fidelity, every alpha level, checked with PIL ---"
& $exe --fidelitytest "$out\fid" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "fidelitytest failed" }

Write-Host "`n--- crash recovery (kills a real child process) ---"
& $exe --crashtest "$out\crash" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "crashtest failed" }

Write-Host "`n--- export formats checked with PIL, cancellation ---"
& $exe --formattest "$out\fmt" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "formattest failed" }

Write-Host "`n--- still formats (JPEG, WebP, BMP, TIFF, PNG-8) and the export preview ---"
& $exe --exporttest "$out\export" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "exporttest failed" }

Write-Host "`n--- rulers, guides, snapping, .flint guides ---"
& $exe --guidestest "$out\guides" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "guidestest failed" }

Write-Host "`n--- sprite slicing, atlas, pivots, hitboxes ---"
& $exe --spritetest "$out\sprite" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "spritetest failed" }
& $exe --edittest "$out\edit" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "edittest failed" }
& $exe --toolstest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "toolstest failed" }

Write-Host "`n--- trace bitmap to vector ---"
& $exe --tracetest "$out\trace" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "tracetest failed" }

Write-Host "`n--- import modes, pixel preservation, palette ---"
& $exe --importtest "$out\import" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "importtest failed" }

Write-Host "`n--- Fireworks PNG import ---"
& $exe --fwtest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "fwtest failed" }

Write-Host "`n--- drawing primitives ---"
& $exe --drawtest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "drawtest failed" }

Write-Host "`n--- canvas wiring ---"
& $exe --canvastest | Out-Host
if ($LASTEXITCODE -ne 0) { throw "canvastest failed" }

Write-Host "`n--- window layout ---"
& $exe --shot "$out\window.png" "assets\walk_sheet.png" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "layout problems" }

Write-Host "`n--- image ops ---"
python make_assets.py
& $exe --opstest "assets\walk_sheet.png" "$out\walk.gif" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "opstest failed" }

Write-Host "`n--- GIF encoder, checked with Pillow ---"
& $exe --selftest $out | Out-Host
python verify_cs.py
if ($LASTEXITCODE -ne 0) { throw "encoder verification failed" }
python verify_ops.py
if ($LASTEXITCODE -ne 0) { throw "GIF verification failed" }

Write-Host "`n--- WebM export (needs ffmpeg) ---"
& $exe --webmtest "assets\walk_sheet.png" "$out\walk.webm" | Out-Host
if ($LASTEXITCODE -ne 0) { throw "webmtest failed" }

Write-Host "`nALL SUITES PASSED" -ForegroundColor Green
