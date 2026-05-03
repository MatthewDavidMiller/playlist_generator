from __future__ import annotations

import threading
import tkinter as tk
from pathlib import Path
from types import SimpleNamespace

from playlist_generator import gui
from playlist_generator.audio_normalization import (
    VolumeNormalizationControl,
    VolumeNormalizationProgress,
    VolumeNormalizationResult,
)
from playlist_generator.core import PlaylistResult, PlaylistValidationError
from playlist_generator.gui import (
    BackgroundGenerationRunner,
    PlaylistGenerationRequest,
    PlaylistGeneratorApp,
    VolumeNormalizationRequest,
)


def build_request(tmp_path: Path) -> PlaylistGenerationRequest:
    return PlaylistGenerationRequest(
        source_directory=str(tmp_path / "music"),
        special_file=str(tmp_path / "station-id.mp3"),
        output_path=str(tmp_path / "playlist.m3u8"),
        insert_every=2,
    )


def build_result(tmp_path: Path) -> PlaylistResult:
    return PlaylistResult(
        source_directory=str(tmp_path / "music"),
        special_file=str(tmp_path / "station-id.mp3"),
        output_path=str(tmp_path / "playlist.m3u8"),
        source_track_count=2,
        playlist_entry_count=3,
        insert_every=2,
    )


def build_normalization_request(tmp_path: Path) -> VolumeNormalizationRequest:
    return VolumeNormalizationRequest(
        source_directory=str(tmp_path / "music"),
        output_directory=str(tmp_path / "normalized"),
    )


def build_unrendered_app() -> PlaylistGeneratorApp:
    master = tk.Tcl()
    app = object.__new__(PlaylistGeneratorApp)
    app.root = SimpleNamespace(after=lambda _delay, callback: callback())
    app.playlist_source_directory = tk.StringVar(master=master)
    app.normalization_source_directory = tk.StringVar(master=master)
    app.source_directory = app.playlist_source_directory
    app.special_file = tk.StringVar(master=master)
    app.output_path = tk.StringVar(master=master)
    app.normalized_output_directory = tk.StringVar(master=master)
    app.insert_every = tk.IntVar(master=master, value=5)
    app.status_text = tk.StringVar(master=master)
    app.status_widget = None
    app.normalization_progress_text = tk.StringVar(master=master)
    app.normalization_progressbar = None
    app.normalize_button = SimpleNamespace(configure=lambda **kwargs: None)
    app.pause_normalization_button = SimpleNamespace(configure=lambda **kwargs: None)
    app.resume_normalization_button = SimpleNamespace(configure=lambda **kwargs: None)
    app.stop_normalization_button = SimpleNamespace(configure=lambda **kwargs: None)
    app.normalization_control = None
    return app


def test_background_generation_runner_blocks_duplicate_start_while_running(
    tmp_path: Path,
) -> None:
    generator_started = threading.Event()
    allow_finish = threading.Event()

    def generator(request: PlaylistGenerationRequest) -> PlaylistResult:
        generator_started.set()
        assert request == build_request(tmp_path)
        assert allow_finish.wait(timeout=1)
        return build_result(tmp_path)

    runner = BackgroundGenerationRunner(
        generator=generator,
        schedule=lambda callback: callback(),
    )
    success_event = threading.Event()
    success_results: list[PlaylistResult] = []

    assert runner.start(
        build_request(tmp_path),
        on_start=lambda: None,
        on_success=lambda result: (success_results.append(result), success_event.set()),
        on_error=lambda error: None,
    )
    assert generator_started.wait(timeout=1)
    assert runner.is_running
    assert not runner.start(
        build_request(tmp_path),
        on_start=lambda: None,
        on_success=lambda result: None,
        on_error=lambda error: None,
    )

    allow_finish.set()

    assert success_event.wait(timeout=1)
    assert success_results == [build_result(tmp_path)]
    assert not runner.is_running


def test_background_generation_runner_reports_expected_errors_and_resets_state(
    tmp_path: Path,
) -> None:
    failure = PlaylistValidationError("bad input")
    error_event = threading.Event()
    observed_errors: list[Exception] = []

    runner = BackgroundGenerationRunner(
        generator=lambda request: (_ for _ in ()).throw(failure),
        schedule=lambda callback: callback(),
    )

    assert runner.start(
        build_request(tmp_path),
        on_start=lambda: None,
        on_success=lambda result: None,
        on_error=lambda error: (observed_errors.append(error), error_event.set()),
    )

    assert error_event.wait(timeout=1)
    assert observed_errors == [failure]
    assert not runner.is_running


def test_gui_window_size_clamps_to_available_screen() -> None:
    assert PlaylistGeneratorApp._calculate_window_size(
        screen_width=1920,
        screen_height=1080,
    ) == (900, 680)
    assert PlaylistGeneratorApp._calculate_window_size(
        screen_width=800,
        screen_height=600,
    ) == (720, 520)
    assert PlaylistGeneratorApp._calculate_window_size(
        screen_width=420,
        screen_height=320,
    ) == (340, 260)


def test_gui_min_window_size_does_not_exceed_small_screen() -> None:
    assert PlaylistGeneratorApp._calculate_min_window_size(
        screen_width=1920,
        screen_height=1080,
    ) == (560, 420)
    assert PlaylistGeneratorApp._calculate_min_window_size(
        screen_width=420,
        screen_height=320,
    ) == (340, 260)


def test_gui_scroll_units_support_common_platform_events() -> None:
    assert PlaylistGeneratorApp._scroll_units(SimpleNamespace(delta=120)) == -1
    assert PlaylistGeneratorApp._scroll_units(SimpleNamespace(delta=-240)) == 2
    assert PlaylistGeneratorApp._scroll_units(SimpleNamespace(delta=1)) == -1
    assert PlaylistGeneratorApp._scroll_units(SimpleNamespace(num=4, delta=0)) == -3
    assert PlaylistGeneratorApp._scroll_units(SimpleNamespace(num=5, delta=0)) == 3
    assert PlaylistGeneratorApp._scroll_units(SimpleNamespace(delta=0)) == 0


def test_background_normalization_runner_blocks_duplicate_start_while_running(
    tmp_path: Path,
) -> None:
    generator_started = threading.Event()
    allow_finish = threading.Event()

    def generator(request: VolumeNormalizationRequest) -> str:
        generator_started.set()
        assert request == build_normalization_request(tmp_path)
        assert allow_finish.wait(timeout=1)
        return "done"

    runner = BackgroundGenerationRunner(
        generator=generator,
        schedule=lambda callback: callback(),
    )
    success_event = threading.Event()
    success_results: list[str] = []

    assert runner.start(
        build_normalization_request(tmp_path),
        on_start=lambda: None,
        on_success=lambda result: (success_results.append(result), success_event.set()),
        on_error=lambda error: None,
    )
    assert generator_started.wait(timeout=1)
    assert runner.is_running
    assert not runner.start(
        build_normalization_request(tmp_path),
        on_start=lambda: None,
        on_success=lambda result: None,
        on_error=lambda error: None,
    )

    allow_finish.set()

    assert success_event.wait(timeout=1)
    assert success_results == ["done"]
    assert not runner.is_running


def test_gui_builds_playlist_request_from_playlist_source(tmp_path: Path) -> None:
    app = build_unrendered_app()
    app.playlist_source_directory.set(str(tmp_path / "playlist-music"))
    app.normalization_source_directory.set(str(tmp_path / "normalization-music"))
    app.special_file.set(str(tmp_path / "station-id.mp3"))
    app.output_path.set(str(tmp_path / "playlist.m3u8"))
    app.insert_every.set(3)

    assert app.build_generation_request() == PlaylistGenerationRequest(
        source_directory=str(tmp_path / "playlist-music"),
        special_file=str(tmp_path / "station-id.mp3"),
        output_path=str(tmp_path / "playlist.m3u8"),
        insert_every=3,
    )


def test_gui_builds_normalization_request_from_normalization_source(
    tmp_path: Path,
) -> None:
    app = build_unrendered_app()
    app.playlist_source_directory.set(str(tmp_path / "playlist-music"))
    app.normalization_source_directory.set(str(tmp_path / "normalization-music"))
    app.normalized_output_directory.set(str(tmp_path / "normalized"))

    assert app.build_normalization_request() == VolumeNormalizationRequest(
        source_directory=str(tmp_path / "normalization-music"),
        output_directory=str(tmp_path / "normalized"),
    )


def test_gui_playlist_source_suggests_independent_normalization_defaults(
    monkeypatch,
    tmp_path: Path,
) -> None:
    app = build_unrendered_app()
    selected = tmp_path / "music"
    monkeypatch.setattr(gui.filedialog, "askdirectory", lambda **kwargs: str(selected))

    app.choose_playlist_source_directory()

    assert app.playlist_source_directory.get() == str(selected)
    assert app.normalization_source_directory.get() == str(selected)
    assert app.output_path.get() == str(selected / "music-playlist.m3u8")
    assert app.normalized_output_directory.get() == str(tmp_path / "music-normalized")


def test_gui_playlist_source_does_not_replace_existing_normalization_source(
    monkeypatch,
    tmp_path: Path,
) -> None:
    app = build_unrendered_app()
    existing_normalization_source = tmp_path / "normalize-this"
    existing_normalized_output = tmp_path / "already-normalized"
    app.normalization_source_directory.set(str(existing_normalization_source))
    app.normalized_output_directory.set(str(existing_normalized_output))
    selected = tmp_path / "playlist-music"
    monkeypatch.setattr(gui.filedialog, "askdirectory", lambda **kwargs: str(selected))

    app.choose_playlist_source_directory()

    assert app.playlist_source_directory.get() == str(selected)
    assert app.normalization_source_directory.get() == str(
        existing_normalization_source
    )
    assert app.normalized_output_directory.get() == str(existing_normalized_output)


def test_gui_normalization_source_suggests_normalized_output(
    monkeypatch,
    tmp_path: Path,
) -> None:
    app = build_unrendered_app()
    selected = tmp_path / "raw-audio"
    monkeypatch.setattr(gui.filedialog, "askdirectory", lambda **kwargs: str(selected))

    app.choose_normalization_source_directory()

    assert app.normalization_source_directory.get() == str(selected)
    assert app.normalized_output_directory.get() == str(
        tmp_path / "raw-audio-normalized"
    )


def test_gui_pause_resume_stop_control_normalization() -> None:
    app = build_unrendered_app()
    control = VolumeNormalizationControl()
    app.normalization_control = control

    app.pause_normalization()

    assert control.is_paused
    assert "paused" in app.status_text.get()

    app.resume_normalization()

    assert not control.is_paused
    assert "Resuming" in app.status_text.get()

    app.stop_normalization()

    assert control.is_stopped
    assert "Stopping" in app.status_text.get()


def test_gui_normalization_progress_updates_status_and_counts(tmp_path: Path) -> None:
    app = build_unrendered_app()

    app._on_normalization_progress(
        VolumeNormalizationProgress(
            total_file_count=50,
            completed_file_count=12,
            normalized_file_count=10,
            skipped_file_count=2,
            current_source_path=str(tmp_path / "song.mp3"),
            action="completed",
        )
    )

    assert app.normalization_progress_text.get() == "12 / 50 files processed"
    assert "Completed: song.mp3" in app.status_text.get()
    assert "Files normalized: 10" in app.status_text.get()
    assert "Files skipped: 2" in app.status_text.get()


def test_gui_normalization_success_message_includes_stopped_and_counts(
    monkeypatch,
    tmp_path: Path,
) -> None:
    app = build_unrendered_app()
    shown_messages: list[tuple[str, str]] = []
    monkeypatch.setattr(
        gui.messagebox,
        "showinfo",
        lambda title, message, **_: shown_messages.append((title, message)),
    )

    app._on_normalization_succeeded(
        VolumeNormalizationResult(
            source_directory=str(tmp_path / "music"),
            output_directory=str(tmp_path / "normalized"),
            normalized_file_count=3,
            skipped_file_count=4,
            stopped=True,
        )
    )

    assert shown_messages == [("Stopped", app.status_text.get())]
    assert "Normalization stopped" in app.status_text.get()
    assert "Files normalized: 3" in app.status_text.get()
    assert "Files skipped: 4" in app.status_text.get()
