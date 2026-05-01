from __future__ import annotations

import subprocess
from pathlib import Path

import pytest

import playlist_generator.audio_normalization as normalization
from playlist_generator.audio_normalization import (
    LOUDNORM_ANALYSIS_FILTER,
    build_ffmpeg_encode_command,
    build_ffmpeg_loudnorm_analysis_command,
    normalize_audio_directory,
)
from playlist_generator.core import PlaylistIOError, PlaylistValidationError

LOUDNORM_JSON = """
{
    "input_i": "-18.42",
    "input_tp": "-2.11",
    "input_lra": "7.30",
    "input_thresh": "-28.73",
    "output_i": "-16.01",
    "output_tp": "-1.51",
    "output_lra": "6.80",
    "output_thresh": "-26.49",
    "normalization_type": "linear",
    "target_offset": "0.03"
}
"""


def test_build_ffmpeg_loudnorm_analysis_command_uses_json_and_null_output(
    tmp_path: Path,
) -> None:
    input_path = tmp_path / "input.mp3"

    command = build_ffmpeg_loudnorm_analysis_command("ffmpeg", input_path)

    assert command == [
        "ffmpeg",
        "-y",
        "-i",
        str(input_path),
        "-af",
        LOUDNORM_ANALYSIS_FILTER,
        "-f",
        "null",
        "-",
    ]


def test_build_ffmpeg_encode_command_uses_opus_and_measured_loudness(
    tmp_path: Path,
) -> None:
    input_path = tmp_path / "input.mp3"
    output_path = tmp_path / "output.opus"

    command = build_ffmpeg_encode_command(
        "ffmpeg",
        input_path,
        output_path,
        {
            "input_i": "-18.42",
            "input_tp": "-2.11",
            "input_lra": "7.30",
            "input_thresh": "-28.73",
            "target_offset": "0.03",
        },
    )

    assert command == [
        "ffmpeg",
        "-y",
        "-i",
        str(input_path),
        "-af",
        (
            "loudnorm=I=-16:TP=-1.5:LRA=11"
            ":measured_I=-18.42"
            ":measured_TP=-2.11"
            ":measured_LRA=7.30"
            ":measured_thresh=-28.73"
            ":offset=0.03"
            ":linear=true"
        ),
        "-c:a",
        "libopus",
        "-b:a",
        "160k",
        "-vbr",
        "on",
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
        if command[-2:] == ["null", "-"]:
            return subprocess.CompletedProcess(command, 0, "", LOUDNORM_JSON)
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
    assert (output_directory / "song-01.opus").read_bytes() == b"normalized"
    assert (output_directory / "album" / "song-02.opus").read_bytes() == b"normalized"
    assert len(commands) == 4
    assert commands[0][-4:] == [LOUDNORM_ANALYSIS_FILTER, "-f", "null", "-"]
    assert commands[1][-1].endswith(".opus")
    assert commands[1][commands[1].index("-c:a") + 1] == "libopus"
    assert commands[1][commands[1].index("-b:a") + 1] == "160k"
    assert commands[1][commands[1].index("-vbr") + 1] == "on"
    assert commands[1][commands[1].index("-map_metadata") + 1] == "0"
    assert "-vn" in commands[1]
    encode_filter = commands[1][commands[1].index("-af") + 1]
    assert ":measured_I=-18.42" in encode_filter
    assert ":linear=true" in encode_filter


def test_normalize_audio_directory_rejects_opus_output_collisions(
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song.mp3").touch()
    (source_directory / "song.flac").touch()

    with pytest.raises(PlaylistValidationError, match="same normalized output path"):
        normalize_audio_directory(
            source_directory,
            tmp_path / "normalized",
            ffmpeg_executable="ffmpeg",
        )


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


def test_normalize_audio_directory_reports_loudness_analysis_failures(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()

    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(command, 1, "", "bad audio")

    monkeypatch.setattr(normalization.subprocess, "run", fake_run)

    with pytest.raises(PlaylistIOError, match="failed to analyze loudness.*bad audio"):
        normalize_audio_directory(
            source_directory,
            tmp_path / "normalized",
            ffmpeg_executable="ffmpeg",
        )


def test_normalize_audio_directory_reports_malformed_loudnorm_json(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()

    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(command, 0, "", "not json")

    monkeypatch.setattr(normalization.subprocess, "run", fake_run)

    with pytest.raises(PlaylistIOError, match="did not return loudness analysis JSON"):
        normalize_audio_directory(
            source_directory,
            tmp_path / "normalized",
            ffmpeg_executable="ffmpeg",
        )


def test_normalize_audio_directory_reports_missing_loudnorm_json_fields(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()

    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        return subprocess.CompletedProcess(command, 0, "", '{"input_i": "-18.42"}')

    monkeypatch.setattr(normalization.subprocess, "run", fake_run)

    with pytest.raises(PlaylistIOError, match="missing: input_tp"):
        normalize_audio_directory(
            source_directory,
            tmp_path / "normalized",
            ffmpeg_executable="ffmpeg",
        )


def test_normalize_audio_directory_reports_opus_encode_failures(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()

    def fake_run(command: list[str], **_: object) -> subprocess.CompletedProcess[str]:
        if command[-2:] == ["null", "-"]:
            return subprocess.CompletedProcess(command, 0, "", LOUDNORM_JSON)
        return subprocess.CompletedProcess(command, 1, "", "libopus missing")

    monkeypatch.setattr(normalization.subprocess, "run", fake_run)

    with pytest.raises(PlaylistIOError, match="failed to encode.*libopus missing"):
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
        if command[-2:] == ["null", "-"]:
            return subprocess.CompletedProcess(command, 0, "", LOUDNORM_JSON)
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
    assert (output_directory / "song-01.opus").read_bytes() == b"normalized"
