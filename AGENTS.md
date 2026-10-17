## Build and test

`dotnet build NetSwitch.sln -c Release`, `dotnet test NetSwitch.sln`. Requires .NET SDK 8+. Build output goes to `artifacts/`, the finished tool to `dist/NetSwitch.exe`. Releases are built by GitHub Actions from a `vX.Y.Z` tag. See `docs/build.md`.

## Agent skills

### Issue tracker

Issues and specs live in GitHub Issues of `AlexeyDesyatnik/NetSwitch` (via the `gh` CLI). See `docs/agents/issue-tracker.md`.

### Domain docs

Single-context: one `GLOSSARY.md` at the repo root and ADRs in `docs/adr/`. See `docs/agents/domain.md`.
