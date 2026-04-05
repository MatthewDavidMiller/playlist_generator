"""Command-line entrypoint for the playlist generator."""

from __future__ import annotations

import argparse
import json
import sys

from .core import create_vlc_playlist


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


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)

    try:
        result = create_vlc_playlist(
            source_directory=args.source_directory,
            special_file=args.special_file,
            insert_every=args.insert_every,
            output_path=args.output_path,
        )
    except Exception as error:
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
