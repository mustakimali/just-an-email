use axum::{
    Json, Router,
    extract::{Query, State},
    http::StatusCode,
    response::IntoResponse,
    routing::{get, post},
};
use serde::Deserialize;

use crate::state::AppState;

#[derive(Deserialize)]
pub struct PostMessageRequest {
    #[serde(rename = "Id")]
    pub id: Option<String>,
    #[serde(rename = "Data")]
    pub data: Option<String>,
}

async fn post_message(
    State(state): State<AppState>,
    Json(req): Json<PostMessageRequest>,
) -> impl IntoResponse {
    let id = match &req.id {
        Some(id) if !id.is_empty() => id.clone(),
        _ => return StatusCode::BAD_REQUEST.into_response(),
    };
    let data = match &req.data {
        Some(d) if !d.is_empty() => d.clone(),
        _ => return StatusCode::BAD_REQUEST.into_response(),
    };

    state.secure_lines.messages.insert(id.clone(), data);

    (StatusCode::CREATED, Json(serde_json::json!(id))).into_response()
}

#[derive(Deserialize)]
pub struct GetMessageQuery {
    pub id: String,
}

async fn get_message(
    State(state): State<AppState>,
    Query(query): Query<GetMessageQuery>,
) -> impl IntoResponse {
    match state.secure_lines.messages.remove(&query.id) {
        Some((_, data)) => (
            StatusCode::OK,
            [("content-type", "application/json")],
            data,
        )
            .into_response(),
        None => StatusCode::NOT_FOUND.into_response(),
    }
}

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/secure-line/message", post(post_message))
        .route("/api/secure-line/message", get(get_message))
}
