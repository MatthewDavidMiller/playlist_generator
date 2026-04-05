# Testing Guide

This document is the testing and validation reference for the repository. For
application behavior and runtime usage, see [README.md](../README.md). For
overall maintainer workflow, see [docs/maintainer-guide.md](maintainer-guide.md).

## Tooling Source Of Truth

Validation behavior is defined in two files:

- [pyproject.toml](../pyproject.toml) for `pytest` and `ruff` configuration.
- [.pre-commit-config.yaml](../.pre-commit-config.yaml) for the commit-time
  hook pipeline.

When commands or rules change, update those files first and then update this
document to match.

## Environment

Use Python 3.9 or newer. Install the project and development dependencies in
the active environment:

```bash
python3 -m pip install -e ".[dev]"
```

If you need to build Windows executables from the repository, also install the
optional packaging dependency:

```bash
python3 -m pip install -e ".[windows-build]"
```

The local hook configuration currently invokes tools through `.venv/bin/python`.
If you use a different environment layout, either activate `.venv` or update
[.pre-commit-config.yaml](../.pre-commit-config.yaml) so hook execution matches
your local setup.

## Validation Commands

Run the checks in this order when you want the clearest failure signals:

```bash
python3 -m ruff check .
python3 -m ruff format --check .
python3 -m pytest
python3 -m pre_commit run --all-files
```

For the Windows packaging helper, use:

```bash
python3 -m playlist_generator.windows_build --dry-run
```

## What Each Command Covers

### `ruff check .`

Runs lint rules for Python correctness and maintainability. The current rule
selection includes:

- `E` and `F` for pycodestyle and pyflakes issues.
- `I` for import sorting.
- `B` for common bugbear findings.
- `UP` for safe Python upgrade suggestions.

If you want lint auto-fixes before rerunning verification, use:

```bash
python3 -m ruff check . --fix
```

### `ruff format --check .`

Verifies formatting without rewriting files. Use `python3 -m ruff format .` to
apply formatting changes locally.

### `pytest`

Runs the automated test suite under `tests/` using the repository configuration
from [pyproject.toml](../pyproject.toml). The suite currently covers:

- Shared playlist generation behavior in `tests/test_playlist_generator.py`.
- CLI behavior and failure handling in `tests/test_cli.py`.
- Windows packaging command construction in `tests/test_windows_build.py`.

Coverage reporting is enabled by default through the pytest configuration.

### `pre_commit run --all-files`

Executes the same pipeline enforced by the git hook across the full repository.
Use this before a commit when you want to confirm hook behavior explicitly.

### `playlist_generator.windows_build --dry-run`

Prints the resolved PyInstaller command line without invoking the packager. This
is the repository-safe validation step for packaging logic on any platform. The
actual executable produced by PyInstaller is platform-specific, so a Windows
`.exe` must still be built on Windows.

## CI Coverage

GitHub Actions workflow
[`.github/workflows/windows-exe.yml`](../.github/workflows/windows-exe.yml)
provides the Windows-side validation that cannot be completed from Linux or
macOS alone. It installs the optional build dependency, runs `pytest` on
`windows-latest`, builds both executables, and uploads `dist/*.exe` as an
artifact. For tag pushes matching `v*`, the same workflow also attaches the
generated `.exe` files to the corresponding GitHub Release.

## Git Hook Behavior

Install the hook once per clone:

```bash
python3 -m pre_commit install
```

After installation, each `git commit` runs the local hooks defined in
[.pre-commit-config.yaml](../.pre-commit-config.yaml):

1. `ruff-check`
2. `ruff-format`
3. `pytest`

If the hook fails, the commit is blocked until the failure is fixed and the
commit is retried.

## Change Expectations

- Any Python behavior change should include or update automated tests.
- Documentation-only changes do not usually require new tests, but the commands
  in this guide should remain accurate.
- If GUI behavior changes, keep the underlying logic covered in automated tests
  whenever possible rather than relying only on manual Tkinter checks.
