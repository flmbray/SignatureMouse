# Contributing

Thanks for your interest in contributing!

## Development
- Requires the .NET 8 SDK.
- Build:
  ```bash
  dotnet build SignatureMouse.csproj -c Debug
  ```

## Guidelines
- Keep changes focused and well-scoped.
- Prefer small, well-described commits.
- If you add new options, update the help text in `Program.cs`.

## Testing
There is no automated test suite yet. Please verify changes manually:
- Analyze: generate an SVG from a sample image.
- Replay (Windows): replay into a safe target (e.g., a blank drawing canvas).
