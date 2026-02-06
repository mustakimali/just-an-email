use axum::{Router, routing::get};

use crate::state::AppState;

async fn health_check() -> &'static str {
    "Healthy"
}

pub fn routes() -> Router<AppState> {
    Router::new().route("/api/test", get(health_check))
}
