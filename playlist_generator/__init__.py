"""Cross-platform VLC playlist generator."""

from .core import (
    SUPPORTED_EXTENSIONS,
    PlaylistResult,
    build_interval_playlist_entries,
    create_vlc_playlist,
    get_audio_files,
    shuffle_tracks,
    write_m3u8_playlist,
)

__all__ = [
    "SUPPORTED_EXTENSIONS",
    "PlaylistResult",
    "build_interval_playlist_entries",
    "create_vlc_playlist",
    "get_audio_files",
    "shuffle_tracks",
    "write_m3u8_playlist",
]
