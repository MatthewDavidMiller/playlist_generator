"""Shared playlist generation logic."""

from __future__ import annotations

import os
import random
import tempfile
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
    ".opus",
    ".wma",
)


class PlaylistGeneratorError(Exception):
    """Base class for expected playlist generation failures."""


class PlaylistValidationError(PlaylistGeneratorError):
    """Raised when user-supplied input is invalid."""


class PlaylistIOError(PlaylistGeneratorError):
    """Raised when the filesystem prevents successful generation."""


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


def is_supported_audio_file(
    path: os.PathLike[str] | str,
    extensions: Sequence[str] = SUPPORTED_EXTENSIONS,
) -> bool:
    return Path(path).suffix.lower() in {extension.lower() for extension in extensions}


def get_audio_files(
    source_directory: os.PathLike[str] | str,
    extensions: Sequence[str] = SUPPORTED_EXTENSIONS,
) -> list[str]:
    source_path = Path(source_directory).expanduser()
    if not source_path.is_dir():
        raise PlaylistValidationError(
            f"Source directory '{source_directory}' does not exist."
        )
    source_path = source_path.resolve(strict=True)

    extension_set = {extension.lower() for extension in extensions}
    try:
        files = [
            str(path.resolve(strict=True))
            for path in sorted(
                source_path.rglob("*"),
                key=lambda item: str(item).lower(),
            )
            if path.is_file() and path.suffix.lower() in extension_set
        ]
    except OSError as error:
        raise PlaylistIOError(
            f"Unable to scan '{source_path}' for audio files: {error}."
        ) from error
    return files


def shuffle_tracks(
    tracks: Iterable[os.PathLike[str] | str] | None,
    rng: random.Random | None = None,
) -> list[str]:
    if tracks is None:
        return []

    try:
        shuffled = [normalize_path(track, require_exists=True) for track in tracks]
    except FileNotFoundError as error:
        raise PlaylistIOError(
            "A source track became unavailable while the playlist was being built."
        ) from error
    (rng or random.Random()).shuffle(shuffled)
    return shuffled


def build_interval_playlist_entries(
    tracks: Sequence[os.PathLike[str] | str] | None,
    special_file: os.PathLike[str] | str,
    insert_every: int,
) -> list[str]:
    if insert_every < 1:
        raise PlaylistValidationError("InsertEvery must be at least 1.")

    if tracks is None:
        return []

    try:
        normalized_special_file = normalize_path(special_file, require_exists=True)
    except FileNotFoundError as error:
        raise PlaylistIOError(
            "The special file became unavailable while the playlist was being built."
        ) from error
    playlist_entries: list[str] = []

    try:
        for index, track in enumerate(tracks, start=1):
            playlist_entries.append(normalize_path(track, require_exists=True))
            if index % insert_every == 0:
                playlist_entries.append(normalized_special_file)
    except FileNotFoundError as error:
        raise PlaylistIOError(
            "A source track became unavailable while the playlist was being built."
        ) from error

    return playlist_entries


def write_m3u8_playlist(
    tracks: Sequence[os.PathLike[str] | str],
    output_path: os.PathLike[str] | str,
) -> str:
    if not str(output_path).strip():
        raise PlaylistValidationError("Output path is required.")

    output = Path(normalize_path(output_path))
    temp_output: Path | None = None

    try:
        output.parent.mkdir(parents=True, exist_ok=True)
        lines = [
            "#EXTM3U",
            *(normalize_path(track, require_exists=True) for track in tracks),
        ]

        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            dir=output.parent,
            prefix=f".{output.name}.",
            suffix=".tmp",
            delete=False,
        ) as handle:
            handle.write("\n".join(lines) + "\n")
            temp_output = Path(handle.name)

        os.replace(temp_output, output)
    except FileNotFoundError as error:
        raise PlaylistIOError(
            "A playlist entry became unavailable while the playlist was being written."
        ) from error
    except OSError as error:
        raise PlaylistIOError(
            f"Unable to write playlist to '{output}': {error}."
        ) from error
    finally:
        if temp_output is not None:
            temp_output.unlink(missing_ok=True)

    return str(output)


def create_vlc_playlist(
    source_directory: os.PathLike[str] | str,
    special_file: os.PathLike[str] | str,
    insert_every: int,
    output_path: os.PathLike[str] | str,
    rng: random.Random | None = None,
) -> PlaylistResult:
    if insert_every < 1:
        raise PlaylistValidationError("InsertEvery must be at least 1.")

    source_path = Path(source_directory).expanduser()
    special_path = Path(special_file).expanduser()

    if not source_path.is_dir():
        raise PlaylistValidationError(
            f"Source directory '{source_directory}' does not exist."
        )
    source_path = source_path.resolve(strict=True)

    if not special_path.is_file():
        raise PlaylistValidationError(f"Special file '{special_file}' does not exist.")
    if not is_supported_audio_file(special_path):
        raise PlaylistValidationError(
            f"Special file '{special_file}' must use a supported audio extension."
        )
    special_path = special_path.resolve(strict=True)

    all_tracks = get_audio_files(source_path)
    source_tracks = [
        track for track in all_tracks if not same_path(track, special_path)
    ]

    if not source_tracks:
        raise PlaylistValidationError(
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
