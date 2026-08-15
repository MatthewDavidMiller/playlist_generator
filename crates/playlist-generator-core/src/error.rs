use std::path::PathBuf;

#[derive(Debug, thiserror::Error)]
pub enum Error {
    #[error("{0}")]
    Validation(String),
    #[error("unable to access '{path}': {source}")]
    Io {
        path: PathBuf,
        #[source]
        source: std::io::Error,
    },
    #[error("{0}")]
    Process(String),
    #[error("operation was interrupted")]
    Interrupted,
}

pub type Result<T> = std::result::Result<T, Error>;

pub(crate) trait IoContext<T> {
    fn at(self, path: impl Into<PathBuf>) -> Result<T>;
}

impl<T> IoContext<T> for std::io::Result<T> {
    fn at(self, path: impl Into<PathBuf>) -> Result<T> {
        let path = path.into();
        self.map_err(|source| Error::Io { path, source })
    }
}
