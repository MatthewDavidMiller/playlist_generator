"""Cross-platform VLC playlist generator."""

from .core import (
    SUPPORTED_EXTENSIONS,
    PlaylistGeneratorError,
    PlaylistIOError,
    PlaylistResult,
    PlaylistValidationError,
    build_interval_playlist_entries,
    create_vlc_playlist,
    get_audio_files,
    is_supported_audio_file,
    shuffle_tracks,
    write_m3u8_playlist,
)

__all__ = [
    "PlaylistGeneratorError",
    "PlaylistIOError",
    "SUPPORTED_EXTENSIONS",
    "PlaylistResult",
    "PlaylistValidationError",
    "build_interval_playlist_entries",
    "create_vlc_playlist",
    "get_audio_files",
    "is_supported_audio_file",
    "shuffle_tracks",
    "write_m3u8_playlist",
]
