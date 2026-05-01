"""Audio volume normalization support."""

from __future__ import annotations

import os
import shutil
import subprocess
import tempfile
from dataclasses import dataclass
from pathlib import Path

from .core import (
    PlaylistIOError,
    PlaylistValidationError,
    get_audio_files,
    normalize_path,
    same_path,
)

LOUDNORM_FILTER = "loudnorm=I=-16:TP=-1.5:LRA=11"


@dataclass(frozen=True)
class VolumeNormalizationResult:
    source_directory: str
    output_directory: str
    normalized_file_count: int
    skipped_file_count: int


def find_ffmpeg() -> str | None:
    return shutil.which("ffmpeg")


def build_ffmpeg_normalize_command(
    ffmpeg_executable: str,
    input_path: os.PathLike[str] | str,
    output_path: os.PathLike[str] | str,
) -> list[str]:
    return [
        ffmpeg_executable,
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


def normalize_audio_file(
    input_path: os.PathLike[str] | str,
    output_path: os.PathLike[str] | str,
    *,
    ffmpeg_executable: str | None = None,
) -> str:
    resolved_ffmpeg = ffmpeg_executable or find_ffmpeg()
    if resolved_ffmpeg is None:
        raise PlaylistValidationError(
            "FFmpeg is required for volume normalization. Install ffmpeg and "
            "make sure it is available on PATH."
        )

    source = Path(normalize_path(input_path, require_exists=True))
    output = Path(normalize_path(output_path))
    temp_output: Path | None = None

    try:
        output.parent.mkdir(parents=True, exist_ok=True)
        with tempfile.NamedTemporaryFile(
            dir=output.parent,
            prefix=f".{output.name}.",
            suffix=output.suffix or ".tmp",
            delete=False,
        ) as handle:
            temp_output = Path(handle.name)

        command = build_ffmpeg_normalize_command(resolved_ffmpeg, source, temp_output)
        completed = subprocess.run(
            command,
            check=False,
            capture_output=True,
            text=True,
        )
        if completed.returncode != 0:
            message = completed.stderr.strip() or completed.stdout.strip()
            detail = f": {message}" if message else "."
            raise PlaylistIOError(f"FFmpeg failed to normalize '{source}'{detail}")

        os.replace(temp_output, output)
    except FileNotFoundError as error:
        raise PlaylistIOError(
            f"Audio file '{source}' became unavailable while it was being normalized."
        ) from error
    except OSError as error:
        raise PlaylistIOError(
            f"Unable to normalize '{source}' to '{output}': {error}."
        ) from error
    finally:
        if temp_output is not None:
            temp_output.unlink(missing_ok=True)

    return str(output)


def normalize_audio_directory(
    source_directory: os.PathLike[str] | str,
    output_directory: os.PathLike[str] | str,
    *,
    ffmpeg_executable: str | None = None,
) -> VolumeNormalizationResult:
    resolved_ffmpeg = ffmpeg_executable or find_ffmpeg()
    if resolved_ffmpeg is None:
        raise PlaylistValidationError(
            "FFmpeg is required for volume normalization. Install ffmpeg and "
            "make sure it is available on PATH."
        )

    source_path = Path(source_directory).expanduser()
    if not source_path.is_dir():
        raise PlaylistValidationError(
            f"Source directory '{source_directory}' does not exist."
        )
    source_path = source_path.resolve(strict=True)

    if not str(output_directory).strip():
        raise PlaylistValidationError("Output directory is required.")
    output_path = Path(normalize_path(output_directory))

    audio_files = get_audio_files(source_path)
    if not audio_files:
        raise PlaylistValidationError(
            f"No supported audio files were found in '{source_path}'."
        )

    normalized_count = 0
    skipped_count = 0
    for audio_file in audio_files:
        source_file = Path(audio_file)
        try:
            source_file.relative_to(output_path)
        except ValueError:
            pass
        else:
            skipped_count += 1
            continue

        relative_path = source_file.relative_to(source_path)
        destination = output_path / relative_path
        if same_path(source_file, destination):
            skipped_count += 1
            continue

        normalize_audio_file(
            source_file,
            destination,
            ffmpeg_executable=resolved_ffmpeg,
        )
        normalized_count += 1

    return VolumeNormalizationResult(
        source_directory=str(source_path),
        output_directory=str(output_path),
        normalized_file_count=normalized_count,
        skipped_file_count=skipped_count,
    )
