use axum::{
    Json, Router,
    extract::{Multipart, Path, State},
    http::{StatusCode, header},
    response::IntoResponse,
    routing::{get, post},
};

use crate::db::{app_db, stats_db};
use crate::models::message::Message;
use crate::routes::app::notify_reload_message;
use crate::services::{cleanup, helpers};
use crate::state::AppState;

async fn upload_file_stream(
    State(state): State<AppState>,
    mut multipart: Multipart,
) -> impl IntoResponse {
    let mut session_id = String::new();
    let mut session_verification = String::new();
    let mut socket_connection_id = String::new();
    let mut encryption_public_key_alias = String::new();
    let mut composer_text = String::new();
    let mut file_data: Option<Vec<u8>> = None;
    let mut file_name_from_field = String::new();

    while let Ok(Some(field)) = multipart.next_field().await {
        let name = field.name().unwrap_or("").to_string();
        match name.as_str() {
            "SessionId" => session_id = field.text().await.unwrap_or_default(),
            "SessionVerification" => session_verification = field.text().await.unwrap_or_default(),
            "SocketConnectionId" => socket_connection_id = field.text().await.unwrap_or_default(),
            "EncryptionPublicKeyAlias" => {
                encryption_public_key_alias = field.text().await.unwrap_or_default()
            }
            "ComposerText" => composer_text = field.text().await.unwrap_or_default(),
            "file" => {
                file_name_from_field = field
                    .file_name()
                    .unwrap_or("upload")
                    .to_string();
                match field.bytes().await {
                    Ok(bytes) => file_data = Some(bytes.to_vec()),
                    Err(e) => {
                        tracing::error!("Failed to read file: {}", e);
                        return StatusCode::BAD_REQUEST.into_response();
                    }
                }
            }
            _ => {}
        }
    }

    let Some(data) = file_data else {
        return StatusCode::BAD_REQUEST.into_response();
    };

    if data.len() as u64 > state.config.max_upload_size_bytes {
        return StatusCode::PAYLOAD_TOO_LARGE.into_response();
    }

    if session_id.is_empty() {
        return StatusCode::BAD_REQUEST.into_response();
    }

    if app_db::get_session_by_id(&state.db, &session_id)
        .await
        .ok()
        .flatten()
        .is_none()
    {
        return StatusCode::BAD_REQUEST.into_response();
    }

    let message_id = app_db::new_guid();
    let upload_dir = helpers::get_upload_folder(&session_id, &state.config.upload_dir);
    tokio::fs::create_dir_all(&upload_dir).await.ok();

    let disk_filename = if !encryption_public_key_alias.is_empty() {
        format!("{}.enc", message_id)
    } else {
        let original = if composer_text.is_empty() {
            file_name_from_field.clone()
        } else {
            composer_text.clone()
        };
        let dest = upload_dir.join(&original);
        if dest.exists() {
            let stem = std::path::Path::new(&original)
                .file_stem()
                .and_then(|s| s.to_str())
                .unwrap_or("file");
            let ext = std::path::Path::new(&original)
                .extension()
                .and_then(|s| s.to_str())
                .unwrap_or("");
            if ext.is_empty() {
                format!("{}_{}", stem, &message_id[..6])
            } else {
                format!("{}_{}.{}", stem, &message_id[..6], ext)
            }
        } else {
            original
        }
    };

    let dest_path = upload_dir.join(&disk_filename);
    if let Err(e) = tokio::fs::write(&dest_path, &data).await {
        tracing::error!("Failed to write file: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let text = if composer_text.is_empty() {
        file_name_from_field
    } else {
        composer_text
    };

    let msg = Message {
        id: message_id,
        session_id: session_id.clone(),
        session_id_verification: if session_verification.is_empty() {
            None
        } else {
            Some(session_verification)
        },
        socket_connection_id: if socket_connection_id.is_empty() {
            None
        } else {
            Some(socket_connection_id)
        },
        encryption_public_key_alias: if encryption_public_key_alias.is_empty() {
            None
        } else {
            Some(encryption_public_key_alias)
        },
        text,
        file_name: Some(disk_filename),
        date_sent: helpers::now_iso(),
        has_file: true,
        file_size_bytes: Some(data.len() as i64),
        is_notification: false,
        date_sent_epoch: helpers::to_epoch(),
    };

    if let Err(e) = app_db::insert_message(&state.db, &msg).await {
        tracing::error!("Failed to insert file message: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let _ = stats_db::record_message_stats(&state.db, msg.text.len() as i64, Some(data.len() as i64)).await;
    notify_reload_message(&state, &session_id);

    StatusCode::ACCEPTED.into_response()
}

async fn download_file(
    State(state): State<AppState>,
    Path((id, session_id)): Path<(String, String)>,
) -> impl IntoResponse {
    let msg = match app_db::get_message_by_id(&state.db, &id).await {
        Ok(Some(m)) if m.session_id == session_id => m,
        _ => return StatusCode::NOT_FOUND.into_response(),
    };

    let upload_dir = helpers::get_upload_folder(&session_id, &state.config.upload_dir);
    let disk_filename = msg.file_name.as_deref().unwrap_or(&msg.text);
    let path = upload_dir.join(disk_filename);

    if !path.exists() {
        return StatusCode::NOT_FOUND.into_response();
    }

    let body = match tokio::fs::read(&path).await {
        Ok(data) => data,
        Err(_) => return StatusCode::INTERNAL_SERVER_ERROR.into_response(),
    };

    (
        StatusCode::OK,
        [
            (header::CONTENT_TYPE, "application/octet-stream".to_string()),
            (
                header::CONTENT_DISPOSITION,
                format!("attachment; filename=\"{}\"", disk_filename),
            ),
        ],
        body,
    )
        .into_response()
}

async fn cli_upload(
    State(state): State<AppState>,
    Path(session_id): Path<String>,
    mut multipart: Multipart,
) -> impl IntoResponse {
    let session = match app_db::get_session_by_id(&state.db, &session_id).await {
        Ok(Some(s)) => s,
        _ => {
            return (
                StatusCode::BAD_REQUEST,
                Json(serde_json::json!({
                    "error": "Invalid Session, follow this redirect_uri to start a session in the browser first.",
                    "redirect_uri": format!("/app#{}{}", session_id, app_db::new_guid())
                })),
            )
                .into_response()
        }
    };

    if session.connection_ids.is_empty() {
        return (
            StatusCode::BAD_REQUEST,
            Json(serde_json::json!({
                "error": "No client is connected, the session may be lost. Try starting the session again or refreshing the browser window."
            })),
        )
            .into_response();
    }

    let mut file_data: Option<Vec<u8>> = None;
    let mut file_name = String::new();

    while let Ok(Some(field)) = multipart.next_field().await {
        if field.name() == Some("file") || field.file_name().is_some() {
            file_name = field
                .file_name()
                .unwrap_or("upload")
                .to_string();
            match field.bytes().await {
                Ok(bytes) => file_data = Some(bytes.to_vec()),
                Err(_) => return StatusCode::BAD_REQUEST.into_response(),
            }
        }
    }

    let Some(data) = file_data else {
        return (
            StatusCode::NOT_FOUND,
            Json(serde_json::json!({"error": "No file posted"})),
        )
            .into_response();
    };

    if data.len() as u64 > state.config.max_upload_size_bytes {
        return (
            StatusCode::PAYLOAD_TOO_LARGE,
            Json(serde_json::json!({"error": "The posted file is too large"})),
        )
            .into_response();
    }

    let message_id = app_db::new_guid();
    let upload_dir = helpers::get_upload_folder(&session_id, &state.config.upload_dir);
    tokio::fs::create_dir_all(&upload_dir).await.ok();

    let disk_filename = file_name.clone();
    let dest_path = upload_dir.join(&disk_filename);
    if let Err(e) = tokio::fs::write(&dest_path, &data).await {
        tracing::error!("Failed to write CLI file: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let file_len = data.len() as i64;
    let msg = Message {
        id: message_id.clone(),
        session_id: session_id.clone(),
        session_id_verification: None,
        socket_connection_id: None,
        encryption_public_key_alias: None,
        text: file_name.clone(),
        file_name: Some(disk_filename),
        date_sent: helpers::now_iso(),
        has_file: true,
        file_size_bytes: Some(file_len),
        is_notification: false,
        date_sent_epoch: helpers::to_epoch(),
    };

    if let Err(e) = app_db::insert_message(&state.db, &msg).await {
        tracing::error!("Failed to insert CLI message: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let _ = stats_db::record_message_stats(&state.db, file_name.len() as i64, Some(file_len)).await;
    notify_reload_message(&state, &session_id);

    Json(serde_json::json!({
        "session_id": session_id,
        "message_id": message_id,
        "file_name": file_name,
        "file_size": file_len,
        "download_url": format!("/api/app/file/{}/{}", message_id, session_id)
    }))
    .into_response()
}

async fn quick_upload(
    State(state): State<AppState>,
    mut multipart: Multipart,
) -> impl IntoResponse {
    let mut file_data: Option<Vec<u8>> = None;
    let mut file_name = String::new();

    while let Ok(Some(field)) = multipart.next_field().await {
        if field.file_name().is_some() {
            file_name = field.file_name().unwrap_or("upload").to_string();
            match field.bytes().await {
                Ok(bytes) => file_data = Some(bytes.to_vec()),
                Err(_) => return StatusCode::BAD_REQUEST.into_response(),
            }
        }
    }

    let Some(data) = file_data else {
        return StatusCode::BAD_REQUEST.into_response();
    };

    if data.len() as u64 > state.config.max_upload_size_bytes {
        return StatusCode::PAYLOAD_TOO_LARGE.into_response();
    }

    let id = app_db::new_guid();
    let id2 = app_db::new_guid();

    let session = crate::models::session::Session {
        id: id.clone(),
        id_verification: id2.clone(),
        date_created: helpers::now_iso(),
        is_lite_session: false,
        cleanup_job_id: None,
        connection_ids: vec![],
    };

    if let Err(e) = app_db::add_or_update_session(&state.db, &session).await {
        tracing::error!("Failed to create quick upload session: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let _ = stats_db::record_stats(&state.db, stats_db::RecordType::Session, 1).await;
    cleanup::spawn_session_cleanup(state.clone(), id.clone());

    let message_id = app_db::new_guid();
    let upload_dir = helpers::get_upload_folder(&id, &state.config.upload_dir);
    tokio::fs::create_dir_all(&upload_dir).await.ok();

    let disk_filename = file_name.clone();
    let dest_path = upload_dir.join(&disk_filename);
    if let Err(e) = tokio::fs::write(&dest_path, &data).await {
        tracing::error!("Failed to write quick upload file: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let file_len = data.len() as i64;
    let msg = Message {
        id: message_id.clone(),
        session_id: id.clone(),
        session_id_verification: Some(id2.clone()),
        socket_connection_id: Some(id.clone()),
        encryption_public_key_alias: None,
        text: file_name.clone(),
        file_name: Some(disk_filename),
        date_sent: helpers::now_iso(),
        has_file: true,
        file_size_bytes: Some(file_len),
        is_notification: false,
        date_sent_epoch: helpers::to_epoch(),
    };

    let _ = app_db::insert_message(&state.db, &msg).await;
    let _ = stats_db::record_message_stats(&state.db, file_name.len() as i64, Some(file_len)).await;

    Json(serde_json::json!({
        "session_id": id,
        "session_idv": id2,
        "message_id": message_id,
        "file_name": file_name,
        "file_size": file_len,
    }))
    .into_response()
}

async fn lite_upload(
    State(state): State<AppState>,
    multipart: Multipart,
) -> impl IntoResponse {
    upload_file_stream(State(state), multipart).await
}

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/app/post/files-stream", post(upload_file_stream))
        .route("/api/app/file/{id}/{sessionId}", get(download_file))
        .route("/api/f/{sessionId}", post(cli_upload))
        .route("/api/app/post/quick-upload", post(quick_upload))
        .route("/api/app/lite/post/files", post(lite_upload))
}
