from __future__ import annotations

import os
import random
import tempfile
import unittest
from pathlib import Path

import pytest

import playlist_generator.core as core
from playlist_generator.core import (
    PlaylistIOError,
    PlaylistValidationError,
    build_interval_playlist_entries,
    create_vlc_playlist,
    get_audio_files,
    write_m3u8_playlist,
)


class BuildIntervalPlaylistEntriesTests(unittest.TestCase):
    def test_inserts_special_track_after_every_full_block(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            special_file = base / "station-id.mp3"
            special_file.touch()
            tracks = [
                base / "song-01.mp3",
                base / "song-02.mp3",
                base / "song-03.mp3",
                base / "song-04.mp3",
            ]
            for track in tracks:
                track.touch()

            actual = build_interval_playlist_entries(tracks, special_file, 2)
            expected = [
                str(tracks[0].resolve()),
                str(tracks[1].resolve()),
                str(special_file.resolve()),
                str(tracks[2].resolve()),
                str(tracks[3].resolve()),
                str(special_file.resolve()),
            ]

            self.assertEqual(actual, expected)

    def test_does_not_append_special_track_after_partial_block(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            special_file = base / "station-id.mp3"
            special_file.touch()
            tracks = [base / "song-01.mp3", base / "song-02.mp3", base / "song-03.mp3"]
            for track in tracks:
                track.touch()

            actual = build_interval_playlist_entries(tracks, special_file, 2)
            expected = [
                str(tracks[0].resolve()),
                str(tracks[1].resolve()),
                str(special_file.resolve()),
                str(tracks[2].resolve()),
            ]

            self.assertEqual(actual, expected)


class GetAudioFilesTests(unittest.TestCase):
    def test_discovers_supported_audio_files_recursively(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            source_directory = Path(temp_dir) / "library"
            nested_directory = source_directory / "disc1"
            nested_directory.mkdir(parents=True)
            (source_directory / "song1.mp3").touch()
            (nested_directory / "song2.flac").touch()
            (nested_directory / "cover.jpg").touch()

            files = get_audio_files(source_directory)

            self.assertEqual(len(files), 2)
            self.assertEqual(len([path for path in files if path.endswith(".mp3")]), 1)
            self.assertEqual(len([path for path in files if path.endswith(".flac")]), 1)


class CreateVlcPlaylistTests(unittest.TestCase):
    def test_writes_playlist_and_excludes_special_file_from_shuffle_pool(self) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            source_directory = base / "music"
            nested_directory = source_directory / "ids"
            output_directory = base / "output"
            nested_directory.mkdir(parents=True)
            output_directory.mkdir()

            song_one = source_directory / "song1.mp3"
            song_two = source_directory / "song2.ogg"
            special_file = nested_directory / "station-id.mp3"
            output_path = output_directory / "playlist.m3u8"

            song_one.touch()
            song_two.touch()
            special_file.touch()
            (source_directory / "notes.txt").touch()

            result = create_vlc_playlist(
                source_directory=source_directory,
                special_file=special_file,
                insert_every=2,
                output_path=output_path,
                rng=random.Random(0),
            )
            playlist_lines = output_path.read_text(encoding="utf-8").splitlines()

            self.assertEqual(playlist_lines[0], "#EXTM3U")
            self.assertEqual(playlist_lines.count(str(special_file.resolve())), 1)
            self.assertEqual(playlist_lines.count(str(song_one.resolve())), 1)
            self.assertEqual(playlist_lines.count(str(song_two.resolve())), 1)
            self.assertEqual(result.source_track_count, 2)
            self.assertEqual(result.playlist_entry_count, 3)

    def test_raises_when_no_supported_files_remain_after_excluding_special_file(
        self,
    ) -> None:
        with tempfile.TemporaryDirectory() as temp_dir:
            base = Path(temp_dir)
            source_directory = base / "music"
            source_directory.mkdir()
            special_file = source_directory / "station-id.mp3"
            special_file.touch()
            output_path = base / "playlist.m3u8"

            with self.assertRaisesRegex(
                PlaylistValidationError, "No supported audio files were found"
            ):
                create_vlc_playlist(
                    source_directory=source_directory,
                    special_file=special_file,
                    insert_every=3,
                    output_path=output_path,
                )


def test_create_vlc_playlist_rejects_unsupported_special_file_extension(
    tmp_path: Path,
) -> None:
    source_directory = tmp_path / "music"
    source_directory.mkdir()
    (source_directory / "song-01.mp3").touch()

    special_file = tmp_path / "station-id.txt"
    special_file.touch()
    output_path = tmp_path / "playlist.m3u8"

    with pytest.raises(PlaylistValidationError, match="supported audio extension"):
        create_vlc_playlist(
            source_directory=source_directory,
            special_file=special_file,
            insert_every=2,
            output_path=output_path,
        )


def test_write_m3u8_playlist_keeps_existing_output_when_replace_fails(
    monkeypatch: pytest.MonkeyPatch,
    tmp_path: Path,
) -> None:
    track = tmp_path / "song-01.mp3"
    output_path = tmp_path / "playlist.m3u8"
    track.touch()
    output_path.write_text("existing output\n", encoding="utf-8")

    temp_paths: list[Path] = []

    def fail_replace(
        source: os.PathLike[str] | str, destination: os.PathLike[str] | str
    ) -> None:
        temp_paths.append(Path(source))
        raise OSError("simulated replace failure")

    monkeypatch.setattr(core.os, "replace", fail_replace)

    with pytest.raises(PlaylistIOError, match="Unable to write playlist"):
        write_m3u8_playlist([track], output_path)

    assert output_path.read_text(encoding="utf-8") == "existing output\n"
    assert temp_paths
    assert all(not temp_path.exists() for temp_path in temp_paths)


if __name__ == "__main__":
    unittest.main()
