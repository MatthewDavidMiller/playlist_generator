from __future__ import annotations

from pathlib import Path

from playlist_generator.windows_build import (
    APP_NAME,
    CLI_TARGET,
    GUI_TARGET,
    build_pyinstaller_command,
    get_targets,
    run_build,
)


def test_get_targets_returns_expected_build_targets() -> None:
    assert get_targets("gui") == [GUI_TARGET]
    assert get_targets("cli") == [CLI_TARGET]
    assert get_targets("both") == [GUI_TARGET, CLI_TARGET]


def test_build_pyinstaller_command_for_gui_uses_onefile_windowed_launcher() -> None:
    repo_root = Path("/tmp/project")

    command = build_pyinstaller_command(
        GUI_TARGET,
        repo_root=repo_root,
        onefile=True,
        clean=True,
    )

    assert command[:3] == ["python", "-m", "PyInstaller"] or command[1:3] == [
        "-m",
        "PyInstaller",
    ]
    assert "--onefile" in command
    assert "--windowed" in command
    assert "--clean" in command
    assert command[command.index("--name") + 1] == APP_NAME
    assert str(repo_root / "scripts/windows_gui_launcher.py") == command[-1]


def test_build_pyinstaller_command_for_cli_uses_onedir_console_launcher() -> None:
    repo_root = Path("/tmp/project")

    command = build_pyinstaller_command(
        CLI_TARGET,
        repo_root=repo_root,
        onefile=False,
        clean=False,
    )

    assert "--onedir" in command
    assert "--console" in command
    assert "--clean" not in command
    assert str(repo_root / "scripts/windows_cli_launcher.py") == command[-1]


def test_run_build_dry_run_prints_both_commands(capsys, tmp_path: Path) -> None:
    exit_code = run_build(
        "both",
        repo_root=tmp_path,
        dry_run=True,
    )

    captured = capsys.readouterr()

    assert exit_code == 0
    assert "PyInstaller" in captured.out
    assert "windows_gui_launcher.py" in captured.out
    assert "windows_cli_launcher.py" in captured.out
