use std::sync::Arc;

use sqlx::sqlite::SqlitePoolOptions;
use tower_http::cors::CorsLayer;
use tracing_subscriber::{layer::SubscriberExt, util::SubscriberInitExt};

mod config;
mod db;
mod models;
mod routes;
mod services;
mod state;
mod ws;

use config::Config;
use state::{AppState, ConversationState, SecureLineState};

#[derive(rust_embed::RustEmbed)]
#[folder = "frontend/dist"]
struct FrontendAssets;

#[tokio::main]
async fn main() {
    tracing_subscriber::registry()
        .with(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| "just_an_email=info,tower_http=info".into()),
        )
        .with(tracing_subscriber::fmt::layer())
        .init();

    let config = Config::from_env();

    tokio::fs::create_dir_all(&config.data_dir).await.ok();
    tokio::fs::create_dir_all(&config.upload_dir).await.ok();

    let db_url = format!("sqlite:{}?mode=rwc", config.db_path.display());
    let pool = SqlitePoolOptions::new()
        .max_connections(5)
        .connect(&db_url)
        .await
        .expect("Failed to connect to database");

    db::schema::run_migrations(&pool)
        .await
        .expect("Failed to run migrations");

    tracing::info!("Database migrations complete");

    let state = AppState {
        db: pool,
        config: config.clone(),
        conversations: Arc::new(ConversationState::new()),
        secure_lines: Arc::new(SecureLineState::new()),
        stats_cache: Arc::new(tokio::sync::RwLock::new(None)),
    };

    let cors = CorsLayer::very_permissive();

    let app = routes::build_router(state)
        .fallback(spa_fallback)
        .layer(cors)
        .layer(tower_http::compression::CompressionLayer::new());

    let addr = format!("0.0.0.0:{}", config.port);
    tracing::info!("Starting server on {}", addr);

    let listener = tokio::net::TcpListener::bind(&addr)
        .await
        .expect("Failed to bind");

    axum::serve(listener, app)
        .await
        .expect("Server failed");
}

async fn spa_fallback(uri: axum::http::Uri) -> impl axum::response::IntoResponse {
    let path = uri.path().trim_start_matches('/');

    if let Some(file) = FrontendAssets::get(path) {
        let mime = mime_guess::from_path(path).first_or_octet_stream();
        return (
            axum::http::StatusCode::OK,
            [(axum::http::header::CONTENT_TYPE, mime.as_ref().to_string())],
            file.data.into_owned(),
        )
            .into_response();
    }

    if let Some(index) = FrontendAssets::get("index.html") {
        return (
            axum::http::StatusCode::OK,
            [(
                axum::http::header::CONTENT_TYPE,
                "text/html".to_string(),
            )],
            index.data.into_owned(),
        )
            .into_response();
    }

    (
        axum::http::StatusCode::NOT_FOUND,
        "Not Found",
    )
        .into_response()
}

use axum::response::IntoResponse;
