use axum::{
    Json, Router,
    extract::State,
    http::StatusCode,
    response::IntoResponse,
    routing::post,
};
use serde::{Deserialize, Serialize};

use crate::db::{app_db, stats_db};
use crate::models::message::Message;
use crate::models::share_token::ShareToken;
use crate::services::{cleanup, helpers};
use crate::state::AppState;

#[derive(Deserialize)]
pub struct CreateSessionRequest {
    pub id: String,
    pub id2: String,
}

async fn create_session(
    State(state): State<AppState>,
    Json(req): Json<CreateSessionRequest>,
) -> impl IntoResponse {
    if req.id.len() != 32 || req.id2.len() != 32 {
        return StatusCode::BAD_REQUEST.into_response();
    }

    if let Ok(Some(_)) = app_db::get_session_by_id(&state.db, &req.id).await {
        return StatusCode::OK.into_response();
    }

    let session = crate::models::session::Session {
        id: req.id.clone(),
        id_verification: req.id2.clone(),
        date_created: helpers::now_iso(),
        is_lite_session: false,
        cleanup_job_id: None,
        connection_ids: vec![],
    };

    if let Err(e) = app_db::add_or_update_session(&state.db, &session).await {
        tracing::error!("Failed to create session: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let _ = app_db::create_new_share_token(&state.db, &req.id).await;
    let _ = stats_db::record_stats(&state.db, stats_db::RecordType::Session, 1).await;

    cleanup::spawn_session_cleanup(state.clone(), req.id.clone());

    StatusCode::ACCEPTED.into_response()
}

#[derive(Deserialize)]
pub struct PostMessageRequest {
    #[serde(rename = "SessionId")]
    pub session_id: String,
    #[serde(rename = "SessionVerification")]
    pub session_verification: Option<String>,
    #[serde(rename = "SocketConnectionId")]
    pub socket_connection_id: Option<String>,
    #[serde(rename = "EncryptionPublicKeyAlias")]
    pub encryption_public_key_alias: Option<String>,
    #[serde(rename = "ComposerText")]
    pub composer_text: String,
}

async fn post_message(
    State(state): State<AppState>,
    Json(req): Json<PostMessageRequest>,
) -> impl IntoResponse {
    let _session = match app_db::get_session_by_id(&state.db, &req.session_id).await {
        Ok(Some(s)) => s,
        _ => return StatusCode::BAD_REQUEST.into_response(),
    };

    let now = helpers::now_iso();
    let epoch = helpers::to_epoch();
    let msg = Message {
        id: app_db::new_guid(),
        session_id: req.session_id.clone(),
        session_id_verification: req.session_verification,
        socket_connection_id: req.socket_connection_id,
        encryption_public_key_alias: req.encryption_public_key_alias,
        text: req.composer_text,
        file_name: None,
        date_sent: now,
        has_file: false,
        file_size_bytes: None,
        is_notification: false,
        date_sent_epoch: epoch,
    };

    if let Err(e) = app_db::insert_message(&state.db, &msg).await {
        tracing::error!("Failed to insert message: {}", e);
        return StatusCode::INTERNAL_SERVER_ERROR.into_response();
    }

    let _ = stats_db::record_message_stats(&state.db, msg.text.len() as i64, None).await;

    notify_reload_message(&state, &msg.session_id);

    StatusCode::ACCEPTED.into_response()
}

pub fn notify_reload_message(state: &AppState, session_id: &str) {
    if let Some(tx) = state.conversations.session_notifiers.get(session_id) {
        let _ = tx.send(crate::state::WsNotification::RequestReloadMessage);
    }
}

#[derive(Deserialize)]
pub struct GetMessagesRequest {
    pub id: String,
    pub id2: String,
    pub from: Option<i64>,
}

async fn get_messages(
    State(state): State<AppState>,
    Json(req): Json<GetMessagesRequest>,
) -> impl IntoResponse {
    let _session = match app_db::get_session(&state.db, &req.id, &req.id2).await {
        Ok(Some(s)) => s,
        _ => return Json(Vec::<Message>::new()).into_response(),
    };

    let from = req.from.unwrap_or(-1);
    match app_db::get_messages_by_session(&state.db, &req.id, from).await {
        Ok(msgs) => Json(msgs).into_response(),
        Err(_) => Json(Vec::<Message>::new()).into_response(),
    }
}

#[derive(Deserialize)]
pub struct GetMessageRawRequest {
    #[serde(rename = "messageId")]
    pub message_id: String,
    #[serde(rename = "sessionId")]
    pub session_id: String,
}

#[derive(Serialize)]
pub struct GetMessageRawResponse {
    #[serde(rename = "Content")]
    pub content: String,
}

async fn get_message_raw(
    State(state): State<AppState>,
    Json(req): Json<GetMessageRawRequest>,
) -> impl IntoResponse {
    let msg = match app_db::get_message_by_id(&state.db, &req.message_id).await {
        Ok(Some(m)) if m.session_id == req.session_id => m,
        _ => return StatusCode::NOT_FOUND.into_response(),
    };

    Json(GetMessageRawResponse {
        content: msg.text,
    })
    .into_response()
}

#[derive(Deserialize)]
pub struct SaveKeyRequest {
    #[serde(rename = "sessionId")]
    pub session_id: String,
    #[serde(rename = "sessionVerification")]
    pub session_verification: String,
    #[serde(rename = "alias")]
    pub alias: String,
    #[serde(rename = "publicKey")]
    pub public_key: String,
}

async fn save_public_key(
    State(state): State<AppState>,
    Json(req): Json<SaveKeyRequest>,
) -> impl IntoResponse {
    if req.session_id.is_empty()
        || req.session_verification.is_empty()
        || req.alias.is_empty()
        || req.public_key.is_empty()
    {
        return StatusCode::BAD_REQUEST.into_response();
    }

    let parsed: serde_json::Value = match serde_json::from_str(&req.public_key) {
        Ok(v) => v,
        Err(_) => {
            return (
                StatusCode::BAD_REQUEST,
                Json(serde_json::json!({"error": "Invalid public key format"})),
            )
                .into_response()
        }
    };

    if parsed.get("kty").and_then(|v| v.as_str()) != Some("RSA")
        || parsed.get("n").is_none()
        || parsed.get("e").is_none()
    {
        return (
            StatusCode::BAD_REQUEST,
            Json(serde_json::json!({"error": "Invalid RSA public key"})),
        )
            .into_response();
    }

    let _session = match app_db::get_session_by_id(&state.db, &req.session_id).await {
        Ok(Some(s)) if s.id_verification == req.session_verification => s,
        _ => return StatusCode::NOT_FOUND.into_response(),
    };

    match app_db::save_public_key(&state.db, &req.session_id, &req.alias, &req.public_key).await {
        Ok(id) => Json(serde_json::json!({"id": id})).into_response(),
        Err(e) => {
            tracing::error!("Failed to save public key: {}", e);
            StatusCode::INTERNAL_SERVER_ERROR.into_response()
        }
    }
}

#[derive(Deserialize)]
pub struct ConnectRequest {
    #[serde(rename = "Token")]
    pub token: Option<i64>,
    #[serde(rename = "token")]
    pub token_alt: Option<i64>,
}

async fn connect_via_pin(
    State(state): State<AppState>,
    Json(req): Json<ConnectRequest>,
) -> impl IntoResponse {
    let token = req.token.or(req.token_alt).unwrap_or(0);
    if token == 0 {
        return (
            StatusCode::BAD_REQUEST,
            Json(serde_json::json!({"error": "Invalid PIN"})),
        )
            .into_response();
    }

    let share_token = match app_db::kv_get::<ShareToken>(&state.db, &token.to_string()).await {
        Ok(Some(t)) => t,
        _ => {
            return (
                StatusCode::BAD_REQUEST,
                Json(serde_json::json!({"error": "Invalid PIN!"})),
            )
                .into_response()
        }
    };

    let session = match app_db::get_session_by_id(&state.db, &share_token.session_id).await {
        Ok(Some(s)) => s,
        _ => {
            return (
                StatusCode::BAD_REQUEST,
                Json(serde_json::json!({"error": "The Session does not exist!"})),
            )
                .into_response()
        }
    };

    let _ = app_db::kv_remove(&state.db, &token.to_string()).await;

    if let Some(tx) = state
        .conversations
        .session_notifiers
        .get(&session.id)
    {
        let _ = tx.send(crate::state::WsNotification::HideSharePanel);
    }

    Json(serde_json::json!({
        "sessionId": session.id,
        "sessionVerification": session.id_verification,
        "isLiteSession": session.is_lite_session
    }))
    .into_response()
}

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/app/new", post(create_session))
        .route("/api/app/post", post(post_message))
        .route("/api/app/messages", post(get_messages))
        .route("/api/app/message-raw", post(get_message_raw))
        .route("/api/app/key", post(save_public_key))
        .route("/api/app/connect", post(connect_via_pin))
}
