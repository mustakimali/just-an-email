use axum::Router;

use crate::state::AppState;

pub mod app;
pub mod app_lite;
pub mod files;
pub mod home;
pub mod public_key;
pub mod secure_line;
pub mod stats;

pub fn build_router(state: AppState) -> Router {
    Router::new()
        .merge(home::routes())
        .merge(app::routes())
        .merge(app_lite::routes())
        .merge(files::routes())
        .merge(public_key::routes())
        .merge(secure_line::routes())
        .merge(stats::routes())
        .merge(crate::ws::routes())
        .with_state(state)
}
