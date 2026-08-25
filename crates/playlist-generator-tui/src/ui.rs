//! Rendering. Every function here reads [`App`] and writes to a ratatui
//! [`Frame`], so the tests draw into an in-memory backend and assert on the
//! resulting cells. No terminal is involved.

use playlist_generator_core::OperationEvent;
use ratatui::Frame;
use ratatui::layout::{Constraint, Layout, Rect};
use ratatui::style::{Modifier, Style, Stylize};
use ratatui::text::{Line, Span};
use ratatui::widgets::{Block, Borders, Gauge, List, ListItem, Paragraph, Tabs, Wrap};

use crate::app::{App, Page, REPOSITORY, TextField};

/// Widest the form is allowed to become. Everything is measured against the
/// form rather than the terminal, so a maximised window and a half-screen
/// window lay out identically instead of stretching every box across a
/// monitor.
const FORM_WIDTH: u16 = 80;

pub(crate) fn draw(frame: &mut Frame<'_>, app: &App) {
    let areas = Layout::vertical([
        Constraint::Length(1),
        Constraint::Min(1),
        Constraint::Length(1),
    ])
    .split(frame.area());
    let (Some(&tabs), Some(&body), Some(&footer)) = (areas.first(), areas.get(1), areas.get(2))
    else {
        return;
    };

    draw_tabs(frame, app, tabs);
    let form = centred(body);
    match app.page {
        Page::Create => draw_create(frame, app, form),
        Page::Normalize => draw_normalize(frame, app, form),
        Page::Activity => draw_activity(frame, app, form),
        Page::About => draw_about(frame, app, form),
    }
    draw_footer(frame, app, footer);
}

/// Centre one column and cap its width, the way the desktop build caps its
/// form. A terminal stretched across a wide monitor otherwise puts the label
/// and its value at opposite ends of the screen.
fn centred(area: Rect) -> Rect {
    let width = area.width.min(FORM_WIDTH);
    let x = area.x + (area.width.saturating_sub(width)) / 2;
    Rect::new(x, area.y, width, area.height)
}

fn draw_tabs(frame: &mut Frame<'_>, app: &App, area: Rect) {
    let selected = Page::ORDER
        .iter()
        .position(|page| *page == app.page)
        .unwrap_or_default();
    let titles = Page::ORDER.map(|page| Line::from(page.title()));
    let tabs = Tabs::new(titles.to_vec())
        .select(selected)
        .highlight_style(Style::default().add_modifier(Modifier::REVERSED))
        .divider("│");
    frame.render_widget(tabs, area);
}

fn draw_footer(frame: &mut Frame<'_>, app: &App, area: Rect) {
    let keys = match app.page {
        Page::Create | Page::Normalize => {
            "Tab move  Enter run  Alt+1-4 page  Alt+←/→ page  Ctrl+Q quit"
        }
        Page::Activity => "p pause/resume  s stop  Alt+1-4 page  Ctrl+Q quit",
        Page::About => "l license  n notices  ↑/↓ scroll  Alt+1-4 page  Ctrl+Q quit",
    };
    frame.render_widget(Paragraph::new(keys).dim(), area);
}

fn intro<'a>(title: &'a str, description: &'a str) -> Paragraph<'a> {
    Paragraph::new(vec![
        Line::from(Span::from(title).bold()),
        Line::from(""),
        Line::from(description),
    ])
    .wrap(Wrap { trim: true })
}

/// One labelled text box. `focused` draws the highlight and places the cursor,
/// which is the only thing telling the user where their typing will land.
fn field(
    frame: &mut Frame<'_>,
    area: Rect,
    label: &str,
    help: &str,
    value: &TextField,
    focused: bool,
    enabled: bool,
) {
    let areas = Layout::vertical([
        Constraint::Length(1),
        Constraint::Length(3),
        Constraint::Length(2),
    ])
    .split(area);
    let (Some(&label_area), Some(&box_area), Some(&help_area)) =
        (areas.first(), areas.get(1), areas.get(2))
    else {
        return;
    };

    frame.render_widget(Paragraph::new(Span::from(label).bold()), label_area);

    let border = if focused {
        Style::default().add_modifier(Modifier::BOLD)
    } else {
        Style::default().add_modifier(Modifier::DIM)
    };
    let block = Block::default().borders(Borders::ALL).border_style(border);
    let inner = block.inner(box_area);
    frame.render_widget(block, box_area);

    // Scroll the text so the cursor stays visible in a box narrower than the
    // path being typed, which is the ordinary case for a music library.
    let visible = usize::from(inner.width);
    let scroll = value.cursor.saturating_sub(visible.saturating_sub(1));
    let shown: String = value.value.chars().skip(scroll).take(visible).collect();
    frame.render_widget(Paragraph::new(shown), inner);

    if focused && enabled {
        let column = u16::try_from(value.cursor.saturating_sub(scroll)).unwrap_or(0);
        frame.set_cursor_position((inner.x + column.min(inner.width), inner.y));
    }

    frame.render_widget(
        Paragraph::new(help).dim().wrap(Wrap { trim: true }),
        help_area,
    );
}

fn button(frame: &mut Frame<'_>, area: Rect, label: &str, focused: bool, enabled: bool) {
    let style = if !enabled {
        Style::default().add_modifier(Modifier::DIM)
    } else if focused {
        Style::default().add_modifier(Modifier::REVERSED)
    } else {
        Style::default()
    };
    let text = format!("[ {label} ]");
    frame.render_widget(Paragraph::new(Span::styled(text, style)), area);
}

fn draw_create(frame: &mut Frame<'_>, app: &App, area: Rect) {
    let areas = Layout::vertical([
        Constraint::Length(5),
        Constraint::Length(6),
        Constraint::Length(6),
        Constraint::Length(6),
        Constraint::Length(6),
        Constraint::Length(1),
        Constraint::Min(0),
    ])
    .split(area);
    let enabled = !app.is_running();

    if let Some(&slot) = areas.first() {
        frame.render_widget(
            intro(
                "Create Playlist",
                "Builds a shuffled playlist file from a folder of music, dropping the \
                 same chosen track back in at a regular interval. Your audio files are \
                 only read, never changed.",
            ),
            slot,
        );
    }
    if let Some(&slot) = areas.get(1) {
        field(
            frame,
            slot,
            "Music folder",
            "This folder and every folder inside it is searched for MP3, FLAC, WAV, \
             M4A, AAC, Ogg, Opus, and WMA files.",
            &app.source,
            app.focus == 0,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(2) {
        field(
            frame,
            slot,
            "Repeated track",
            "One audio file that is added again and again through the playlist, such \
             as a jingle or a station ident. It is left out of the shuffle.",
            &app.special,
            app.focus == 1,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(3) {
        field(
            frame,
            slot,
            "How often to repeat it",
            "Add it after every this many shuffled tracks. If fewer tracks than that \
             are left at the end, no copy is added there.",
            &app.insert_every,
            app.focus == 2,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(4) {
        field(
            frame,
            slot,
            "Save the playlist as",
            "The playlist file to write. The name has to end in .m3u8, and an existing \
             file at this location is replaced.",
            &app.playlist_output,
            app.focus == 3,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(5) {
        button(frame, slot, "Create playlist", app.focus == 4, enabled);
    }
}

fn draw_normalize(frame: &mut Frame<'_>, app: &App, area: Rect) {
    let areas = Layout::vertical([
        Constraint::Length(6),
        Constraint::Length(6),
        Constraint::Length(6),
        Constraint::Length(6),
        Constraint::Length(6),
        Constraint::Length(1),
        Constraint::Min(0),
    ])
    .split(area);
    let enabled = !app.is_running();

    if let Some(&slot) = areas.first() {
        frame.render_widget(
            intro(
                "Normalize Volume",
                "Copies a music folder at an even listening volume, so tracks no longer \
                 jump between loud and quiet. The copies are new Opus files with the \
                 original tags kept; nothing in the source folder is modified. This \
                 needs FFmpeg installed.",
            ),
            slot,
        );
    }
    if let Some(&slot) = areas.get(1) {
        field(
            frame,
            slot,
            "Music folder",
            "The folder of audio files to copy, including everything inside it.",
            &app.normalize_source,
            app.focus == 0,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(2) {
        field(
            frame,
            slot,
            "Save the copies to",
            "A separate folder for the results, which keeps the same folder layout. \
             Files that are already there are left alone, so a run that was stopped \
             can simply be started again.",
            &app.normalize_output,
            app.focus == 1,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(3) {
        field(
            frame,
            slot,
            "FFmpeg program (optional)",
            "Leave this empty to use the FFmpeg already installed on this computer. \
             Fill it in only to choose a particular copy of FFmpeg.",
            &app.ffmpeg,
            app.focus == 2,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(4) {
        field(
            frame,
            slot,
            "Files at a time",
            "Higher finishes sooner but uses more of the processor. Lower leaves the \
             computer freer for other work.",
            &app.jobs,
            app.focus == 3,
            enabled,
        );
    }
    if let Some(&slot) = areas.get(5) {
        button(frame, slot, "Start normalizing", app.focus == 4, enabled);
    }
}

/// Progress as a fraction, computed in integers so that a very large file
/// count cannot lose precision on the way through a float.
fn fraction(progress: &OperationEvent) -> f64 {
    if progress.total == 0 {
        return 0.0;
    }
    let per_mille = progress
        .completed
        .saturating_mul(1_000)
        .checked_div(progress.total)
        .unwrap_or_default()
        .min(1_000);
    f64::from(u16::try_from(per_mille).unwrap_or(1_000)) / 1_000.0
}

fn draw_activity(frame: &mut Frame<'_>, app: &App, area: Rect) {
    let has_progress = app.progress.is_some();
    let areas = Layout::vertical([
        Constraint::Length(4),
        Constraint::Length(if has_progress { 4 } else { 0 }),
        Constraint::Length(1),
        Constraint::Min(1),
    ])
    .split(area);

    if let Some(&slot) = areas.first() {
        frame.render_widget(
            intro(
                "Activity",
                "Progress and messages from the current run, and from the last one that \
                 finished.",
            ),
            slot,
        );
    }

    if let (Some(&slot), Some(progress)) = (areas.get(1), app.progress.as_ref()) {
        let rows = Layout::vertical([Constraint::Length(1), Constraint::Length(1)]).split(slot);
        if let Some(&bar) = rows.first() {
            frame.render_widget(
                Gauge::default().ratio(fraction(progress)).label(format!(
                    "{} of {} files",
                    progress.completed, progress.total
                )),
                bar,
            );
        }
        if let Some(&counts) = rows.get(1) {
            frame.render_widget(
                Paragraph::new(format!(
                    "{} copied  •  {} skipped, already in the output folder  •  {} failed",
                    progress.normalized, progress.skipped, progress.failed
                )),
                counts,
            );
        }
    }

    if let Some(&slot) = areas.get(2) {
        let state = if !app.is_running() {
            String::new()
        } else if app.is_paused() {
            "Paused — p to resume, s to stop".to_owned()
        } else {
            "Running — p to pause, s to stop".to_owned()
        };
        frame.render_widget(Paragraph::new(state), slot);
    }

    if let Some(&slot) = areas.get(3) {
        if app.activity.is_empty() {
            frame.render_widget(Paragraph::new("Nothing has run yet."), slot);
        } else {
            // Show the tail: the newest lines are the ones being waited on.
            let height = usize::from(slot.height);
            let skip = app.activity.len().saturating_sub(height);
            let items: Vec<ListItem<'_>> = app
                .activity
                .iter()
                .skip(skip)
                .map(|line| ListItem::new(line.as_str()))
                .collect();
            frame.render_widget(List::new(items), slot);
        }
    }
}

fn draw_about(frame: &mut Frame<'_>, app: &App, area: Rect) {
    let mut lines = vec![
        Line::from(Span::from("Playlist Generator").bold()),
        Line::from(format!("Version {}", env!("CARGO_PKG_VERSION"))),
        Line::from(""),
        Line::from(
            "Builds shuffled .m3u8 playlists and volume-matched Opus copies of a music \
             library. A command-line version of the same program is next to this one in \
             the installation folder.",
        ),
        Line::from(""),
        Line::from(Span::from("Project repository").bold()),
        Line::from(REPOSITORY),
        Line::from(""),
        Line::from(if app.documents.license {
            "l  License (shown below)"
        } else {
            "l  License"
        }),
        Line::from(if app.documents.notices {
            "n  Third-party notices (shown below)"
        } else {
            "n  Third-party notices"
        }),
    ];

    // Both documents run to thousands of lines, so they stay folded away until
    // somebody asks to read them.
    if app.documents.license {
        lines.push(Line::from(""));
        lines.extend(include_str!("../../../LICENSE").lines().map(Line::from));
    }
    if app.documents.notices {
        lines.push(Line::from(""));
        lines.extend(
            include_str!("../../../THIRD_PARTY_NOTICES.txt")
                .lines()
                .map(Line::from),
        );
    }

    frame.render_widget(
        Paragraph::new(lines)
            .wrap(Wrap { trim: false })
            .scroll((app.about_scroll, 0)),
        area,
    );
}

#[cfg(test)]
mod tests {
    use crossterm::event::{KeyCode, KeyEvent, KeyModifiers};
    use playlist_generator_core::{NormalizationAction, OperationEvent};
    use ratatui::Terminal;
    use ratatui::backend::TestBackend;

    use super::{FORM_WIDTH, draw};
    use crate::app::{App, Page};

    /// Draw one frame at the given size and return every cell as text, one
    /// String per row, trailing blanks trimmed.
    fn render(app: &App, width: u16, height: u16) -> Vec<String> {
        // Nothing TestBackend does can fail, so every one of these patterns is
        // irrefutable.
        let Ok(mut terminal) = Terminal::new(TestBackend::new(width, height));
        let Ok(_) = terminal.draw(|frame| draw(frame, app));
        let buffer = terminal.backend().buffer().clone();
        (0..buffer.area.height)
            .map(|y| {
                (0..buffer.area.width)
                    .filter_map(|x| buffer.cell((x, y)).map(|cell| cell.symbol().to_owned()))
                    .collect::<String>()
                    .trim_end()
                    .to_owned()
            })
            .collect()
    }

    fn contains(rows: &[String], needle: &str) -> bool {
        rows.iter().any(|row| row.contains(needle))
    }

    /// The sizes a terminal realistically gets: the classic minimum, a common
    /// modern default, and a maximised window on a wide monitor.
    const SIZES: [(u16, u16); 3] = [(80, 24), (120, 40), (200, 60)];

    #[test]
    fn every_page_names_itself_at_every_supported_size() {
        for (width, height) in SIZES {
            for page in Page::ORDER {
                let mut app = App::default();
                app.page = page;
                let rows = render(&app, width, height);
                assert!(
                    contains(&rows, page.title()),
                    "{page:?} did not render its title at {width}x{height}: {rows:?}"
                );
            }
        }
    }

    #[test]
    fn no_row_ever_runs_past_the_terminal() {
        for (width, height) in SIZES {
            for page in Page::ORDER {
                let mut app = App::default();
                app.page = page;
                app.documents.license = true;
                for row in render(&app, width, height) {
                    assert!(
                        row.chars().count() <= usize::from(width),
                        "a row overflowed {width} columns on {page:?}: {row:?}"
                    );
                }
            }
        }
    }

    #[test]
    fn the_form_stops_widening_once_it_is_wide_enough_to_read() {
        let app = App::default();
        let wide = render(&app, 200, 40);
        let leading = wide
            .iter()
            .find(|row| row.contains("Music folder"))
            .map(|row| row.len() - row.trim_start().len())
            .unwrap_or_default();
        // Centred rather than stretched: a 200-column terminal must leave a
        // margin on the left instead of running the form edge to edge.
        assert!(
            leading > 0,
            "the form should be centred on a wide terminal: {wide:?}"
        );
        let width_used = wide
            .iter()
            .map(|row| row.chars().count())
            .max()
            .unwrap_or_default();
        assert!(
            width_used <= usize::from(FORM_WIDTH) + leading,
            "the form widened past its cap: used {width_used} columns"
        );
    }

    #[test]
    fn every_control_explains_itself() {
        let app = App::default();
        let rows = render(&app, 100, 40);
        for help in ["MP3", "jingle", ".m3u8"] {
            assert!(
                contains(&rows, help),
                "the create form dropped its guidance for {help}: {rows:?}"
            );
        }

        let mut app = App::default();
        app.page = Page::Normalize;
        let rows = render(&app, 100, 40);
        for help in ["FFmpeg", "folder layout", "processor"] {
            assert!(
                contains(&rows, help),
                "the normalize form dropped its guidance for {help}: {rows:?}"
            );
        }
    }

    #[test]
    fn the_footer_names_the_keys_that_work_on_the_page_being_shown() {
        let mut app = App::default();
        let rows = render(&app, 100, 30);
        assert!(contains(&rows, "Enter run"), "{rows:?}");

        app.page = Page::Activity;
        let rows = render(&app, 100, 30);
        assert!(contains(&rows, "p pause/resume"), "{rows:?}");

        app.page = Page::About;
        let rows = render(&app, 100, 30);
        assert!(contains(&rows, "l license"), "{rows:?}");
    }

    /// How many cells of the progress bar are filled. The bar carries its own
    /// label instead of a percentage, so the fill is the only thing reporting
    /// how far along the run is.
    fn gauge_fill(completed: usize, total: usize) -> usize {
        let mut app = App::default();
        app.page = Page::Activity;
        app.progress = Some(OperationEvent {
            action: NormalizationAction::Encoding,
            path: None,
            completed,
            total,
            normalized: 2,
            skipped: 1,
            failed: 0,
            message: "encoding".to_owned(),
        });
        render(&app, 100, 30)
            .iter()
            .find(|row| row.contains("files"))
            .map(|row| row.chars().filter(|cell| *cell == '█').count())
            .unwrap_or_default()
    }

    #[test]
    fn progress_is_reported_with_its_counts_once_a_run_has_sent_any() {
        let mut app = App::default();
        app.page = Page::Activity;
        app.progress = Some(OperationEvent {
            action: NormalizationAction::Encoding,
            path: None,
            completed: 3,
            total: 4,
            normalized: 2,
            skipped: 1,
            failed: 0,
            message: "encoding".to_owned(),
        });
        app.log("encoding");
        let rows = render(&app, 100, 30);
        assert!(contains(&rows, "3 of 4 files"), "{rows:?}");
        assert!(contains(&rows, "2 copied"), "{rows:?}");
    }

    #[test]
    fn the_progress_bar_fills_as_the_run_advances() {
        let none = gauge_fill(0, 4);
        let some = gauge_fill(1, 4);
        let most = gauge_fill(3, 4);
        assert_eq!(none, 0, "an unstarted run should show an empty bar");
        assert!(
            none < some && some < most,
            "the bar should track progress: {none} then {some} then {most}"
        );
    }

    #[test]
    fn an_empty_activity_page_says_so_rather_than_showing_nothing() {
        let mut app = App::default();
        app.page = Page::Activity;
        let rows = render(&app, 100, 30);
        assert!(contains(&rows, "Nothing has run yet."), "{rows:?}");
    }

    #[test]
    fn the_log_keeps_the_newest_lines_when_it_outgrows_the_page() {
        let mut app = App::default();
        app.page = Page::Activity;
        for index in 0..200 {
            app.log(format!("line {index}"));
        }
        let rows = render(&app, 100, 24);
        assert!(
            contains(&rows, "line 199"),
            "the tail was dropped: {rows:?}"
        );
        assert!(
            !contains(&rows, "line 0 "),
            "the head should have scrolled off: {rows:?}"
        );
    }

    #[test]
    fn legal_text_is_reachable_behind_its_headings() {
        let mut app = App::default();
        app.page = Page::About;
        let folded = render(&app, 100, 40);
        assert!(contains(&folded, "License"), "{folded:?}");
        assert!(
            !contains(&folded, "MIT License"),
            "the license body should stay folded away: {folded:?}"
        );

        app.on_key(KeyEvent::new(KeyCode::Char('l'), KeyModifiers::NONE));
        let opened = render(&app, 100, 40);
        assert!(
            contains(&opened, "MIT License") || contains(&opened, "Permission is hereby granted"),
            "the license body should appear once asked for: {opened:?}"
        );
    }

    #[test]
    fn the_focused_box_is_the_one_carrying_the_cursor() {
        // The cursor is the only thing telling the user which box their typing
        // lands in, so it has to move with focus.
        let Ok(mut terminal) = Terminal::new(TestBackend::new(100, 40));
        let mut app = App::default();

        let Ok(_) = terminal.draw(|frame| draw(frame, &app));
        let Ok(top) = terminal.get_cursor_position();

        app.focus = 1;
        let Ok(_) = terminal.draw(|frame| draw(frame, &app));
        let Ok(next) = terminal.get_cursor_position();

        assert!(
            next.y > top.y,
            "the cursor should follow focus down the form: {top:?} then {next:?}"
        );
    }
}
