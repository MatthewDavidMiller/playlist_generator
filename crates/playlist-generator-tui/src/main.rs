#![forbid(unsafe_code)]

//! The terminal frontend, packaged for Linux.
//!
//! The graphical frontend is the browser interface `playlist-generator-web`
//! serves. This one is for machines with no browser at hand, such as a server
//! reached over SSH. A terminal is read, write, and ioctl on file descriptors,
//! which resolves nothing at run time, so this build is one static binary that
//! works on every distribution.

mod app;
mod ui;

use std::io::{self, IsTerminal, Stdout, Write};
use std::panic;
use std::process::ExitCode;
use std::time::Duration;

use crossterm::event::{self, Event, KeyEventKind};
use crossterm::terminal::{
    EnterAlternateScreen, LeaveAlternateScreen, disable_raw_mode, enable_raw_mode,
};
use crossterm::{ExecutableCommand, cursor};
use ratatui::Terminal;
use ratatui::backend::CrosstermBackend;

use crate::app::App;

/// How long a pass waits for a key before redrawing anyway. A run reports its
/// progress through a channel rather than a keystroke, so without a timeout the
/// gauge would only move when the user happened to press something.
const TICK: Duration = Duration::from_millis(100);

fn enter() -> io::Result<Terminal<CrosstermBackend<Stdout>>> {
    enable_raw_mode()?;
    let mut stdout = io::stdout();
    stdout.execute(EnterAlternateScreen)?;
    Terminal::new(CrosstermBackend::new(stdout))
}

/// Put the terminal back. Every early exit runs this, including a panic:
/// leaving raw mode on gives the user a shell that echoes nothing and does not
/// respond to Ctrl+C.
fn restore() {
    let mut stdout = io::stdout();
    drop(stdout.execute(LeaveAlternateScreen));
    drop(stdout.execute(cursor::Show));
    drop(disable_raw_mode());
    drop(stdout.flush());
}

fn run(terminal: &mut Terminal<CrosstermBackend<Stdout>>) -> io::Result<()> {
    let mut app = App::default();
    while !app.should_quit {
        app.receive();
        terminal.draw(|frame| ui::draw(frame, &app))?;
        if event::poll(TICK)? {
            // Windows reports press and release; acting on both would run every
            // key twice. Terminals that report neither send `Unknown`, which
            // must still be honoured.
            if let Event::Key(key) = event::read()?
                && key.kind != KeyEventKind::Release
            {
                app.on_key(key);
            }
        }
    }
    // Waits for a cancelled run to finish removing its temporary file and
    // reaping FFmpeg before the process exits.
    app.shutdown();
    Ok(())
}

fn main() -> ExitCode {
    // Raw mode is an ioctl on the terminal, so without one the first thing
    // this does fails with ENXIO and a debug-printed io::Error. Checking up
    // front is what turns that into an answer, and pipes and cron jobs are
    // exactly where the command-line build belongs anyway.
    if !io::stdin().is_terminal() || !io::stdout().is_terminal() {
        eprintln!(
            "playlist-generator-tui needs a terminal. Use 'playlist-generator' \
             for scripts, pipes, and scheduled runs."
        );
        return ExitCode::from(2);
    }

    let previous = panic::take_hook();
    panic::set_hook(Box::new(move |info| {
        restore();
        previous(info);
    }));

    let mut terminal = match enter() {
        Ok(terminal) => terminal,
        Err(error) => {
            restore();
            eprintln!("playlist-generator-tui could not start: {error}");
            return ExitCode::FAILURE;
        }
    };
    let result = run(&mut terminal);
    restore();
    match result {
        Ok(()) => ExitCode::SUCCESS,
        Err(error) => {
            eprintln!("playlist-generator-tui stopped: {error}");
            ExitCode::FAILURE
        }
    }
}
