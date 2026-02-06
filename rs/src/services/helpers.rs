use chrono::Utc;
use std::path::PathBuf;

pub fn to_epoch() -> i64 {
    Utc::now().timestamp()
}

pub fn now_iso() -> String {
    Utc::now().format("%Y-%m-%dT%H:%M:%S").to_string()
}

#[allow(dead_code)]
pub fn format_file_size(bytes: i64) -> String {
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

pub fn get_upload_folder(session_id: &str, upload_dir: &PathBuf) -> PathBuf {
    upload_dir.join(session_id)
}

pub fn format_token(token: i64) -> String {
    format!("{:0>6}", token)
}
