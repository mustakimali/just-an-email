use axum::{
    Json, Router,
    extract::State,
    response::IntoResponse,
    routing::get,
};

use crate::db::stats_db;
use crate::state::AppState;

async fn stats_raw(State(state): State<AppState>) -> impl IntoResponse {
    {
        let cache = state.stats_cache.read().await;
        if let Some(ref c) = *cache {
            if chrono::Utc::now() < c.expires_at {
                return Json(c.data.clone()).into_response();
            }
        }
    }

    match stats_db::get_all_stats(&state.db).await {
        Ok(data) => {
            let json = serde_json::to_value(&data).unwrap_or_default();
            let mut cache = state.stats_cache.write().await;
            *cache = Some(crate::state::StatsCache {
                data: json.clone(),
                expires_at: chrono::Utc::now() + chrono::Duration::hours(1),
            });
            Json(json).into_response()
        }
        Err(e) => {
            tracing::error!("Failed to get stats: {}", e);
            Json(serde_json::json!([])).into_response()
        }
    }
}

pub fn routes() -> Router<AppState> {
    Router::new().route("/api/stats/raw", get(stats_raw))
}
