"""Shared playlist generation logic."""

from __future__ import annotations

import os
import random
from collections.abc import Iterable, Sequence
from dataclasses import dataclass
from pathlib import Path

SUPPORTED_EXTENSIONS = (
    ".mp3",
    ".flac",
    ".wav",
    ".m4a",
    ".aac",
    ".ogg",
    ".wma",
)


@dataclass(frozen=True)
class PlaylistResult:
    source_directory: str
    special_file: str
    output_path: str
    source_track_count: int
    playlist_entry_count: int
    insert_every: int


def normalize_path(path: os.PathLike[str] | str, require_exists: bool = False) -> str:
    candidate = Path(path).expanduser()
    if require_exists:
        return str(candidate.resolve(strict=True))
    return str(candidate.resolve(strict=False))


def same_path(left: os.PathLike[str] | str, right: os.PathLike[str] | str) -> bool:
    return os.path.normcase(normalize_path(left)) == os.path.normcase(
        normalize_path(right)
    )


def get_audio_files(
    source_directory: os.PathLike[str] | str,
    extensions: Sequence[str] = SUPPORTED_EXTENSIONS,
) -> list[str]:
    source_path = Path(source_directory).expanduser()
    if not source_path.is_dir():
        raise FileNotFoundError(
            f"Source directory '{source_directory}' does not exist."
        )
    source_path = source_path.resolve(strict=True)

    extension_set = {extension.lower() for extension in extensions}
    files = [
        str(path.resolve(strict=True))
        for path in sorted(source_path.rglob("*"), key=lambda item: str(item).lower())
        if path.is_file() and path.suffix.lower() in extension_set
    ]
    return files


def shuffle_tracks(
    tracks: Iterable[os.PathLike[str] | str] | None,
    rng: random.Random | None = None,
) -> list[str]:
    if tracks is None:
        return []

    shuffled = [normalize_path(track, require_exists=True) for track in tracks]
    (rng or random.Random()).shuffle(shuffled)
    return shuffled


def build_interval_playlist_entries(
    tracks: Sequence[os.PathLike[str] | str] | None,
    special_file: os.PathLike[str] | str,
    insert_every: int,
) -> list[str]:
    if insert_every < 1:
        raise ValueError("InsertEvery must be at least 1.")

    if tracks is None:
        return []

    normalized_special_file = normalize_path(special_file, require_exists=True)
    playlist_entries: list[str] = []

    for index, track in enumerate(tracks, start=1):
        playlist_entries.append(normalize_path(track, require_exists=True))
        if index % insert_every == 0:
            playlist_entries.append(normalized_special_file)

    return playlist_entries


def write_m3u8_playlist(
    tracks: Sequence[os.PathLike[str] | str],
    output_path: os.PathLike[str] | str,
) -> str:
    if not str(output_path).strip():
        raise ValueError("Output path is required.")

    output = Path(normalize_path(output_path))
    output.parent.mkdir(parents=True, exist_ok=True)
    lines = [
        "#EXTM3U",
        *(normalize_path(track, require_exists=True) for track in tracks),
    ]
    output.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return str(output)


def create_vlc_playlist(
    source_directory: os.PathLike[str] | str,
    special_file: os.PathLike[str] | str,
    insert_every: int,
    output_path: os.PathLike[str] | str,
    rng: random.Random | None = None,
) -> PlaylistResult:
    source_path = Path(source_directory).expanduser()
    special_path = Path(special_file).expanduser()

    if not source_path.is_dir():
        raise FileNotFoundError(
            f"Source directory '{source_directory}' does not exist."
        )
    source_path = source_path.resolve(strict=True)

    if not special_path.is_file():
        raise FileNotFoundError(f"Special file '{special_file}' does not exist.")
    special_path = special_path.resolve(strict=True)

    all_tracks = get_audio_files(source_path)
    source_tracks = [
        track for track in all_tracks if not same_path(track, special_path)
    ]

    if not source_tracks:
        raise ValueError(
            "No supported audio files were found in "
            f"'{source_path}' after excluding the special file."
        )

    shuffled_tracks = shuffle_tracks(source_tracks, rng=rng)
    playlist_entries = build_interval_playlist_entries(
        shuffled_tracks,
        special_path,
        insert_every,
    )
    written_playlist = write_m3u8_playlist(playlist_entries, output_path)

    return PlaylistResult(
        source_directory=str(source_path),
        special_file=str(special_path),
        output_path=written_playlist,
        source_track_count=len(source_tracks),
        playlist_entry_count=len(playlist_entries),
        insert_every=insert_every,
    )
