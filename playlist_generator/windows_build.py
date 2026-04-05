"""Helpers for packaging the application as a Windows executable."""

from __future__ import annotations

import argparse
import subprocess
import sys
from dataclasses import dataclass
from pathlib import Path

APP_NAME = "VLCPlaylistGenerator"
GUI_LAUNCHER = "scripts/windows_gui_launcher.py"
CLI_LAUNCHER = "scripts/windows_cli_launcher.py"


@dataclass(frozen=True)
class BuildTarget:
    """Configuration for a PyInstaller build target."""

    name: str
    launcher: str
    windowed: bool


GUI_TARGET = BuildTarget(
    name=APP_NAME,
    launcher=GUI_LAUNCHER,
    windowed=True,
)

CLI_TARGET = BuildTarget(
    name=f"{APP_NAME}CLI",
    launcher=CLI_LAUNCHER,
    windowed=False,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Build Windows executables for the VLC Playlist Generator."
    )
    parser.add_argument(
        "--target",
        choices=("gui", "cli", "both"),
        default="gui",
        help="Which executable target to build.",
    )
    parser.add_argument(
        "--onedir",
        action="store_true",
        help="Build a one-directory bundle instead of a single-file executable.",
    )
    parser.add_argument(
        "--clean",
        action="store_true",
        help="Ask PyInstaller to clean its cache before building.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the resolved PyInstaller commands without running them.",
    )
    return parser


def get_repo_root() -> Path:
    return Path(__file__).resolve().parent.parent


def get_targets(selection: str) -> list[BuildTarget]:
    if selection == "gui":
        return [GUI_TARGET]
    if selection == "cli":
        return [CLI_TARGET]
    return [GUI_TARGET, CLI_TARGET]


def build_pyinstaller_command(
    target: BuildTarget,
    *,
    repo_root: Path,
    onefile: bool = True,
    clean: bool = False,
) -> list[str]:
    build_root = repo_root / "build" / "pyinstaller"
    command = [
        sys.executable,
        "-m",
        "PyInstaller",
        "--noconfirm",
        "--name",
        target.name,
        "--distpath",
        str(repo_root / "dist"),
        "--workpath",
        str(build_root / "work"),
        "--specpath",
        str(build_root / "spec"),
    ]

    command.append("--onefile" if onefile else "--onedir")
    command.append("--windowed" if target.windowed else "--console")

    if clean:
        command.append("--clean")

    command.append(str(repo_root / target.launcher))
    return command


def format_command(command: list[str]) -> str:
    return subprocess.list2cmdline(command)


def run_build(
    selection: str,
    *,
    repo_root: Path | None = None,
    onefile: bool = True,
    clean: bool = False,
    dry_run: bool = False,
) -> int:
    resolved_repo_root = repo_root or get_repo_root()
    targets = get_targets(selection)

    for target in targets:
        command = build_pyinstaller_command(
            target,
            repo_root=resolved_repo_root,
            onefile=onefile,
            clean=clean,
        )
        print(format_command(command))
        if dry_run:
            continue

        completed = subprocess.run(command, cwd=resolved_repo_root, check=False)
        if completed.returncode != 0:
            return completed.returncode

    return 0


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return run_build(
        args.target,
        onefile=not args.onedir,
        clean=args.clean,
        dry_run=args.dry_run,
    )


if __name__ == "__main__":
    raise SystemExit(main())
