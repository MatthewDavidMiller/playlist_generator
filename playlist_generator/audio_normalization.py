"""Audio volume normalization support."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import tempfile
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path

from .core import (
    PlaylistIOError,
    PlaylistValidationError,
    get_audio_files,
    normalize_path,
    same_path,
)

LOUDNORM_TARGET = "I=-16:TP=-1.5:LRA=11"
LOUDNORM_FILTER = f"loudnorm={LOUDNORM_TARGET}"
LOUDNORM_ANALYSIS_FILTER = f"{LOUDNORM_FILTER}:print_format=json"
LOUDNORM_REQUIRED_FIELDS = (
    "input_i",
    "input_tp",
    "input_lra",
    "input_thresh",
    "target_offset",
)
NORMALIZED_AUDIO_SUFFIX = ".opus"
NORMALIZATION_POLL_SECONDS = 0.1


@dataclass(frozen=True)
class VolumeNormalizationResult:
    source_directory: str
    output_directory: str
    normalized_file_count: int
    skipped_file_count: int
    stopped: bool = False


@dataclass(frozen=True)
class VolumeNormalizationProgress:
    total_file_count: int
    completed_file_count: int
    normalized_file_count: int
    skipped_file_count: int
    current_source_path: str
    action: str


ProgressCallback = Callable[[VolumeNormalizationProgress], None]


class VolumeNormalizationControl:
    def __init__(self) -> None:
        self._paused = threading.Event()
        self._stopped = threading.Event()

    @property
    def is_paused(self) -> bool:
        return self._paused.is_set()

    @property
    def is_stopped(self) -> bool:
        return self._stopped.is_set()

    def pause(self) -> None:
        self._paused.set()

    def resume(self) -> None:
        self._paused.clear()

    def stop(self) -> None:
        self._stopped.set()
        self.resume()

    def wait_if_paused(self) -> None:
        while self.is_paused and not self.is_stopped:
            time.sleep(NORMALIZATION_POLL_SECONDS)

    def should_stop(self) -> bool:
        return self.is_stopped


def find_ffmpeg() -> str | None:
    return shutil.which("ffmpeg")


def build_ffmpeg_normalize_command(
    ffmpeg_executable: str,
    input_path: os.PathLike[str] | str,
    output_path: os.PathLike[str] | str,
) -> list[str]:
    return build_ffmpeg_encode_command(
        ffmpeg_executable,
        input_path,
        output_path,
        {
            "input_i": "0",
            "input_tp": "0",
            "input_lra": "0",
            "input_thresh": "0",
            "target_offset": "0",
        },
    )


def build_ffmpeg_loudnorm_analysis_command(
    ffmpeg_executable: str,
    input_path: os.PathLike[str] | str,
) -> list[str]:
    return [
        ffmpeg_executable,
        "-y",
        "-i",
        str(input_path),
        "-af",
        LOUDNORM_ANALYSIS_FILTER,
        "-f",
        "null",
        "-",
    ]


def build_ffmpeg_encode_command(
    ffmpeg_executable: str,
    input_path: os.PathLike[str] | str,
    output_path: os.PathLike[str] | str,
    loudnorm_stats: dict[str, str],
) -> list[str]:
    second_pass_filter = (
        f"{LOUDNORM_FILTER}"
        f":measured_I={loudnorm_stats['input_i']}"
        f":measured_TP={loudnorm_stats['input_tp']}"
        f":measured_LRA={loudnorm_stats['input_lra']}"
        f":measured_thresh={loudnorm_stats['input_thresh']}"
        f":offset={loudnorm_stats['target_offset']}"
        ":linear=true"
    )
    return [
        ffmpeg_executable,
        "-y",
        "-i",
        str(input_path),
        "-af",
        second_pass_filter,
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


def normalized_audio_output_path(path: os.PathLike[str] | str) -> Path:
    return Path(path).with_suffix(NORMALIZED_AUDIO_SUFFIX)


def parse_loudnorm_stats(output: str, source: Path) -> dict[str, str]:
    json_start = output.rfind("{")
    json_end = output.rfind("}")
    if json_start == -1 or json_end == -1 or json_end < json_start:
        raise PlaylistIOError(
            f"FFmpeg did not return loudness analysis JSON for '{source}'."
        )

    try:
        parsed = json.loads(output[json_start : json_end + 1])
    except json.JSONDecodeError as error:
        raise PlaylistIOError(
            f"FFmpeg returned malformed loudness analysis JSON for '{source}'."
        ) from error

    missing_fields = [
        field for field in LOUDNORM_REQUIRED_FIELDS if not str(parsed.get(field, ""))
    ]
    if missing_fields:
        missing = ", ".join(missing_fields)
        raise PlaylistIOError(
            f"FFmpeg loudness analysis for '{source}' is missing: {missing}."
        )

    return {field: str(parsed[field]) for field in LOUDNORM_REQUIRED_FIELDS}


def run_ffmpeg_command(
    command: list[str],
    *,
    error_message: str,
) -> subprocess.CompletedProcess[str]:
    creationflags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
    completed = subprocess.run(
        command,
        check=False,
        capture_output=True,
        text=True,
        creationflags=creationflags,
    )
    if completed.returncode != 0:
        message = completed.stderr.strip() or completed.stdout.strip()
        detail = f": {message}" if message else "."
        raise PlaylistIOError(f"{error_message}{detail}")
    return completed


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
    output = normalized_audio_output_path(normalize_path(output_path))
    temp_output: Path | None = None

    try:
        output.parent.mkdir(parents=True, exist_ok=True)

        analysis_command = build_ffmpeg_loudnorm_analysis_command(
            resolved_ffmpeg,
            source,
        )
        analysis = run_ffmpeg_command(
            analysis_command,
            error_message=f"FFmpeg failed to analyze loudness for '{source}'",
        )
        loudnorm_stats = parse_loudnorm_stats(
            f"{analysis.stdout}\n{analysis.stderr}",
            source,
        )

        with tempfile.NamedTemporaryFile(
            dir=output.parent,
            prefix=f".{output.name}.",
            suffix=NORMALIZED_AUDIO_SUFFIX,
            delete=False,
        ) as handle:
            temp_output = Path(handle.name)

        command = build_ffmpeg_encode_command(
            resolved_ffmpeg,
            source,
            temp_output,
            loudnorm_stats,
        )
        run_ffmpeg_command(
            command,
            error_message=f"FFmpeg failed to encode normalized audio for '{source}'",
        )

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
    progress_callback: ProgressCallback | None = None,
    control: VolumeNormalizationControl | None = None,
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
    completed_count = 0
    stopped = False
    normalization_jobs: list[tuple[Path, Path]] = []
    destinations: dict[str, Path] = {}
    total_count = len(audio_files)

    def report(source_file: Path, action: str) -> None:
        if progress_callback is None:
            return
        progress_callback(
            VolumeNormalizationProgress(
                total_file_count=total_count,
                completed_file_count=completed_count,
                normalized_file_count=normalized_count,
                skipped_file_count=skipped_count,
                current_source_path=str(source_file),
                action=action,
            )
        )

    for audio_file in audio_files:
        source_file = Path(audio_file)
        try:
            source_file.relative_to(output_path)
        except ValueError:
            pass
        else:
            skipped_count += 1
            completed_count += 1
            report(source_file, "skipped")
            continue

        relative_path = source_file.relative_to(source_path)
        destination = normalized_audio_output_path(output_path / relative_path)
        if same_path(source_file, destination):
            skipped_count += 1
            completed_count += 1
            report(source_file, "skipped")
            continue

        if destination.exists():
            skipped_count += 1
            completed_count += 1
            report(source_file, "skipped")
            continue

        destination_key = os.path.normcase(str(destination))
        if destination_key in destinations:
            raise PlaylistValidationError(
                "Multiple source files would write to the same normalized "
                f"output path '{destination}': '{destinations[destination_key]}' "
                f"and '{source_file}'."
            )
        destinations[destination_key] = source_file
        normalization_jobs.append((source_file, destination))

    for source_file, destination in normalization_jobs:
        if control is not None:
            if control.is_paused:
                report(source_file, "paused")
            control.wait_if_paused()
            if control.should_stop():
                stopped = True
                report(source_file, "stopped")
                break

        report(source_file, "normalizing")
        normalize_audio_file(
            source_file,
            destination,
            ffmpeg_executable=resolved_ffmpeg,
        )
        normalized_count += 1
        completed_count += 1
        report(source_file, "completed")

        if control is not None and control.should_stop():
            stopped = True

    return VolumeNormalizationResult(
        source_directory=str(source_path),
        output_directory=str(output_path),
        normalized_file_count=normalized_count,
        skipped_file_count=skipped_count,
        stopped=stopped,
    )
