# Credits and licences

## Third-party code in Flint

### Colour quantiser — Craig Kolb, 1989

`desktop/Quantizer.cs` is a port of `colorquant.c`, variance-based colour
quantisation, by Craig E. Kolb, Department of Mathematics, Yale University.
Copyright (C) 1989 Craig E. Kolb.

Macromedia shipped this source with Fireworks 8 under
`Configuration/Third Party Source Code/Color Quantization`. Its licence allows
it to be freely copied, modified and redistributed provided the copyright
notice is preserved, and requires that users be told the source is available.
The notice is preserved at the top of `Quantizer.cs`, and that file is the
source of the port.

### NAudio — Mark Heath and contributors

Audio playback, microphone capture and decoding of WAV, MP3 and (through
Windows Media Foundation) AAC, WMA and FLAC. Version 2.2.1, MIT licence.
Bundled inside the self-contained executable.
https://github.com/naudio/NAudio

### Everything else

Written for Flint: the GIF, APNG and animated-WebP container writers, the
compositor, drawing tools, document model, undo, project format, timeline,
mixer, slicer and atlas packer.

## External tools (not bundled)

- **ffmpeg** — used for video, animated WebP, and decoding audio formats
  Windows cannot. Not distributed with Flint. ffmpeg's own licence (LGPL or
  GPL depending on build) applies to ffmpeg.

## Runtime

- **.NET 8** — MIT licence, bundled in the self-contained executable.

## Formats followed, not code copied

- The atlas JSON follows the layout Aseprite writes, so existing engine
  importers can read it. No Aseprite code is used.

## Not used, on purpose

Flint includes no Adobe or Macromedia code, icons, logos, UI artwork or assets
beyond the separately licensed quantiser above. Fireworks 8 was studied only
as a functional reference:

- The pattern and texture files in Fireworks' `Configuration` folder are not
  bundled: their redistribution terms are unclear.
- The Auto Shape `.jsf` scripts were read but not copied.
