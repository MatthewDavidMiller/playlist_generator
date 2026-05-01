from __future__ import annotations

import json
from pathlib import Path

import pytest

from playlist_generator import cli
from playlist_generator.audio_normalization import VolumeNormalizationResult
from playlist_generator.core import PlaylistValidationError
from playlist_generator.ffmpeg_setup import FfmpegInstallPlan


def test_cli_main_writes_playlist_and_prints_summary(
    capsys,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    special_file = tmp_path / "station-id.mp3"
    output_path = tmp_path / "playlist.m3u8"

    (source_directory / "song-01.mp3").touch()
    (source_directory / "song-02.flac").touch()
    special_file.touch()

    exit_code = cli.main(
        [
            "--source-directory",
            str(source_directory),
            "--special-file",
            str(special_file),
            "--insert-every",
            "2",
            "--output-path",
            str(output_path),
        ]
    )

    captured = capsys.readouterr()
    summary = json.loads(captured.out.splitlines()[-1])

    assert exit_code == 0
    assert output_path.exists()
    assert "Playlist written to" in captured.out
    assert summary["insert_every"] == 2
    assert summary["source_track_count"] == 2


def test_cli_main_returns_error_code_when_playlist_generation_fails(
    capsys,
    tmp_path: Path,
) -> None:
    missing_source_directory = tmp_path / "missing"
    special_file = tmp_path / "station-id.mp3"
    output_path = tmp_path / "playlist.m3u8"
    special_file.touch()

    exit_code = cli.main(
        [
            "--source-directory",
            str(missing_source_directory),
            "--special-file",
            str(special_file),
            "--insert-every",
            "2",
            "--output-path",
            str(output_path),
        ]
    )

    captured = capsys.readouterr()

    assert exit_code == 1
    assert "does not exist" in captured.err


def test_cli_main_returns_error_code_for_expected_playlist_errors(
    monkeypatch: pytest.MonkeyPatch,
    capsys,
) -> None:
    def raise_validation_error(**_: object) -> None:
        raise PlaylistValidationError("bad input")

    monkeypatch.setattr(cli, "create_vlc_playlist", raise_validation_error)

    exit_code = cli.main(
        [
            "--source-directory",
            "music",
            "--special-file",
            "station-id.mp3",
            "--insert-every",
            "2",
            "--output-path",
            "playlist.m3u8",
        ]
    )

    captured = capsys.readouterr()

    assert exit_code == 1
    assert captured.err.strip() == "bad input"


def test_cli_main_propagates_unexpected_exceptions(
    monkeypatch: pytest.MonkeyPatch,
) -> None:
    def raise_runtime_error(**_: object) -> None:
        raise RuntimeError("boom")

    monkeypatch.setattr(cli, "create_vlc_playlist", raise_runtime_error)

    with pytest.raises(RuntimeError, match="boom"):
        cli.main(
            [
                "--source-directory",
                "music",
                "--special-file",
                "station-id.mp3",
                "--insert-every",
                "2",
                "--output-path",
                "playlist.m3u8",
            ]
        )


def test_cli_normalize_volume_prints_summary(
    monkeypatch: pytest.MonkeyPatch,
    capsys,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    output_directory = tmp_path / "normalized"

    def fake_normalize_audio_directory(
        *,
        source_directory: str,
        output_directory: str,
    ) -> VolumeNormalizationResult:
        return VolumeNormalizationResult(
            source_directory=source_directory,
            output_directory=output_directory,
            normalized_file_count=2,
            skipped_file_count=1,
        )

    monkeypatch.setattr(
        cli,
        "normalize_audio_directory",
        fake_normalize_audio_directory,
    )

    exit_code = cli.main(
        [
            "normalize-volume",
            "--source-directory",
            str(source_directory),
            "--output-directory",
            str(output_directory),
        ]
    )

    captured = capsys.readouterr()
    summary = json.loads(captured.out.splitlines()[-1])

    assert exit_code == 0
    assert "Normalized audio written to" in captured.out
    assert summary["source_directory"] == str(source_directory)
    assert summary["output_directory"] == str(output_directory)
    assert summary["normalized_file_count"] == 2
    assert summary["skipped_file_count"] == 1


def test_cli_normalize_volume_returns_error_code_for_expected_errors(
    monkeypatch: pytest.MonkeyPatch,
    capsys,
) -> None:
    def raise_validation_error(**_: object) -> None:
        raise PlaylistValidationError("ffmpeg missing")

    monkeypatch.setattr(cli, "normalize_audio_directory", raise_validation_error)

    exit_code = cli.main(
        [
            "normalize-volume",
            "--source-directory",
            "music",
            "--output-directory",
            "normalized",
        ]
    )

    captured = capsys.readouterr()

    assert exit_code == 1
    assert captured.err.strip() == "ffmpeg missing"


def test_cli_install_ffmpeg_reports_existing_install(
    monkeypatch: pytest.MonkeyPatch,
    capsys,
) -> None:
    monkeypatch.setattr(
        cli,
        "build_ffmpeg_install_plan",
        lambda: FfmpegInstallPlan(True, [], "already installed"),
    )

    exit_code = cli.main(["install-ffmpeg"])

    captured = capsys.readouterr()

    assert exit_code == 0
    assert "already installed" in captured.out


def test_cli_install_ffmpeg_runs_command_after_confirmation(
    monkeypatch: pytest.MonkeyPatch,
    capsys,
) -> None:
    commands: list[list[str]] = []
    monkeypatch.setattr(
        cli,
        "build_ffmpeg_install_plan",
        lambda: FfmpegInstallPlan(False, ["brew", "install", "ffmpeg"], "install it"),
    )
    monkeypatch.setattr("builtins.input", lambda _: "yes")
    monkeypatch.setattr(
        cli,
        "run_ffmpeg_install",
        lambda command: commands.append(command),
    )

    exit_code = cli.main(["install-ffmpeg"])

    captured = capsys.readouterr()

    assert exit_code == 0
    assert commands == [["brew", "install", "ffmpeg"]]
    assert "FFmpeg installation command completed" in captured.out
