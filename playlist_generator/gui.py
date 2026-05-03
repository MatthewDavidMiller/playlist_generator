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
DEFAULT_WINDOW_SIZE = (900, 680)
MIN_WINDOW_SIZE = (560, 420)
SCREEN_MARGIN = 80
MIN_WRAP_LENGTH = 180

THEME_COLORS = {
    "dark": {
        "background": "#111827",
        "surface": "#1f2937",
        "surface_alt": "#273449",
        "text": "#f9fafb",
        "muted": "#cbd5e1",
        "accent": "#38bdf8",
        "accent_pressed": "#0ea5e9",
        "border": "#334155",
        "field": "#0f172a",
        "field_text": "#f8fafc",
        "disabled": "#94a3b8",
        "select": "#075985",
    },
    "light": {
        "background": "#f4f7fb",
        "surface": "#ffffff",
        "surface_alt": "#eef4fb",
        "text": "#172033",
        "muted": "#526173",
        "accent": "#0f766e",
        "accent_pressed": "#115e59",
        "border": "#cfd9e5",
        "field": "#ffffff",
        "field_text": "#111827",
        "disabled": "#7b8794",
        "select": "#99f6e4",
    },
}

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
        self._configure_window_size()

        self.playlist_source_directory = tk.StringVar()
        self.normalization_source_directory = tk.StringVar()
        self.source_directory = self.playlist_source_directory
        self.special_file = tk.StringVar()
        self.output_path = tk.StringVar()
        self.normalized_output_directory = tk.StringVar()
        self.insert_every = tk.IntVar(value=5)
        self.theme_name = tk.StringVar(value="dark")
        self.status_text = tk.StringVar(
            value=(
                "Start with the playlist section, or use volume normalization "
                "as a separate copy step."
            )
        )
        self.status_widget: tk.Text | None = None
        self.status_scrollbar: ttk.Scrollbar | None = None
        self.theme_button: ttk.Button | None = None
        self.scroll_canvas: tk.Canvas | None = None
        self.scrollable_content: ttk.Frame | None = None
        self.scroll_window: int | None = None
        self.wrapped_labels: list[tuple[ttk.Label, int]] = []
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

        self._configure_theme()
        self._build_layout()
        self.apply_theme()

    def _configure_window_size(self) -> None:
        width, height = self._calculate_window_size(
            screen_width=self.root.winfo_screenwidth(),
            screen_height=self.root.winfo_screenheight(),
        )
        min_width, min_height = self._calculate_min_window_size(
            screen_width=self.root.winfo_screenwidth(),
            screen_height=self.root.winfo_screenheight(),
        )
        self.root.geometry(f"{width}x{height}")
        self.root.minsize(min_width, min_height)

    @staticmethod
    def _calculate_window_size(
        *, screen_width: int, screen_height: int
    ) -> tuple[int, int]:
        available_width = max(320, screen_width - SCREEN_MARGIN)
        available_height = max(260, screen_height - SCREEN_MARGIN)
        return (
            min(DEFAULT_WINDOW_SIZE[0], available_width),
            min(DEFAULT_WINDOW_SIZE[1], available_height),
        )

    @staticmethod
    def _calculate_min_window_size(
        *, screen_width: int, screen_height: int
    ) -> tuple[int, int]:
        return (
            min(MIN_WINDOW_SIZE[0], max(320, screen_width - SCREEN_MARGIN)),
            min(MIN_WINDOW_SIZE[1], max(260, screen_height - SCREEN_MARGIN)),
        )

    def _build_layout(self) -> None:
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(0, weight=1)

        canvas = tk.Canvas(self.root, borderwidth=0, highlightthickness=0)
        scrollbar = ttk.Scrollbar(self.root, orient="vertical", command=canvas.yview)
        canvas.configure(yscrollcommand=scrollbar.set)
        canvas.grid(row=0, column=0, sticky="nsew")
        scrollbar.grid(row=0, column=1, sticky="ns")

        frame = ttk.Frame(canvas, padding=20, style="App.TFrame")
        scroll_window = canvas.create_window((0, 0), window=frame, anchor="nw")
        self.scroll_canvas = canvas
        self.scrollable_content = frame
        self.scroll_window = scroll_window

        frame.bind("<Configure>", self._on_scroll_content_configured)
        canvas.bind("<Configure>", self._on_scroll_canvas_configured)
        canvas.bind("<Enter>", self._bind_mousewheel)
        canvas.bind("<Leave>", self._unbind_mousewheel)

        frame.columnconfigure(0, weight=1)
        frame.rowconfigure(3, weight=1)

        header = ttk.Frame(frame, style="App.TFrame")
        header.grid(row=0, column=0, sticky="ew", pady=(0, 16))
        header.columnconfigure(0, weight=1)

        ttk.Label(
            header,
            text="VLC Playlist Generator",
            style="Title.TLabel",
        ).grid(row=0, column=0, sticky="w")
        self._add_wrapped_label(
            header,
            text=(
                "Build shuffled playlists and copy normalized audio without "
                "changing originals."
            ),
            style="HeaderHelp.TLabel",
        ).grid(row=1, column=0, sticky="ew", pady=(4, 0))
        self.theme_button = ttk.Button(
            header,
            text="Light Mode",
            command=self.toggle_theme,
            style="Secondary.TButton",
        )
        self.theme_button.grid(row=0, column=1, rowspan=2, sticky="e", padx=(16, 0))

        playlist_section = self._create_section(
            frame,
            row=1,
            title="Playlist Generator",
            description=(
                "Choose the tracks to shuffle, the audio file to insert, and "
                "where the playlist should be saved."
            ),
        )

        self._add_path_row(
            playlist_section,
            row=1,
            label="Playlist music folder",
            variable=self.playlist_source_directory,
            help_text=(
                "Scanned recursively for songs that will be shuffled into the playlist."
            ),
            button_text="Browse...",
            command=self.choose_playlist_source_directory,
        )
        self._add_path_row(
            playlist_section,
            row=2,
            label="Special audio file",
            variable=self.special_file,
            help_text="Inserted after each full block of regular songs.",
            button_text="Browse...",
            command=self.choose_special_file,
        )
        self._add_interval_row(playlist_section, row=3)
        self._add_path_row(
            playlist_section,
            row=4,
            label="Output playlist",
            variable=self.output_path,
            help_text="Saved as an .m3u8 file with absolute paths for VLC.",
            button_text="Save as...",
            command=self.choose_output_path,
        )

        playlist_actions = ttk.Frame(playlist_section, style="Card.TFrame")
        playlist_actions.grid(
            row=5,
            column=0,
            columnspan=3,
            sticky="ew",
            pady=(12, 0),
        )
        playlist_actions.columnconfigure(0, weight=1)
        self._add_wrapped_label(
            playlist_actions,
            text=ABSOLUTE_PATH_WARNING,
            justify="left",
            style="Help.TLabel",
            reserve_width=180,
        ).grid(row=0, column=0, sticky="ew")
        self.generate_button = ttk.Button(
            playlist_actions,
            text="Generate Playlist",
            command=self.generate_playlist,
            style="Accent.TButton",
        )
        self.generate_button.grid(row=0, column=1, sticky="e", padx=(16, 0))

        normalization_section = self._create_section(
            frame,
            row=2,
            title="Volume Normalization",
            description=(
                "Copy supported audio files to a separate folder after FFmpeg "
                "normalizes their loudness as Opus 160k files."
            ),
        )

        self._add_path_row(
            normalization_section,
            row=1,
            label="Normalization source folder",
            variable=self.normalization_source_directory,
            help_text=(
                "Scanned recursively for files to normalize; this can differ "
                "from the playlist folder."
            ),
            button_text="Browse...",
            command=self.choose_normalization_source_directory,
        )
        self._add_path_row(
            normalization_section,
            row=2,
            label="Normalized output folder",
            variable=self.normalized_output_directory,
            help_text=(
                "Receives copied .opus files while preserving source subfolders."
            ),
            button_text="Choose...",
            command=self.choose_normalized_output_directory,
        )

        normalization_actions = ttk.Frame(normalization_section, style="Card.TFrame")
        normalization_actions.grid(
            row=3,
            column=0,
            columnspan=3,
            sticky="ew",
            pady=(12, 0),
        )
        normalization_actions.columnconfigure(0, weight=1)
        self._add_wrapped_label(
            normalization_actions,
            text=(
                "FFmpeg is required for normalization; playlist generation "
                "does not need it."
            ),
            justify="left",
            style="Help.TLabel",
            reserve_width=290,
        ).grid(row=0, column=0, sticky="ew")
        self.install_ffmpeg_button = ttk.Button(
            normalization_actions,
            text="Install FFmpeg",
            command=self.install_ffmpeg,
            style="Secondary.TButton",
        )
        self.install_ffmpeg_button.grid(row=0, column=1, sticky="e", padx=(16, 8))
        self.normalize_button = ttk.Button(
            normalization_actions,
            text="Normalize Volume",
            command=self.normalize_volume,
            style="Accent.TButton",
        )
        self.normalize_button.grid(row=0, column=2, sticky="e")

        status_frame = self._create_section(
            frame,
            row=3,
            title="Status",
            description="Progress, results, and validation messages appear here.",
        )
        status_frame.rowconfigure(1, weight=1)
        status_frame.columnconfigure(0, weight=1)
        status = tk.Text(
            status_frame,
            height=7,
            wrap="word",
            relief="flat",
            borderwidth=0,
            padx=12,
            pady=10,
        )
        status_scrollbar = ttk.Scrollbar(
            status_frame,
            orient="vertical",
            command=status.yview,
        )
        status.configure(yscrollcommand=status_scrollbar.set)
        status.grid(row=1, column=0, columnspan=2, sticky="nsew", pady=(10, 0))
        status_scrollbar.grid(row=1, column=2, sticky="ns", pady=(10, 0))
        status.insert("1.0", self.status_text.get())
        status.configure(state="disabled")
        self.status_widget = status
        self.status_scrollbar = status_scrollbar

    def _on_scroll_content_configured(self, event: tk.Event) -> None:
        if self.scroll_canvas is None:
            return
        self.scroll_canvas.configure(scrollregion=self.scroll_canvas.bbox("all"))

    def _on_scroll_canvas_configured(self, event: tk.Event) -> None:
        if self.scroll_canvas is None or self.scroll_window is None:
            return
        self.scroll_canvas.itemconfigure(self.scroll_window, width=event.width)
        self._update_wraplengths(event.width)

    def _bind_mousewheel(self, event: tk.Event) -> None:
        self.root.bind_all("<MouseWheel>", self._on_mousewheel)
        self.root.bind_all("<Button-4>", self._on_mousewheel)
        self.root.bind_all("<Button-5>", self._on_mousewheel)

    def _unbind_mousewheel(self, event: tk.Event) -> None:
        self.root.unbind_all("<MouseWheel>")
        self.root.unbind_all("<Button-4>")
        self.root.unbind_all("<Button-5>")

    def _on_mousewheel(self, event: tk.Event) -> None:
        if self.scroll_canvas is None:
            return
        units = self._scroll_units(event)
        if units:
            self.scroll_canvas.yview_scroll(units, "units")

    @staticmethod
    def _scroll_units(event: tk.Event) -> int:
        button_number = getattr(event, "num", None)
        if button_number == 4:
            return -3
        if button_number == 5:
            return 3

        delta = getattr(event, "delta", 0)
        if delta > 0:
            return -max(1, abs(delta) // 120)
        if delta < 0:
            return max(1, abs(delta) // 120)
        return 0

    def _add_wrapped_label(
        self,
        parent: ttk.Widget,
        *,
        text: str,
        style: str,
        justify: str = "left",
        reserve_width: int = 0,
    ) -> ttk.Label:
        label = ttk.Label(parent, text=text, justify=justify, style=style)
        self.wrapped_labels.append((label, reserve_width))
        label.bind(
            "<Configure>",
            lambda event, label=label: self._set_label_wraplength(label, event.width),
        )
        return label

    def _update_wraplengths(self, container_width: int) -> None:
        for label, reserve_width in self.wrapped_labels:
            self._set_label_wraplength(label, container_width - reserve_width)

    @staticmethod
    def _set_label_wraplength(label: ttk.Label, width: int) -> None:
        label.configure(wraplength=max(MIN_WRAP_LENGTH, width))

    def _create_section(
        self,
        parent: ttk.Widget,
        *,
        row: int,
        title: str,
        description: str,
    ) -> ttk.Frame:
        section = ttk.Frame(parent, padding=16, style="Card.TFrame")
        section.grid(row=row, column=0, sticky="nsew", pady=(0, 16))
        section.columnconfigure(1, weight=1)

        ttk.Label(section, text=title, style="SectionTitle.TLabel").grid(
            row=0,
            column=0,
            sticky="w",
        )
        self._add_wrapped_label(
            section,
            text=description,
            justify="left",
            style="Help.TLabel",
        ).grid(row=0, column=1, columnspan=2, sticky="ew", padx=(18, 0))
        return section

    def _add_path_row(
        self,
        parent: ttk.Widget,
        *,
        row: int,
        label: str,
        variable: tk.StringVar,
        help_text: str,
        button_text: str,
        command: Callable[[], None],
    ) -> None:
        ttk.Label(parent, text=label, style="Field.TLabel").grid(
            row=row,
            column=0,
            sticky="nw",
            pady=(14, 0),
        )
        field_frame = ttk.Frame(parent, style="Card.TFrame")
        field_frame.grid(row=row, column=1, sticky="ew", padx=(18, 12), pady=(14, 0))
        field_frame.columnconfigure(0, weight=1)
        ttk.Entry(field_frame, textvariable=variable).grid(
            row=0,
            column=0,
            sticky="ew",
        )
        self._add_wrapped_label(
            field_frame,
            text=help_text,
            justify="left",
            style="Help.TLabel",
        ).grid(row=1, column=0, sticky="ew", pady=(4, 0))
        ttk.Button(parent, text=button_text, command=command).grid(
            row=row,
            column=2,
            sticky="ew",
            pady=(14, 0),
        )

    def _add_interval_row(self, parent: ttk.Widget, *, row: int) -> None:
        ttk.Label(parent, text="Insert every", style="Field.TLabel").grid(
            row=row,
            column=0,
            sticky="nw",
            pady=(14, 0),
        )
        field_frame = ttk.Frame(parent, style="Card.TFrame")
        field_frame.grid(row=row, column=1, sticky="w", padx=(18, 12), pady=(14, 0))
        ttk.Spinbox(
            field_frame,
            from_=1,
            to=100000,
            textvariable=self.insert_every,
            width=12,
        ).grid(row=0, column=0, sticky="w")
        self._add_wrapped_label(
            field_frame,
            text=(
                "Number of regular songs played before the special audio "
                "file is inserted."
            ),
            justify="left",
            style="Help.TLabel",
        ).grid(row=1, column=0, sticky="w", pady=(4, 0))

    def _configure_theme(self) -> None:
        style = ttk.Style(self.root)
        if "clam" in style.theme_names():
            style.theme_use("clam")
        self.style = style

    def apply_theme(self) -> None:
        colors = THEME_COLORS[self.theme_name.get()]
        self.root.configure(background=colors["background"])
        if self.scroll_canvas is not None:
            self.scroll_canvas.configure(background=colors["background"])

        self.style.configure("App.TFrame", background=colors["background"])
        self.style.configure(
            "Card.TFrame",
            background=colors["surface"],
            bordercolor=colors["border"],
            lightcolor=colors["surface"],
            darkcolor=colors["surface"],
            relief="flat",
        )
        self.style.configure(
            "TLabel",
            background=colors["surface"],
            foreground=colors["text"],
        )
        self.style.configure(
            "Title.TLabel",
            background=colors["background"],
            foreground=colors["text"],
            font=("TkDefaultFont", 18, "bold"),
        )
        self.style.configure(
            "SectionTitle.TLabel",
            background=colors["surface"],
            foreground=colors["text"],
            font=("TkDefaultFont", 12, "bold"),
        )
        self.style.configure(
            "Field.TLabel",
            background=colors["surface"],
            foreground=colors["text"],
            font=("TkDefaultFont", 10, "bold"),
        )
        self.style.configure(
            "Help.TLabel",
            background=colors["surface"],
            foreground=colors["muted"],
        )
        self.style.configure(
            "HeaderHelp.TLabel",
            background=colors["background"],
            foreground=colors["muted"],
        )
        self.style.configure(
            "TEntry",
            fieldbackground=colors["field"],
            foreground=colors["field_text"],
            insertcolor=colors["field_text"],
            bordercolor=colors["border"],
            lightcolor=colors["border"],
            darkcolor=colors["border"],
            padding=6,
        )
        self.style.configure(
            "TSpinbox",
            fieldbackground=colors["field"],
            foreground=colors["field_text"],
            insertcolor=colors["field_text"],
            bordercolor=colors["border"],
            arrowcolor=colors["text"],
            padding=4,
        )
        self.style.configure(
            "TButton",
            background=colors["surface_alt"],
            foreground=colors["text"],
            bordercolor=colors["border"],
            focusthickness=1,
            focuscolor=colors["accent"],
            padding=(12, 7),
        )
        self.style.map(
            "TButton",
            background=[
                ("pressed", colors["border"]),
                ("active", colors["surface_alt"]),
                ("disabled", colors["surface"]),
            ],
            foreground=[("disabled", colors["disabled"])],
        )
        self.style.configure(
            "Secondary.TButton",
            background=colors["surface_alt"],
            foreground=colors["text"],
        )
        self.style.configure(
            "Accent.TButton",
            background=colors["accent"],
            foreground="#ffffff",
            bordercolor=colors["accent"],
        )
        self.style.map(
            "Accent.TButton",
            background=[
                ("pressed", colors["accent_pressed"]),
                ("active", colors["accent_pressed"]),
                ("disabled", colors["surface_alt"]),
            ],
            foreground=[("disabled", colors["disabled"])],
        )

        if self.status_widget is not None:
            self.status_widget.configure(
                background=colors["field"],
                foreground=colors["field_text"],
                insertbackground=colors["field_text"],
                selectbackground=colors["select"],
                selectforeground=colors["field_text"],
                highlightbackground=colors["border"],
                highlightcolor=colors["accent"],
            )
        if self.theme_button is not None:
            next_theme = (
                "Light Mode" if self.theme_name.get() == "dark" else "Dark Mode"
            )
            self.theme_button.configure(text=next_theme)

    def toggle_theme(self) -> None:
        next_theme = "light" if self.theme_name.get() == "dark" else "dark"
        self.theme_name.set(next_theme)
        self.apply_theme()

    def set_status(self, text: str) -> None:
        self.status_text.set(text)
        if self.status_widget is None:
            return
        self.status_widget.configure(state="normal")
        self.status_widget.delete("1.0", "end")
        self.status_widget.insert("1.0", text)
        self.status_widget.configure(state="disabled")

    def default_playlist_path(self) -> str:
        source_directory = self.playlist_source_directory.get().strip()
        if not source_directory:
            return ""
        leaf_name = Path(source_directory).name or "playlist"
        return str(Path(source_directory) / f"{leaf_name}-playlist.m3u8")

    def default_normalized_output_directory(self) -> str:
        source_directory = self.normalization_source_directory.get().strip()
        if not source_directory:
            return ""
        source_path = Path(source_directory)
        return str(source_path.with_name(f"{source_path.name}-normalized"))

    def choose_playlist_source_directory(self) -> None:
        selected = filedialog.askdirectory(
            parent=self.root,
            title="Choose the folder that contains the music for the playlist.",
            mustexist=True,
        )
        if selected:
            self.playlist_source_directory.set(selected)
            if not self.output_path.get().strip():
                self.output_path.set(self.default_playlist_path())
            if not self.normalization_source_directory.get().strip():
                self.normalization_source_directory.set(selected)
            if not self.normalized_output_directory.get().strip():
                self.normalized_output_directory.set(
                    self.default_normalized_output_directory()
                )

    def choose_source_directory(self) -> None:
        self.choose_playlist_source_directory()

    def choose_normalization_source_directory(self) -> None:
        selected = filedialog.askdirectory(
            parent=self.root,
            title="Choose the folder that contains files to normalize.",
            mustexist=True,
        )
        if selected:
            self.normalization_source_directory.set(selected)
            if not self.normalized_output_directory.get().strip():
                self.normalized_output_directory.set(
                    self.default_normalized_output_directory()
                )

    def choose_special_file(self) -> None:
        initial_dir = self.playlist_source_directory.get().strip() or None
        selected = filedialog.askopenfilename(
            parent=self.root,
            title="Choose the special audio file",
            initialdir=initial_dir,
            filetypes=[
                ("Audio Files", "*.mp3 *.flac *.wav *.m4a *.aac *.ogg *.opus *.wma"),
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
        initial_dir = self.normalization_source_directory.get().strip() or None
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
            source_directory=self.playlist_source_directory.get(),
            special_file=self.special_file.get(),
            insert_every=self.insert_every.get(),
            output_path=self.output_path.get(),
        )

    def build_normalization_request(self) -> VolumeNormalizationRequest:
        return VolumeNormalizationRequest(
            source_directory=self.normalization_source_directory.get(),
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
