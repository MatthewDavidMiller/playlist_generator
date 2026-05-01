"""Command-line entrypoint for the playlist generator."""

from __future__ import annotations

import argparse
import json
import subprocess
import sys

from .audio_normalization import normalize_audio_directory
from .core import PlaylistGeneratorError, create_vlc_playlist
from .ffmpeg_setup import (
    build_ffmpeg_install_plan,
    format_command,
    run_ffmpeg_install,
)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Generate a shuffled VLC-compatible playlist and insert a special track "
            "after every configured number of songs."
        )
    )
    parser.add_argument(
        "--source-directory",
        required=True,
        help="Directory to scan recursively for source tracks.",
    )
    parser.add_argument(
        "--special-file",
        required=True,
        help="Audio file to insert after each full block of songs.",
    )
    parser.add_argument(
        "--insert-every",
        required=True,
        type=int,
        help="Number of songs between special track insertions.",
    )
    parser.add_argument(
        "--output-path",
        required=True,
        help="Destination path for the generated .m3u8 playlist.",
    )
    return parser


def build_normalize_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="playlist_generator normalize-volume",
        description=(
            "Normalize supported audio files in a directory as Opus 160k .opus files."
        ),
    )
    parser.add_argument(
        "--source-directory",
        required=True,
        help="Directory to scan recursively for audio files.",
    )
    parser.add_argument(
        "--output-directory",
        required=True,
        help="Directory where normalized audio files will be written.",
    )
    return parser


def build_install_ffmpeg_parser() -> argparse.ArgumentParser:
    return argparse.ArgumentParser(
        prog="playlist_generator install-ffmpeg",
        description="Show and optionally run a guided FFmpeg install command.",
    )


def main_normalize_volume(argv: list[str] | None = None) -> int:
    parser = build_normalize_parser()
    args = parser.parse_args(argv)

    try:
        result = normalize_audio_directory(
            source_directory=args.source_directory,
            output_directory=args.output_directory,
        )
    except PlaylistGeneratorError as error:
        print(str(error), file=sys.stderr)
        return 1

    print(f"Normalized audio written to {result.output_directory}")
    print(
        json.dumps(
            {
                "source_directory": result.source_directory,
                "output_directory": result.output_directory,
                "normalized_file_count": result.normalized_file_count,
                "skipped_file_count": result.skipped_file_count,
            }
        )
    )
    return 0


def main_install_ffmpeg(argv: list[str] | None = None) -> int:
    parser = build_install_ffmpeg_parser()
    parser.parse_args(argv)

    plan = build_ffmpeg_install_plan()
    print(plan.message)
    if plan.is_installed:
        return 0
    if not plan.command:
        return 1

    print(f"Command: {format_command(plan.command)}")
    response = input("Run this command now? [y/N] ").strip().lower()
    if response not in {"y", "yes"}:
        print("FFmpeg installation was not started.")
        return 1

    try:
        run_ffmpeg_install(plan.command)
    except subprocess.CalledProcessError as error:
        print(f"FFmpeg installation failed with exit code {error.returncode}.")
        return error.returncode or 1

    print("FFmpeg installation command completed.")
    return 0


def main(argv: list[str] | None = None) -> int:
    if argv is None:
        argv = sys.argv[1:]
    if argv[:1] == ["normalize-volume"]:
        return main_normalize_volume(argv[1:])
    if argv[:1] == ["install-ffmpeg"]:
        return main_install_ffmpeg(argv[1:])

    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        result = create_vlc_playlist(
            source_directory=args.source_directory,
            special_file=args.special_file,
            insert_every=args.insert_every,
            output_path=args.output_path,
        )
    except PlaylistGeneratorError as error:
        print(str(error), file=sys.stderr)
        return 1

    print(f"Playlist written to {result.output_path}")
    print(
        json.dumps(
            {
                "source_directory": result.source_directory,
                "special_file": result.special_file,
                "output_path": result.output_path,
                "source_track_count": result.source_track_count,
                "playlist_entry_count": result.playlist_entry_count,
                "insert_every": result.insert_every,
            }
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
