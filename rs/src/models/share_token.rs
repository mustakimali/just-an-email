use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ShareToken {
    #[serde(rename = "Id")]
    pub id: i64,
    #[serde(rename = "SessionId")]
    pub session_id: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionShareToken {
    #[serde(rename = "Token")]
    pub token: i64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SessionMetaByConnectionId {
    #[serde(rename = "SessionId")]
    pub session_id: String,
}
