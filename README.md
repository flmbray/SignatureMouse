# SignatureMouse

SignatureMouse converts a signature image into a vector path and replays it with the mouse. It is designed to make online signature fields usable without hand-drawing every time.

## Features
- **Analyze** a signature image into an SVG path (pen-up/pen-down preserved).
- **Replay** the signature using mouse input.
- **Windows-only** interactive placement:
  - `--draw-rect`: select a target rectangle to fit the signature.
  - `--plop`: drag and scale an overlay of the signature before replay.

## Requirements
- .NET 8 SDK
- Windows for replay features (analyze works cross-platform)

## Build
```bash
dotnet build SignatureMouse.csproj -c Release
```

## Usage
### Analyze
```bash
dotnet run -- analyze -i /path/to/signature.jpg -o signature.svg
```

Useful options:
- `--threshold <0-255>`: override automatic thresholding.
- `--close-radius <pixels>`: morphological closing before thinning.
- `--smooth-iterations <count>`: smooth the vector path.
- `--rotate -90|90|180`: rotate the image before processing.
- `--save-cleaned <file>`: save the thinned skeleton preview.

### Replay (Windows)
```powershell
dotnet run -- replay -i signature.svg
```

Placement options:
- `--draw-rect`: select a target rectangle; signature fits inside with padding.
- `--plop`: drag the overlay and resize with the mouse wheel (Ctrl+wheel for coarse steps).
- `--padding <0-0.45>`: padding ratio used with `--draw-rect`.

When using `--draw-rect` or `--plop` and no delay is specified, delay defaults to `0`.

## Notes & Safety
This tool sends synthetic mouse input. Use it only where you are authorized to sign, and be aware that some apps may block automated input.

## Third-Party Licenses
This project depends on SixLabors.ImageSharp. The package is split-licensed; for open-source usage it is granted under the Apache License 2.0 per the Six Labors Split License. See the package license for details:
```
https://www.nuget.org/packages/SixLabors.ImageSharp
```

## License
MIT. See `LICENSE`.
