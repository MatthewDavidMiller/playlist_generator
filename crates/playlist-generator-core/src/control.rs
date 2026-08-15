use std::sync::{Arc, Condvar, Mutex};

use crate::{Error, Result};

#[derive(Debug, Default)]
struct State {
    paused: bool,
    cancelled: bool,
}

#[derive(Clone, Debug, Default)]
pub struct RunControl {
    inner: Arc<(Mutex<State>, Condvar)>,
}

impl RunControl {
    pub fn pause(&self) {
        if let Ok(mut state) = self.inner.0.lock() {
            state.paused = true;
        }
    }

    pub fn resume(&self) {
        if let Ok(mut state) = self.inner.0.lock() {
            state.paused = false;
            self.inner.1.notify_all();
        }
    }

    pub fn cancel(&self) {
        if let Ok(mut state) = self.inner.0.lock() {
            state.cancelled = true;
            state.paused = false;
            self.inner.1.notify_all();
        }
    }

    pub fn is_paused(&self) -> bool {
        self.inner.0.lock().is_ok_and(|state| state.paused)
    }

    pub fn is_cancelled(&self) -> bool {
        self.inner.0.lock().map_or(true, |state| state.cancelled)
    }

    pub fn checkpoint(&self) -> Result<()> {
        let mut state = self.inner.0.lock().map_err(|_| Error::Interrupted)?;
        while state.paused && !state.cancelled {
            state = self.inner.1.wait(state).map_err(|_| Error::Interrupted)?;
        }
        if state.cancelled {
            Err(Error::Interrupted)
        } else {
            Ok(())
        }
    }
}
