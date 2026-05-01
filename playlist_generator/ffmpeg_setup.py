"""Guided FFmpeg installation support."""

from __future__ import annotations

import platform
import shutil
import subprocess
from dataclasses import dataclass


@dataclass(frozen=True)
class FfmpegInstallPlan:
    is_installed: bool
    command: list[str]
    message: str


def is_ffmpeg_installed() -> bool:
    return shutil.which("ffmpeg") is not None


def build_ffmpeg_install_plan() -> FfmpegInstallPlan:
    if is_ffmpeg_installed():
        return FfmpegInstallPlan(
            is_installed=True,
            command=[],
            message="FFmpeg is already installed and available on PATH.",
        )

    system = platform.system().lower()
    if system == "windows" and shutil.which("winget"):
        return FfmpegInstallPlan(
            is_installed=False,
            command=["winget", "install", "Gyan.FFmpeg"],
            message="Install FFmpeg with winget.",
        )
    if system == "darwin" and shutil.which("brew"):
        return FfmpegInstallPlan(
            is_installed=False,
            command=["brew", "install", "ffmpeg"],
            message="Install FFmpeg with Homebrew.",
        )
    if system == "linux" and shutil.which("apt"):
        return FfmpegInstallPlan(
            is_installed=False,
            command=["sudo", "apt", "install", "ffmpeg"],
            message="Install FFmpeg with apt.",
        )

    return FfmpegInstallPlan(
        is_installed=False,
        command=[],
        message=(
            "FFmpeg is not installed. Install it with your operating system's "
            "package manager, then make sure ffmpeg is available on PATH."
        ),
    )


def format_command(command: list[str]) -> str:
    return " ".join(command)


def run_ffmpeg_install(command: list[str]) -> None:
    if not command:
        raise ValueError("No FFmpeg install command is available for this platform.")
    subprocess.run(command, check=True)
