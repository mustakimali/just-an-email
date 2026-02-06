use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, sqlx::FromRow)]
pub struct PublicKey {
    #[sqlx(rename = "Id")]
    pub id: String,
    #[sqlx(rename = "SessionId")]
    pub session_id: String,
    #[sqlx(rename = "Alias")]
    pub alias: String,
    #[sqlx(rename = "PublicKeyJson")]
    pub public_key_json: String,
    #[sqlx(rename = "DateCreated")]
    pub date_created: String,
}
