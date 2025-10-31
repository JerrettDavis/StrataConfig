# Contributing

We welcome contributions! This project is structured for clarity and safety.

## Guidelines

- Keep changes focused and incremental.
- Follow existing naming and structure patterns.
- Add/adjust tests alongside functional changes.
- Avoid introducing unrelated formatting or drive-by refactors.

## Dev Setup

- Install the required .NET SDK (target: `net10.0`).
- Build the solution: `dotnet build`.
- Run tests: `dotnet test StrataConfig.Tests`.
- E2E: install browsers and run (see E2E docs).

## PR Checklist

- [ ] Unit/API tests passing
- [ ] E2E passing locally (or explicitly skipped)
- [ ] Docs updated if user-facing behavior changed
- [ ] No hard-coded absolute paths to static assets

