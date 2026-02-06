use std::env;
use std::path::PathBuf;

#[derive(Clone, Debug)]
pub struct Config {
    pub port: u16,
    pub data_dir: PathBuf,
    pub db_path: PathBuf,
    pub upload_dir: PathBuf,
    pub max_upload_size_bytes: u64,
    #[allow(dead_code)]
    pub max_upload_size_display: String,
    pub session_ttl_hours: u64,
}

impl Config {
    pub fn from_env() -> Self {
        let data_dir = PathBuf::from(env::var("DATA_DIR").unwrap_or_else(|_| "App_Data".into()));
        let db_path = data_dir.join("Data.sqlite");
        let upload_dir = data_dir.join("upload");
        let port = env::var("PORT")
            .ok()
            .and_then(|p| p.parse().ok())
            .unwrap_or(8080);
        let max_upload_size_bytes = env::var("MAX_UPLOAD_SIZE_BYTES")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(83_886_080);
        let session_ttl_hours = env::var("SESSION_TTL_HOURS")
            .ok()
            .and_then(|s| s.parse().ok())
            .unwrap_or(24);

        Self {
            port,
            data_dir,
            db_path,
            upload_dir,
            max_upload_size_bytes,
            max_upload_size_display: format_file_size(max_upload_size_bytes as i64),
            session_ttl_hours,
        }
    }
}

fn format_file_size(bytes: i64) -> String {
    let sizes = ["B", "KB", "MB", "GB", "TB"];
    let mut len = bytes as f64;
    let mut order = 0;
    while len >= 1024.0 && order < sizes.len() - 1 {
        order += 1;
        len /= 1024.0;
    }
    if order == 0 {
        format!("{} {}", len as i64, sizes[order])
    } else {
        format!("{:.2} {}", len, sizes[order])
    }
}
