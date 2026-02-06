use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, sqlx::FromRow)]
pub struct Message {
    #[sqlx(rename = "Id")]
    pub id: String,
    #[sqlx(rename = "SessionId")]
    pub session_id: String,
    #[sqlx(rename = "SessionIdVerification")]
    pub session_id_verification: Option<String>,
    #[sqlx(rename = "SocketConnectionId")]
    pub socket_connection_id: Option<String>,
    #[sqlx(rename = "EncryptionPublicKeyAlias")]
    pub encryption_public_key_alias: Option<String>,
    #[sqlx(rename = "Text")]
    pub text: String,
    #[sqlx(rename = "FileName")]
    pub file_name: Option<String>,
    #[sqlx(rename = "DateSent")]
    pub date_sent: String,
    #[sqlx(rename = "HasFile")]
    pub has_file: bool,
    #[sqlx(rename = "FileSizeBytes")]
    pub file_size_bytes: Option<i64>,
    #[sqlx(rename = "IsNotification")]
    pub is_notification: bool,
    #[sqlx(rename = "DateSentEpoch")]
    pub date_sent_epoch: i64,
}
