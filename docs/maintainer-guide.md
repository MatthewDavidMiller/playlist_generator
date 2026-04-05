# Maintainer Guide

User-facing behavior, feature scope, and runtime usage are documented in [README.md](../README.md). This guide only covers local maintenance workflow for the Python codebase.

## Local Setup

Create or activate a Python 3.9+ environment, then install the project in
editable mode with development dependencies:

```bash
python3 -m pip install -e ".[dev]"
```

Tkinter is required for the desktop GUI. Platform-specific install notes live
in [README.md](../README.md#requirements).

## Validation Workflow

Install the repository hook once per clone:

```bash
python3 -m pre_commit install
```

Detailed validation commands, hook behavior, and test scope are documented in
[docs/testing.md](testing.md). Tool configuration is centralized in
[pyproject.toml](../pyproject.toml).

## Code Scope

All active application code lives in `playlist_generator/`. Tests live in
`tests/`. Removed PowerShell assets are out of scope and should not be
reintroduced.

## Documentation Rules

- Keep `README.md` focused on what the application does and how to run it.
- Keep maintainer workflow in this document.
- Update [AGENTS.md](../AGENTS.md) only when repository-specific automation
  guidance changes.
- Cross-reference existing docs instead of copying the same commands into
  multiple files.
