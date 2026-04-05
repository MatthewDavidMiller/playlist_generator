"""Tkinter GUI for the playlist generator."""

from __future__ import annotations

import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk

from .core import create_vlc_playlist


class PlaylistGeneratorApp:
    def __init__(self, root: tk.Tk) -> None:
        self.root = root
        self.root.title("VLC Playlist Generator")
        self.root.minsize(760, 340)

        self.source_directory = tk.StringVar()
        self.special_file = tk.StringVar()
        self.output_path = tk.StringVar()
        self.insert_every = tk.IntVar(value=5)
        self.status_text = tk.StringVar(
            value="Select a music folder, special file, interval, and output path."
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

        status = tk.Text(frame, height=5, wrap="word")
        status.grid(row=4, column=0, columnspan=2, sticky="nsew", pady=(8, 0))
        status.insert("1.0", self.status_text.get())
        status.configure(state="disabled")
        frame.rowconfigure(4, weight=1)
        self.status_widget = status

        ttk.Button(frame, text="Generate", command=self.generate_playlist).grid(
            row=4, column=2, sticky="nsew", padx=(12, 0), pady=(8, 0)
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

    def generate_playlist(self) -> None:
        try:
            self.set_status("Generating playlist...")
            result = create_vlc_playlist(
                source_directory=self.source_directory.get(),
                special_file=self.special_file.get(),
                insert_every=self.insert_every.get(),
                output_path=self.output_path.get(),
            )
        except Exception as error:
            message = str(error)
            self.set_status(message)
            messagebox.showerror("Generation Failed", message, parent=self.root)
            return

        message = "\n".join(
            [
                f"Playlist created: {result.output_path}",
                f"Shuffled source tracks: {result.source_track_count}",
                f"Playlist entries: {result.playlist_entry_count}",
                f"Inserted every: {result.insert_every} songs",
            ]
        )
        self.set_status(message)
        messagebox.showinfo(
            "Success",
            f"Playlist created successfully.\n\n{result.output_path}",
            parent=self.root,
        )


def main() -> None:
    root = tk.Tk()
    PlaylistGeneratorApp(root)
    root.mainloop()


if __name__ == "__main__":
    main()
