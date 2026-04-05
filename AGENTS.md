# AGENTS

Use the repository documentation as the primary reference before making changes.

## Read Order

1. [README.md](README.md) for product behavior, supported usage, and runtime
   requirements.
2. [docs/maintainer-guide.md](docs/maintainer-guide.md) for maintainer workflow
   and documentation boundaries.
3. [docs/testing.md](docs/testing.md) for validation commands and testing scope.
4. [pyproject.toml](pyproject.toml) and
   [.pre-commit-config.yaml](.pre-commit-config.yaml) as the source of truth for
   package metadata, lint rules, test settings, and hooks.

## Working Rules

- Do not duplicate documentation. Update the existing source section and add a
  cross-reference where needed.
- Keep `README.md` user-facing. Put maintainer-only workflow in `docs/`.
- When behavior, commands, or tooling change, update the relevant documentation
  in the same change.
- Validate Python changes with the commands documented in
  [docs/testing.md](docs/testing.md#validation-commands).
