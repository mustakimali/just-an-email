use std::sync::Arc;

use dashmap::DashMap;
use sqlx::SqlitePool;
use tokio::sync::broadcast;

use crate::config::Config;

#[derive(Clone)]
pub struct AppState {
    pub db: SqlitePool,
    pub config: Config,
    pub conversations: Arc<ConversationState>,
    pub secure_lines: Arc<SecureLineState>,
    pub stats_cache: Arc<tokio::sync::RwLock<Option<StatsCache>>>,
}

pub struct StatsCache {
    pub data: serde_json::Value,
    pub expires_at: chrono::DateTime<chrono::Utc>,
}

pub struct ConversationState {
    pub connections: DashMap<String, ConversationConnection>,
    pub session_notifiers: DashMap<String, broadcast::Sender<WsNotification>>,
}

pub struct ConversationConnection {
    pub session_id: String,
    pub tx: broadcast::Sender<WsNotification>,
}

#[derive(Clone, Debug)]
pub enum WsNotification {
    RequestReloadMessage,
    ShowSharePanel { token: String },
    HideSharePanel,
    SessionDeleted,
    SetNumberOfDevices { count: usize },
    StartKeyExchange {
        peer_id: String,
        pka: String,
        initiate: bool,
    },
    Callback {
        method: String,
        data: String,
    },
    Connected {
        connection_id: String,
    },
}

pub struct SecureLineState {
    pub connections: DashMap<String, String>,
    pub sessions: DashMap<String, Vec<String>>,
    pub session_notifiers: DashMap<String, broadcast::Sender<SecureLineNotification>>,
    pub messages: DashMap<String, String>,
}

#[derive(Clone, Debug)]
pub enum SecureLineNotification {
    Broadcast {
        event: String,
        data: Option<String>,
    },
    StartKeyExchange {
        peer_id: String,
        pka: String,
        initiate: bool,
    },
    Callback {
        method: String,
        data: String,
    },
}

impl ConversationState {
    pub fn new() -> Self {
        Self {
            connections: DashMap::new(),
            session_notifiers: DashMap::new(),
        }
    }
}

impl SecureLineState {
    pub fn new() -> Self {
        Self {
            connections: DashMap::new(),
            sessions: DashMap::new(),
            session_notifiers: DashMap::new(),
            messages: DashMap::new(),
        }
    }
}
