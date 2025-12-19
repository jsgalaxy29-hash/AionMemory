# Continuous Integration Notes

## Standard commands
Use the same sequence locally and in CI:

1. `dotnet restore AionMemory.slnx`
2. `dotnet build AionMemory.slnx -c Release`
3. `dotnet test AionMemory.slnx -c Release`

The CI workflow runs these commands with `--no-restore`/`--no-build` flags after the first step to keep the pipeline fast and deterministic.

## Platforms and workloads
- .NET 10 preview is required (`10.0.100-preview.5` in CI).
- The solution includes a MAUI AppHost; building and testing on Windows installs the `maui` workload in CI to satisfy that target.
- Linux/macOS runners can execute headless projects (Domain/Infrastructure/AI) if the MAUI projects are excluded, but the official CI pipeline runs on Windows to cover the full solution.

## Quality gates
- Nullable is enabled in all projects.
- CI turns on `TreatWarningsAsErrors` for Domain, Infrastructure, AI, and AI provider projects via `Directory.Build.props` to block warning regressions.

## Tests
- Test projects live under `tests/` and rely only on in-memory SQLite; there are no machine-specific paths.
- The CI workflow publishes `.trx` results as artifacts for diagnostics.

## Build artifacts
- MAUI publish targets are platform-specific. To produce a Windows build locally: `dotnet publish src/Aion.AppHost/Aion.AppHost.csproj -f net10.0-windows10.0.19041.0 -c Release`.
- Android/iOS/macOS builds require the corresponding SDK workloads and platforms; they are not produced automatically in CI.
