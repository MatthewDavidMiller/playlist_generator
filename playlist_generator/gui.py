"""Tkinter GUI for the playlist generator."""

from __future__ import annotations

import threading
import tkinter as tk
import traceback
from collections.abc import Callable
from dataclasses import dataclass
from pathlib import Path
from tkinter import filedialog, messagebox, ttk
from typing import Generic, TypeVar

from .audio_normalization import (
    VolumeNormalizationResult,
    normalize_audio_directory,
)
from .core import PlaylistGeneratorError, PlaylistResult, create_vlc_playlist
from .ffmpeg_setup import (
    build_ffmpeg_install_plan,
    format_command,
    run_ffmpeg_install,
)

ABSOLUTE_PATH_WARNING = (
    "Generated playlists store absolute local file paths. Sharing them may expose "
    "your directory structure and the playlist may not work on another machine."
)

TRequest = TypeVar("TRequest")
TResult = TypeVar("TResult")


@dataclass(frozen=True)
class PlaylistGenerationRequest:
    source_directory: str
    special_file: str
    output_path: str
    insert_every: int


@dataclass(frozen=True)
class VolumeNormalizationRequest:
    source_directory: str
    output_directory: str


@dataclass(frozen=True)
class FfmpegInstallRequest:
    command: tuple[str, ...]


def run_generation_request(request: PlaylistGenerationRequest) -> PlaylistResult:
    return create_vlc_playlist(
        source_directory=request.source_directory,
        special_file=request.special_file,
        insert_every=request.insert_every,
        output_path=request.output_path,
    )


def run_normalization_request(
    request: VolumeNormalizationRequest,
) -> VolumeNormalizationResult:
    return normalize_audio_directory(
        source_directory=request.source_directory,
        output_directory=request.output_directory,
    )


def run_ffmpeg_install_request(request: FfmpegInstallRequest) -> str:
    command = list(request.command)
    run_ffmpeg_install(command)
    return format_command(command)


class BackgroundGenerationRunner(Generic[TRequest, TResult]):
    def __init__(
        self,
        *,
        generator: Callable[[TRequest], TResult],
        schedule: Callable[[Callable[[], None]], None],
    ) -> None:
        self._generator = generator
        self._schedule = schedule
        self._is_running = False

    @property
    def is_running(self) -> bool:
        return self._is_running

    def start(
        self,
        request: TRequest,
        *,
        on_start: Callable[[], None],
        on_success: Callable[[TResult], None],
        on_error: Callable[[Exception], None],
    ) -> bool:
        if self._is_running:
            return False

        self._is_running = True
        on_start()
        threading.Thread(
            target=self._run_in_background,
            args=(request, on_success, on_error),
            daemon=True,
        ).start()
        return True

    def _run_in_background(
        self,
        request: TRequest,
        on_success: Callable[[TResult], None],
        on_error: Callable[[Exception], None],
    ) -> None:
        try:
            result = self._generator(request)
        except Exception as error:
            self._schedule(
                lambda captured_error=error: self._finish_with_error(
                    captured_error,
                    on_error,
                )
            )
            return

        self._schedule(lambda: self._finish_with_success(result, on_success))

    def _finish_with_success(
        self,
        result: TResult,
        on_success: Callable[[TResult], None],
    ) -> None:
        self._is_running = False
        on_success(result)

    def _finish_with_error(
        self,
        error: Exception,
        on_error: Callable[[Exception], None],
    ) -> None:
        self._is_running = False
        on_error(error)


class PlaylistGeneratorApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("VLC Playlist Generator")
        self.root.minsize(820, 500)

        self.source_directory = tk.StringVar()
        self.special_file = tk.StringVar()
        self.output_path = tk.StringVar()
        self.normalized_output_directory = tk.StringVar()
        self.insert_every = tk.IntVar(value=5)
        self.status_text = tk.StringVar(
            value="Select a music folder, special file, interval, and output path."
        )
        self.generation_runner = BackgroundGenerationRunner[
            PlaylistGenerationRequest, PlaylistResult
        ](
            generator=run_generation_request,
            schedule=lambda callback: self.root.after(0, callback),
        )
        self.normalization_runner = BackgroundGenerationRunner[
            VolumeNormalizationRequest, VolumeNormalizationResult
        ](
            generator=run_normalization_request,
            schedule=lambda callback: self.root.after(0, callback),
        )
        self.ffmpeg_install_runner = BackgroundGenerationRunner[
            FfmpegInstallRequest, str
        ](
            generator=run_ffmpeg_install_request,
            schedule=lambda callback: self.root.after(0, callback),
        )

        self._build_layout()

    def _build_layout(self) -> None:
        frame = ttk.Frame(self.root, padding=16)
        frame.grid(sticky="nsew")
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=1)

        for column in range(3):
            frame.columnconfigure(column, weight=1 if column == 1 else 0)

        ttk.Label(frame, text="Music folder").grid(
            row=0, column=0, sticky="w", pady=(0, 12)
        )
        ttk.Entry(frame, textvariable=self.source_directory).grid(
            row=0, column=1, sticky="ew", padx=(12, 12), pady=(0, 12)
        )
        ttk.Button(frame, text="Browse...", command=self.choose_source_directory).grid(
            row=0, column=2, sticky="ew", pady=(0, 12)
        )

        ttk.Label(frame, text="Special file").grid(
            row=1, column=0, sticky="w", pady=(0, 12)
        )
        ttk.Entry(frame, textvariable=self.special_file).grid(
            row=1, column=1, sticky="ew", padx=(12, 12), pady=(0, 12)
        )
        ttk.Button(frame, text="Browse...", command=self.choose_special_file).grid(
            row=1, column=2, sticky="ew", pady=(0, 12)
        )

        ttk.Label(frame, text="Insert every").grid(
            row=2, column=0, sticky="w", pady=(0, 12)
        )
        ttk.Spinbox(
            frame,
            from_=1,
            to=100000,
            textvariable=self.insert_every,
            width=12,
        ).grid(row=2, column=1, sticky="w", padx=(12, 12), pady=(0, 12))

        ttk.Label(frame, text="Output playlist").grid(
            row=3, column=0, sticky="w", pady=(0, 12)
        )
        ttk.Entry(frame, textvariable=self.output_path).grid(
            row=3, column=1, sticky="ew", padx=(12, 12), pady=(0, 12)
        )
        ttk.Button(frame, text="Save as...", command=self.choose_output_path).grid(
            row=3, column=2, sticky="ew", pady=(0, 12)
        )

        ttk.Separator(frame).grid(
            row=4,
            column=0,
            columnspan=3,
            sticky="ew",
            pady=(4, 12),
        )

        ttk.Label(frame, text="Normalized output").grid(
            row=5, column=0, sticky="w", pady=(0, 12)
        )
        ttk.Entry(frame, textvariable=self.normalized_output_directory).grid(
            row=5, column=1, sticky="ew", padx=(12, 12), pady=(0, 12)
        )
        ttk.Button(
            frame,
            text="Choose...",
            command=self.choose_normalized_output_directory,
        ).grid(row=5, column=2, sticky="ew", pady=(0, 12))

        self.normalize_button = ttk.Button(
            frame,
            text="Normalize Volume",
            command=self.normalize_volume,
        )
        self.normalize_button.grid(row=6, column=1, sticky="w", pady=(0, 12))

        self.install_ffmpeg_button = ttk.Button(
            frame,
            text="Install FFmpeg",
            command=self.install_ffmpeg,
        )
        self.install_ffmpeg_button.grid(row=6, column=2, sticky="ew", pady=(0, 12))

        status = tk.Text(frame, height=6, wrap="word")
        status.grid(row=7, column=0, columnspan=2, sticky="nsew", pady=(8, 0))
        status.insert("1.0", self.status_text.get())
        status.configure(state="disabled")
        frame.rowconfigure(7, weight=1)
        self.status_widget = status

        ttk.Label(
            frame,
            text=ABSOLUTE_PATH_WARNING,
            wraplength=520,
            justify="left",
        ).grid(row=8, column=0, columnspan=2, sticky="w", pady=(8, 0))

        self.generate_button = ttk.Button(
            frame,
            text="Generate",
            command=self.generate_playlist,
        )
        self.generate_button.grid(
            row=7, column=2, sticky="nsew", padx=(12, 0), pady=(8, 0)
        )

    def set_status(self, text: str) -> None:
        self.status_widget.configure(state="normal")
        self.status_widget.delete("1.0", "end")
        self.status_widget.insert("1.0", text)
        self.status_widget.configure(state="disabled")

    def default_playlist_path(self) -> str:
        source_directory = self.source_directory.get().strip()
        if not source_directory:
            return ""
        leaf_name = Path(source_directory).name or "playlist"
        return str(Path(source_directory) / f"{leaf_name}-playlist.m3u8")

    def choose_source_directory(self) -> None:
        selected = filedialog.askdirectory(
            parent=self.root,
            title="Choose the folder that contains the music for the playlist.",
            mustexist=True,
        )
        if selected:
            self.source_directory.set(selected)
            if not self.output_path.get().strip():
                self.output_path.set(self.default_playlist_path())
            if not self.normalized_output_directory.get().strip():
                source_path = Path(selected)
                self.normalized_output_directory.set(
                    str(source_path.with_name(f"{source_path.name}-normalized"))
                )

    def choose_special_file(self) -> None:
        initial_dir = self.source_directory.get().strip() or None
        selected = filedialog.askopenfilename(
            parent=self.root,
            title="Choose the special audio file",
            initialdir=initial_dir,
            filetypes=[
                ("Audio Files", "*.mp3 *.flac *.wav *.m4a *.aac *.ogg *.wma"),
                ("All Files", "*.*"),
            ],
        )
        if selected:
            self.special_file.set(selected)

    def choose_output_path(self) -> None:
        selected = filedialog.asksaveasfilename(
            parent=self.root,
            title="Save playlist as",
            initialfile=Path(
                self.output_path.get() or self.default_playlist_path()
            ).name
            or "playlist.m3u8",
            defaultextension=".m3u8",
            filetypes=[
                ("M3U8 Playlist", "*.m3u8"),
                ("M3U Playlist", "*.m3u"),
                ("All Files", "*.*"),
            ],
        )
        if selected:
            self.output_path.set(selected)

    def choose_normalized_output_directory(self) -> None:
        initial_dir = self.source_directory.get().strip() or None
        selected = filedialog.askdirectory(
            parent=self.root,
            title="Choose where normalized audio files will be written.",
            initialdir=initial_dir,
        )
        if selected:
            self.normalized_output_directory.set(selected)

    def set_generation_active(self, active: bool) -> None:
        self.generate_button.configure(state="disabled" if active else "normal")

    def set_normalization_active(self, active: bool) -> None:
        self.normalize_button.configure(state="disabled" if active else "normal")

    def set_ffmpeg_install_active(self, active: bool) -> None:
        self.install_ffmpeg_button.configure(state="disabled" if active else "normal")

    def build_generation_request(self) -> PlaylistGenerationRequest:
        return PlaylistGenerationRequest(
            source_directory=self.source_directory.get(),
            special_file=self.special_file.get(),
            insert_every=self.insert_every.get(),
            output_path=self.output_path.get(),
        )

    def build_normalization_request(self) -> VolumeNormalizationRequest:
        return VolumeNormalizationRequest(
            source_directory=self.source_directory.get(),
            output_directory=self.normalized_output_directory.get(),
        )

    def generate_playlist(self) -> None:
        self.generation_runner.start(
            self.build_generation_request(),
            on_start=self._on_generation_started,
            on_success=self._on_generation_succeeded,
            on_error=self._on_generation_failed,
        )

    def normalize_volume(self) -> None:
        self.normalization_runner.start(
            self.build_normalization_request(),
            on_start=self._on_normalization_started,
            on_success=self._on_normalization_succeeded,
            on_error=self._on_normalization_failed,
        )

    def install_ffmpeg(self) -> None:
        plan = build_ffmpeg_install_plan()
        if plan.is_installed:
            self.set_status(plan.message)
            messagebox.showinfo("FFmpeg", plan.message, parent=self.root)
            return
        if not plan.command:
            self.set_status(plan.message)
            messagebox.showerror("FFmpeg Not Found", plan.message, parent=self.root)
            return

        command_text = format_command(plan.command)
        confirmed = messagebox.askyesno(
            "Install FFmpeg",
            f"{plan.message}\n\nRun this command now?\n\n{command_text}",
            parent=self.root,
        )
        if not confirmed:
            self.set_status("FFmpeg installation was not started.")
            return

        self.ffmpeg_install_runner.start(
            FfmpegInstallRequest(tuple(plan.command)),
            on_start=self._on_ffmpeg_install_started,
            on_success=self._on_ffmpeg_install_succeeded,
            on_error=self._on_ffmpeg_install_failed,
        )

    def _on_generation_started(self) -> None:
        self.set_generation_active(True)
        self.set_status("Generating playlist...")

    def _on_generation_succeeded(self, result: PlaylistResult) -> None:
        self.set_generation_active(False)

        message = "\n".join(
            [
                f"Playlist created: {result.output_path}",
                f"Shuffled source tracks: {result.source_track_count}",
                f"Playlist entries: {result.playlist_entry_count}",
                f"Inserted every: {result.insert_every} songs",
                "",
                ABSOLUTE_PATH_WARNING,
            ]
        )
        self.set_status(message)
        messagebox.showinfo(
            "Success",
            (
                "Playlist created successfully.\n\n"
                f"{result.output_path}\n\n"
                f"Warning: {ABSOLUTE_PATH_WARNING}"
            ),
            parent=self.root,
        )

    def _on_generation_failed(self, error: Exception) -> None:
        self.set_generation_active(False)
        if isinstance(error, PlaylistGeneratorError):
            message = str(error)
        else:
            traceback.print_exception(error)
            message = "An unexpected error occurred while generating the playlist."

        self.set_status(message)
        messagebox.showerror("Generation Failed", message, parent=self.root)

    def _on_normalization_started(self) -> None:
        self.set_normalization_active(True)
        self.set_status("Normalizing audio files...")

    def _on_normalization_succeeded(
        self,
        result: VolumeNormalizationResult,
    ) -> None:
        self.set_normalization_active(False)
        message = "\n".join(
            [
                f"Normalized audio written to: {result.output_directory}",
                f"Files normalized: {result.normalized_file_count}",
                f"Files skipped: {result.skipped_file_count}",
            ]
        )
        self.set_status(message)
        messagebox.showinfo("Success", message, parent=self.root)

    def _on_normalization_failed(self, error: Exception) -> None:
        self.set_normalization_active(False)
        if isinstance(error, PlaylistGeneratorError):
            message = str(error)
        else:
            traceback.print_exception(error)
            message = "An unexpected error occurred while normalizing audio."

        self.set_status(message)
        messagebox.showerror("Normalization Failed", message, parent=self.root)

    def _on_ffmpeg_install_started(self) -> None:
        self.set_ffmpeg_install_active(True)
        self.set_status("Running FFmpeg install command...")

    def _on_ffmpeg_install_succeeded(self, command_text: str) -> None:
        self.set_ffmpeg_install_active(False)
        message = f"FFmpeg install command completed:\n{command_text}"
        self.set_status(message)
        messagebox.showinfo("FFmpeg", message, parent=self.root)

    def _on_ffmpeg_install_failed(self, error: Exception) -> None:
        self.set_ffmpeg_install_active(False)
        traceback.print_exception(error)
        message = "FFmpeg installation failed. Check the command output and try again."
        self.set_status(message)
        messagebox.showerror("FFmpeg Install Failed", message, parent=self.root)


def main() -> None:
    root = tk.Tk()
    PlaylistGeneratorApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
