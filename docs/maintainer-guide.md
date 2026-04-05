# Maintainer Guide

User-facing behavior, feature scope, and runtime usage are documented in [README.md](../README.md). This guide only covers local maintenance workflow for the Python codebase.

## Local Setup

Create or activate a Python 3.9+ environment, then install the project in
editable mode with development dependencies:

```bash
python -m pip install -e ".[dev]"
```

Tkinter is required for the desktop GUI. Platform-specific install notes live
in [README.md](../README.md#requirements).

Install the Windows packaging dependency only when you need to produce a
standalone `.exe`:

```bash
python -m pip install -e ".[windows-build]"
```

## Validation Workflow

Install the repository hook once per clone:

```bash
python -m pre_commit install
```

Detailed validation commands, hook behavior, and test scope are documented in
[docs/testing.md](testing.md). Tool configuration is centralized in
[pyproject.toml](../pyproject.toml).

## Windows Release Build

The supported Windows packaging path uses
[`playlist_generator.windows_build`](../playlist_generator/windows_build.py)
with checked-in launcher scripts under [`scripts/`](../scripts/). Build the GUI
executable with:

```bash
python -m playlist_generator.windows_build
```

Useful variants:

- `python -m playlist_generator.windows_build --target both` builds GUI and
  CLI executables.
- `python -m playlist_generator.windows_build --onedir` creates unpacked
  output instead of a single-file executable.
- `python -m playlist_generator.windows_build --dry-run` prints the resolved
  PyInstaller command without running it.

The command writes output to `dist/` and uses `build/pyinstaller/` for work and
spec files. Those generated directories are ignored by git.

For repository automation, [`.github/workflows/windows-exe.yml`](../.github/workflows/windows-exe.yml)
uses a read-only Windows build job to install the pinned dependency set from
[`requirements/windows-ci-lock.txt`](../requirements/windows-ci-lock.txt), run
`pip-audit`, execute the tests, build both executables, and upload the
resulting `.exe` files as a GitHub Actions artifact. A separate tag-only job
with `contents: write` rebuilds the executables from the same pinned inputs and
publishes them to GitHub Releases. Use the Actions artifact for branch
validation and the Release asset for a versioned public download.

## Code Scope

All active application code lives in `playlist_generator/`. Packaging launcher
scripts live in `scripts/`. Tests live in `tests/`. Removed PowerShell assets
are out of scope and should not be reintroduced.

## Documentation Rules

- Keep `README.md` focused on what the application does and how to run it.
- Keep maintainer workflow in this document.
- Update [AGENTS.md](../AGENTS.md) only when repository-specific automation
  guidance changes.
- Cross-reference existing docs instead of copying the same commands into
  multiple files.
