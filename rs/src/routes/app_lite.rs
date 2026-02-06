use axum::{
    Json, Router,
    extract::State,
    http::StatusCode,
    response::IntoResponse,
    routing::post,
};
use serde::{Deserialize, Serialize};

use crate::db::app_db;
use crate::models::message::Message;
use crate::models::share_token::SessionShareToken;
use crate::services::{cleanup, helpers};
use crate::state::AppState;

#[derive(Deserialize)]
pub struct LitePollRequest {
    pub id: String,
    pub id2: String,
    pub from: Option<i64>,
}

#[derive(Serialize)]
pub struct LitePollResponse {
    #[serde(rename = "hasSession")]
    pub has_session: bool,
    #[serde(rename = "hasToken")]
    pub has_token: bool,
    #[serde(rename = "token")]
    pub token: Option<String>,
    #[serde(rename = "messages")]
    pub messages: Vec<Message>,
}

async fn lite_poll(
    State(state): State<AppState>,
    Json(req): Json<LitePollRequest>,
) -> impl IntoResponse {
    let session_valid = match app_db::get_session(&state.db, &req.id, &req.id2).await {
        Ok(Some(_)) => true,
        _ => false,
    };

    if !session_valid {
        return Json(LitePollResponse {
            has_session: false,
            has_token: false,
            token: None,
            messages: vec![],
        })
        .into_response();
    }

    let token = app_db::kv_get::<SessionShareToken>(&state.db, &req.id)
        .await
        .ok()
        .flatten();

    let from = req.from.unwrap_or(-1);
    let messages = app_db::get_messages_by_session(&state.db, &req.id, from)
        .await
        .unwrap_or_default();

    Json(LitePollResponse {
        has_session: true,
        has_token: token.is_some(),
        token: token.map(|t| helpers::format_token(t.token)),
        messages,
    })
    .into_response()
}

#[derive(Deserialize)]
pub struct LiteSessionRequest {
    #[serde(rename = "SessionId")]
    pub session_id: String,
    #[serde(rename = "SessionVerification")]
    pub session_verification: String,
}

async fn lite_share_token_new(
    State(state): State<AppState>,
    Json(req): Json<LiteSessionRequest>,
) -> impl IntoResponse {
    match app_db::get_session(&state.db, &req.session_id, &req.session_verification).await {
        Ok(Some(_)) => {}
        _ => return StatusCode::BAD_REQUEST.into_response(),
    }

    match app_db::create_new_share_token(&state.db, &req.session_id).await {
        Ok(token) => Json(serde_json::json!({"token": helpers::format_token(token)})).into_response(),
        Err(_) => StatusCode::INTERNAL_SERVER_ERROR.into_response(),
    }
}

async fn lite_share_token_cancel(
    State(state): State<AppState>,
    Json(req): Json<LiteSessionRequest>,
) -> impl IntoResponse {
    match app_db::get_session(&state.db, &req.session_id, &req.session_verification).await {
        Ok(Some(_)) => {}
        _ => return StatusCode::BAD_REQUEST.into_response(),
    }

    if let Ok(Some(sst)) = app_db::kv_get::<SessionShareToken>(&state.db, &req.session_id).await {
        let _ = app_db::kv_remove(&state.db, &sst.token.to_string()).await;
        let _ = app_db::kv_remove(&state.db, &req.session_id).await;
    }

    StatusCode::OK.into_response()
}

async fn lite_erase_session(
    State(state): State<AppState>,
    Json(req): Json<LiteSessionRequest>,
) -> impl IntoResponse {
    match app_db::get_session(&state.db, &req.session_id, &req.session_verification).await {
        Ok(Some(_)) => {}
        _ => return StatusCode::BAD_REQUEST.into_response(),
    }

    let _cids = cleanup::erase_session_return_connection_ids(&state.db, &state, &req.session_id).await;

    if let Some(tx) = state.conversations.session_notifiers.get(&req.session_id) {
        let _ = tx.send(crate::state::WsNotification::SessionDeleted);
    }

    StatusCode::OK.into_response()
}

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/app/lite/poll", post(lite_poll))
        .route("/api/app/lite/share-token/new", post(lite_share_token_new))
        .route(
            "/api/app/lite/share-token/cancel",
            post(lite_share_token_cancel),
        )
        .route("/api/app/lite/erase-session", post(lite_erase_session))
}
