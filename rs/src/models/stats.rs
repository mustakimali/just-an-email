use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, sqlx::FromRow)]
pub struct Stats {
    #[sqlx(rename = "Id")]
    pub id: i64,
    #[sqlx(rename = "Messages")]
    pub messages: i64,
    #[sqlx(rename = "MessagesSizeBytes")]
    pub messages_size_bytes: i64,
    #[sqlx(rename = "Files")]
    pub files: i64,
    #[sqlx(rename = "FilesSizeBytes")]
    pub files_size_bytes: i64,
    #[sqlx(rename = "Devices")]
    pub devices: i64,
    #[sqlx(rename = "Sessions")]
    pub sessions: i64,
    #[sqlx(rename = "Version")]
    pub version: i64,
    #[sqlx(rename = "DateCreatedUtc")]
    pub date_created_utc: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StatMonth {
    #[serde(rename = "month")]
    pub month: String,
    #[serde(rename = "days")]
    pub days: Vec<Stats>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StatYear {
    #[serde(rename = "year")]
    pub year: String,
    #[serde(rename = "months")]
    pub months: Vec<StatMonth>,
}
