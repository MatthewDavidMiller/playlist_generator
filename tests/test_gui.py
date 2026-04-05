from __future__ import annotations

import threading
from pathlib import Path

from playlist_generator.core import PlaylistResult, PlaylistValidationError
from playlist_generator.gui import (
    BackgroundGenerationRunner,
    PlaylistGenerationRequest,
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
