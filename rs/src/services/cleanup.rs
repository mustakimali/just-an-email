use sqlx::SqlitePool;
use tracing::info;

use crate::db::app_db;
use crate::models::share_token::SessionShareToken;
use crate::services::helpers::get_upload_folder;
use crate::state::AppState;

pub async fn erase_session_return_connection_ids(
    pool: &SqlitePool,
    state: &AppState,
    session_id: &str,
) -> Vec<String> {
    let upload_folder = get_upload_folder(session_id, &state.config.upload_dir);
    if upload_folder.exists() {
        if let Err(e) = tokio::fs::remove_dir_all(&upload_folder).await {
            tracing::warn!("Failed to delete upload folder {:?}: {}", upload_folder, e);
        } else {
            info!("Deleted upload folder: {:?}", upload_folder);
        }
    }

    let session = match app_db::get_session_by_id(pool, session_id).await {
        Ok(Some(s)) => s,
        _ => return vec![],
    };

    let count = app_db::delete_all_messages_by_session(pool, session_id)
        .await
        .unwrap_or(0);
    info!("Deleted {} messages for session {}", count, session_id);

    let _ = app_db::delete_public_keys_by_session(pool, session_id).await;

    for cid in &session.connection_ids {
        let _ = app_db::kv_remove(pool, cid).await;
    }

    if let Ok(Some(sst)) = app_db::kv_get::<SessionShareToken>(pool, session_id).await {
        let _ = app_db::kv_remove(pool, &sst.token.to_string()).await;
        let _ = app_db::kv_remove(pool, session_id).await;
    }

    let connection_ids = session.connection_ids.clone();

    let _ = app_db::delete_session(pool, session_id).await;

    connection_ids
}

pub fn spawn_session_cleanup(state: AppState, session_id: String) {
    let ttl_hours = state.config.session_ttl_hours;
    tokio::spawn(async move {
        tokio::time::sleep(tokio::time::Duration::from_secs(ttl_hours * 3600)).await;
        info!("TTL cleanup triggered for session {}", session_id);
        erase_session_return_connection_ids(&state.db, &state, &session_id).await;
    });
}
