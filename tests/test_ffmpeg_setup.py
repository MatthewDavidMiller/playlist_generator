from __future__ import annotations

import platform
import shutil

import playlist_generator.ffmpeg_setup as ffmpeg_setup


def test_build_ffmpeg_install_plan_reports_existing_install(
    monkeypatch,
) -> None:
    monkeypatch.setattr(ffmpeg_setup, "is_ffmpeg_installed", lambda: True)

    plan = ffmpeg_setup.build_ffmpeg_install_plan()

    assert plan.is_installed
    assert plan.command == []


def test_build_ffmpeg_install_plan_uses_winget_on_windows(monkeypatch) -> None:
    monkeypatch.setattr(ffmpeg_setup, "is_ffmpeg_installed", lambda: False)
    monkeypatch.setattr(platform, "system", lambda: "Windows")
    monkeypatch.setattr(
        shutil,
        "which",
        lambda name: "/bin/winget" if name == "winget" else None,
    )

    plan = ffmpeg_setup.build_ffmpeg_install_plan()

    assert plan.command == ["winget", "install", "Gyan.FFmpeg"]


def test_build_ffmpeg_install_plan_uses_brew_on_macos(monkeypatch) -> None:
    monkeypatch.setattr(ffmpeg_setup, "is_ffmpeg_installed", lambda: False)
    monkeypatch.setattr(platform, "system", lambda: "Darwin")
    monkeypatch.setattr(
        shutil,
        "which",
        lambda name: "/bin/brew" if name == "brew" else None,
    )

    plan = ffmpeg_setup.build_ffmpeg_install_plan()

    assert plan.command == ["brew", "install", "ffmpeg"]


def test_build_ffmpeg_install_plan_uses_apt_on_linux(monkeypatch) -> None:
    monkeypatch.setattr(ffmpeg_setup, "is_ffmpeg_installed", lambda: False)
    monkeypatch.setattr(platform, "system", lambda: "Linux")
    monkeypatch.setattr(
        shutil,
        "which",
        lambda name: "/bin/apt" if name == "apt" else None,
    )

    plan = ffmpeg_setup.build_ffmpeg_install_plan()

    assert plan.command == ["sudo", "apt", "install", "ffmpeg"]


def test_build_ffmpeg_install_plan_handles_unknown_platform(monkeypatch) -> None:
    monkeypatch.setattr(ffmpeg_setup, "is_ffmpeg_installed", lambda: False)
    monkeypatch.setattr(platform, "system", lambda: "FreeBSD")

    plan = ffmpeg_setup.build_ffmpeg_install_plan()

    assert not plan.is_installed
    assert plan.command == []
