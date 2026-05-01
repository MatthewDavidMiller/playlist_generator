from __future__ import annotations

import subprocess
from pathlib import Path

import pytest

import playlist_generator.audio_normalization as normalization
from playlist_generator.audio_normalization import (
    LOUDNORM_FILTER,
    build_ffmpeg_normalize_command,
    normalize_audio_directory,
)
from playlist_generator.core import PlaylistIOError, PlaylistValidationError


def test_build_ffmpeg_normalize_command_uses_loudnorm_filter(tmp_path: Path) -> None:
    input_path = tmp_path / "input.mp3"
    output_path = tmp_path / "output.mp3"

    command = build_ffmpeg_normalize_command("ffmpeg", input_path, output_path)

    assert command == [
        "ffmpeg",
        "-y",
        "-i",
        str(input_path),
        "-af",
        LOUDNORM_FILTER,
        "-map_metadata",
        "0",
        "-vn",
        str(output_path),
    ]


def test_normalize_audio_directory_preserves_relative_output_paths(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    nested_directory = source_directory / "album"
    output_directory = tmp_path / "normalized"
    nested_directory.mkdir(parents=True)
    (source_directory / "song-01.mp3").touch()
    (nested_directory / "song-02.flac").touch()
    (nested_directory / "cover.jpg").touch()
    commands: list[list[str]] = []

    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        commands.append(command)
        Path(command[-1]).write_bytes(b"normalized")
        return subprocess.CompletedProcess(command, 0, "", "")

    monkeypatch.setattr(normalization.subprocess, "run", fake_run)

    result = normalize_audio_directory(
        source_directory,
        output_directory,
        ffmpeg_executable="ffmpeg",
    )

    assert result.normalized_file_count == 2
    assert result.skipped_file_count == 0
    assert (output_directory / "song-01.mp3").read_bytes() == b"normalized"
    assert (output_directory / "album" / "song-02.flac").read_bytes() == b"normalized"
    assert len(commands) == 2


def test_normalize_audio_directory_requires_ffmpeg(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()
    monkeypatch.setattr(normalization, "find_ffmpeg", lambda: None)

    with pytest.raises(PlaylistValidationError, match="FFmpeg is required"):
        normalize_audio_directory(source_directory, tmp_path / "normalized")


def test_normalize_audio_directory_reports_ffmpeg_failures(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()

    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(command, 1, "", "bad audio")

    monkeypatch.setattr(normalization.subprocess, "run", fake_run)

    with pytest.raises(PlaylistIOError, match="bad audio"):
        normalize_audio_directory(
            source_directory,
            tmp_path / "normalized",
            ffmpeg_executable="ffmpeg",
        )


def test_normalize_audio_directory_skips_same_path_outputs(tmp_path: Path) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()

    result = normalize_audio_directory(
        source_directory,
        source_directory,
        ffmpeg_executable="ffmpeg",
    )

    assert result.normalized_file_count == 0
    assert result.skipped_file_count == 1


def test_normalize_audio_directory_skips_existing_output_tree_files(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    output_directory = source_directory / "normalized"
    output_directory.mkdir(parents=True)
    (source_directory / "song-01.mp3").touch()
    (output_directory / "song-02.mp3").touch()

    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        Path(command[-1]).write_bytes(b"normalized")
        return subprocess.CompletedProcess(command, 0, "", "")

    monkeypatch.setattr(normalization.subprocess, "run", fake_run)

    result = normalize_audio_directory(
        source_directory,
        output_directory,
        ffmpeg_executable="ffmpeg",
    )

    assert result.normalized_file_count == 1
    assert result.skipped_file_count == 1
