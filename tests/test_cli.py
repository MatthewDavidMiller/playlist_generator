from __future__ import annotations

import json
from pathlib import Path

from playlist_generator import cli


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
