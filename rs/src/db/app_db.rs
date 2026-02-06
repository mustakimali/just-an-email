use rand::Rng;
use sqlx::SqlitePool;
use uuid::Uuid;

use crate::models::message::Message;
use crate::models::public_key::PublicKey;
use crate::models::session::{Session, SessionEntity};
use crate::models::share_token::{SessionMetaByConnectionId, SessionShareToken, ShareToken};

pub fn new_guid() -> String {
    Uuid::new_v4().simple().to_string()
}

// --- KV Store ---

pub async fn kv_get<T: serde::de::DeserializeOwned>(
    pool: &SqlitePool,
    id: &str,
) -> Result<Option<T>, sqlx::Error> {
    let row: Option<(String,)> =
        sqlx::query_as("SELECT DataJson FROM Kv WHERE Id = ?")
            .bind(id)
            .fetch_optional(pool)
            .await?;

    Ok(row.and_then(|(json,)| serde_json::from_str(&json).ok()))
}

pub async fn kv_set<T: serde::Serialize>(
    pool: &SqlitePool,
    id: &str,
    model: &T,
) -> Result<(), sqlx::Error> {
    let json = serde_json::to_string(model).unwrap_or_default();
    sqlx::query("INSERT OR REPLACE INTO Kv (Id, DataJson) VALUES (?, ?)")
        .bind(id)
        .bind(&json)
        .execute(pool)
        .await?;
    Ok(())
}

pub async fn kv_remove(pool: &SqlitePool, id: &str) -> Result<(), sqlx::Error> {
    sqlx::query("DELETE FROM Kv WHERE Id = ?")
        .bind(id)
        .execute(pool)
        .await?;
    Ok(())
}

pub async fn kv_exists(pool: &SqlitePool, id: &str) -> Result<bool, sqlx::Error> {
    let count: (i64,) = sqlx::query_as("SELECT COUNT(*) FROM Kv WHERE Id = ?")
        .bind(id)
        .fetch_one(pool)
        .await?;
    Ok(count.0 > 0)
}

// --- Sessions ---

pub async fn get_session_by_id(
    pool: &SqlitePool,
    id: &str,
) -> Result<Option<Session>, sqlx::Error> {
    let entity: Option<SessionEntity> =
        sqlx::query_as("SELECT * FROM Sessions WHERE Id = ?")
            .bind(id)
            .fetch_optional(pool)
            .await?;
    Ok(entity.map(Session::from_entity))
}

pub async fn get_session(
    pool: &SqlitePool,
    id: &str,
    id2: &str,
) -> Result<Option<Session>, sqlx::Error> {
    let session = get_session_by_id(pool, id).await?;
    Ok(session.filter(|s| s.id_verification == id2))
}

pub async fn add_or_update_session(
    pool: &SqlitePool,
    session: &Session,
) -> Result<bool, sqlx::Error> {
    let connection_ids_json = serde_json::to_string(&session.connection_ids).unwrap_or_default();
    let rows = sqlx::query(
        "INSERT OR REPLACE INTO Sessions (Id, IdVerification, DateCreated, IsLiteSession, CleanupJobId, ConnectionIdsJson) \
         VALUES (?, ?, ?, ?, ?, ?)",
    )
    .bind(&session.id)
    .bind(&session.id_verification)
    .bind(&session.date_created)
    .bind(session.is_lite_session)
    .bind(&session.cleanup_job_id)
    .bind(&connection_ids_json)
    .execute(pool)
    .await?;
    Ok(rows.rows_affected() > 0)
}

pub async fn delete_session(pool: &SqlitePool, id: &str) -> Result<(), sqlx::Error> {
    sqlx::query("DELETE FROM Sessions WHERE Id = ?")
        .bind(id)
        .execute(pool)
        .await?;
    Ok(())
}

// --- Messages ---

pub async fn get_message_by_id(
    pool: &SqlitePool,
    id: &str,
) -> Result<Option<Message>, sqlx::Error> {
    sqlx::query_as("SELECT * FROM Messages WHERE Id = ?")
        .bind(id)
        .fetch_optional(pool)
        .await
}

pub async fn get_messages_by_session(
    pool: &SqlitePool,
    session_id: &str,
    from_epoch: i64,
) -> Result<Vec<Message>, sqlx::Error> {
    sqlx::query_as(
        "SELECT * FROM Messages WHERE SessionId = ? AND DateSentEpoch > ? ORDER BY DateSent DESC",
    )
    .bind(session_id)
    .bind(from_epoch)
    .fetch_all(pool)
    .await
}

pub async fn insert_message(pool: &SqlitePool, msg: &Message) -> Result<(), sqlx::Error> {
    sqlx::query(
        "INSERT INTO Messages \
         (Id, SessionId, SessionIdVerification, SocketConnectionId, EncryptionPublicKeyAlias, \
          Text, FileName, DateSent, HasFile, FileSizeBytes, IsNotification, DateSentEpoch) \
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
    )
    .bind(&msg.id)
    .bind(&msg.session_id)
    .bind(&msg.session_id_verification)
    .bind(&msg.socket_connection_id)
    .bind(&msg.encryption_public_key_alias)
    .bind(&msg.text)
    .bind(&msg.file_name)
    .bind(&msg.date_sent)
    .bind(msg.has_file)
    .bind(msg.file_size_bytes)
    .bind(msg.is_notification)
    .bind(msg.date_sent_epoch)
    .execute(pool)
    .await?;
    Ok(())
}

pub async fn delete_all_messages_by_session(
    pool: &SqlitePool,
    session_id: &str,
) -> Result<u64, sqlx::Error> {
    let result = sqlx::query("DELETE FROM Messages WHERE SessionId = ?")
        .bind(session_id)
        .execute(pool)
        .await?;
    Ok(result.rows_affected())
}

// --- Connection tracking ---

pub async fn track_client(
    pool: &SqlitePool,
    session_id: &str,
    connection_id: &str,
) -> Result<bool, sqlx::Error> {
    if !add_connection_id(pool, session_id, connection_id).await? {
        return Ok(false);
    }
    kv_set(
        pool,
        connection_id,
        &SessionMetaByConnectionId {
            session_id: session_id.to_string(),
        },
    )
    .await?;
    Ok(true)
}

pub async fn add_connection_id(
    pool: &SqlitePool,
    session_id: &str,
    connection_id: &str,
) -> Result<bool, sqlx::Error> {
    let Some(mut session) = get_session_by_id(pool, session_id).await? else {
        return Ok(false);
    };
    if !session.connection_ids.contains(&connection_id.to_string()) {
        session.connection_ids.push(connection_id.to_string());
    }
    add_or_update_session(pool, &session).await
}

pub async fn untrack_client_return_session_id(
    pool: &SqlitePool,
    connection_id: &str,
) -> Result<Option<String>, sqlx::Error> {
    let Some(meta) = kv_get::<SessionMetaByConnectionId>(pool, connection_id).await? else {
        return Ok(None);
    };
    let Some(mut session) = get_session_by_id(pool, &meta.session_id).await? else {
        return Ok(None);
    };
    session
        .connection_ids
        .retain(|id| id != connection_id);
    add_or_update_session(pool, &session).await?;
    kv_remove(pool, connection_id).await?;
    Ok(Some(meta.session_id))
}

pub async fn find_client(
    pool: &SqlitePool,
    session_id: &str,
) -> Result<Vec<String>, sqlx::Error> {
    let session = get_session_by_id(pool, session_id).await?;
    Ok(session.map(|s| s.connection_ids).unwrap_or_default())
}

// --- Share tokens ---

pub async fn create_new_share_token(
    pool: &SqlitePool,
    session_id: &str,
) -> Result<i64, sqlx::Error> {
    let mut min: i64 = 100_000;
    let mut max: i64 = 1_000_000;
    let mut tries = 0;

    let mut number: i64 = {
        let mut rng = rand::rng();
        rng.random_range(min..max)
    };
    while kv_exists(pool, &number.to_string()).await? {
        if tries > (max - min) / 2 {
            tries = 0;
            min *= 10;
            max *= 10;
        }
        tries += 1;
        number = {
            let mut rng = rand::rng();
            rng.random_range(min..max)
        };
    }

    let token = ShareToken {
        id: number,
        session_id: session_id.to_string(),
    };
    kv_set(pool, &number.to_string(), &token).await?;
    kv_set(
        pool,
        session_id,
        &SessionShareToken { token: number },
    )
    .await?;
    Ok(number)
}

// --- Public keys ---

pub async fn save_public_key(
    pool: &SqlitePool,
    session_id: &str,
    alias: &str,
    public_key_json: &str,
) -> Result<String, sqlx::Error> {
    let id = new_guid();
    let now = chrono::Utc::now().format("%Y-%m-%dT%H:%M:%S").to_string();
    sqlx::query(
        "INSERT INTO PublicKeys (Id, SessionId, Alias, PublicKeyJson, DateCreated) VALUES (?, ?, ?, ?, ?)",
    )
    .bind(&id)
    .bind(session_id)
    .bind(alias)
    .bind(public_key_json)
    .bind(&now)
    .execute(pool)
    .await?;
    Ok(id)
}

pub async fn get_public_key_by_id(
    pool: &SqlitePool,
    id: &str,
) -> Result<Option<PublicKey>, sqlx::Error> {
    sqlx::query_as("SELECT * FROM PublicKeys WHERE Id = ?")
        .bind(id)
        .fetch_optional(pool)
        .await
}

pub async fn delete_public_keys_by_session(
    pool: &SqlitePool,
    session_id: &str,
) -> Result<(), sqlx::Error> {
    sqlx::query("DELETE FROM PublicKeys WHERE SessionId = ?")
        .bind(session_id)
        .execute(pool)
        .await?;
    Ok(())
}
