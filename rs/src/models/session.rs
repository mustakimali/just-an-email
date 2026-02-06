use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize, sqlx::FromRow)]
pub struct SessionEntity {
    #[sqlx(rename = "Id")]
    pub id: String,
    #[sqlx(rename = "IdVerification")]
    pub id_verification: String,
    #[sqlx(rename = "DateCreated")]
    pub date_created: String,
    #[sqlx(rename = "IsLiteSession")]
    pub is_lite_session: bool,
    #[sqlx(rename = "CleanupJobId")]
    pub cleanup_job_id: Option<String>,
    #[sqlx(rename = "ConnectionIdsJson")]
    pub connection_ids_json: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Session {
    pub id: String,
    pub id_verification: String,
    pub date_created: String,
    pub is_lite_session: bool,
    pub cleanup_job_id: Option<String>,
    pub connection_ids: Vec<String>,
}

impl Session {
    pub fn from_entity(entity: SessionEntity) -> Self {
        let connection_ids: Vec<String> = entity
            .connection_ids_json
            .as_deref()
            .and_then(|j| serde_json::from_str(j).ok())
            .unwrap_or_default();

        Self {
            id: entity.id,
            id_verification: entity.id_verification,
            date_created: entity.date_created,
            is_lite_session: entity.is_lite_session,
            cleanup_job_id: entity.cleanup_job_id,
            connection_ids,
        }
    }
}
