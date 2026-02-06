pub mod conversation;
pub mod protocol;
pub mod secure_line;

use axum::{Router, routing::get};

use crate::state::AppState;

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/ws/conversation", get(conversation::ws_handler))
        .route("/ws/secure-line", get(secure_line::ws_handler))
}
