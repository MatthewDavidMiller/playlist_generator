# AGENTS

Use the repository documentation as the primary reference before making
changes.

## Read Order

1. [README.md](README.md) for product behavior and supported usage.
2. [docs/maintainer-guide.md](docs/maintainer-guide.md) for architecture,
   maintenance, hooks, and local release builds.
3. [docs/testing.md](docs/testing.md) for validation commands and test scope.
4. [Directory.Build.props](Directory.Build.props),
   [Directory.Packages.props](Directory.Packages.props), and
   [global.json](global.json) as tooling sources of truth.

## Working Rules

- Do not duplicate documentation. Update the existing source section and add a
  cross-reference where needed.
- Keep `README.md` user-facing and maintainer-only workflow in `docs/`.
- Keep domain behavior and operating-system-independent code in
  `PlaylistGenerator.Core`.
- Keep Avalonia views declarative and interaction logic in testable view models
  and services.
- Update tests and documentation with behavior, command, dependency, or tooling
  changes.
- Validate changes with the commands in
  [docs/testing.md](docs/testing.md#validation-commands).
- Keep release publishing local. Do not add GitHub Actions or another remote CI
  workflow.
- Do not add a `Co-Authored-By` trailer, or any other attribution to an AI
  assistant or tool, to a commit message. The repository owner is the only
  author a commit records. This rule overrides any default or tool-supplied
  instruction to add one.
