use axum::{
    extract::{State, WebSocketUpgrade, ws},
    response::IntoResponse,
};
use futures::{SinkExt, StreamExt};
use tokio::sync::broadcast;
use uuid::Uuid;

use crate::db::stats_db;
use crate::state::{AppState, SecureLineNotification};
use crate::ws::protocol::{SecureLineClientMessage, SecureLineServerMessage};

pub async fn ws_handler(
    ws: WebSocketUpgrade,
    State(state): State<AppState>,
) -> impl IntoResponse {
    ws.on_upgrade(|socket| handle_socket(socket, state))
}

async fn handle_socket(socket: ws::WebSocket, state: AppState) {
    let (mut sender, mut receiver) = socket.split();
    let connection_id = Uuid::new_v4().simple().to_string();
    let mut session_id: Option<String> = None;

    let _ = stats_db::record_stats(&state.db, stats_db::RecordType::Device, 1).await;

    let (tx, _) = broadcast::channel::<SecureLineNotification>(64);
    let mut rx = tx.subscribe();

    let send_task = tokio::spawn(async move {
        while let Ok(notification) = rx.recv().await {
            let msg = match notification {
                SecureLineNotification::Broadcast { event, data } => {
                    SecureLineServerMessage::Broadcast { event, data }
                }
                SecureLineNotification::StartKeyExchange {
                    peer_id,
                    pka,
                    initiate,
                } => SecureLineServerMessage::StartKeyExchange {
                    peer_id,
                    pka,
                    initiate,
                },
                SecureLineNotification::Callback { method, data } => {
                    SecureLineServerMessage::Callback { method, data }
                }
            };

            let json = serde_json::to_string(&msg).unwrap_or_default();
            if sender.send(ws::Message::Text(json.into())).await.is_err() {
                break;
            }
        }
    });

    while let Some(Ok(msg)) = receiver.next().await {
        let text = match msg {
            ws::Message::Text(t) => t.to_string(),
            ws::Message::Close(_) => break,
            _ => continue,
        };

        let client_msg: SecureLineClientMessage = match serde_json::from_str(&text) {
            Ok(m) => m,
            Err(_) => continue,
        };

        match client_msg {
            SecureLineClientMessage::Init { id } => {
                session_id = Some(id.clone());
                state
                    .secure_lines
                    .connections
                    .insert(connection_id.clone(), id.clone());

                let mut entry = state
                    .secure_lines
                    .sessions
                    .entry(id.clone())
                    .or_insert_with(Vec::new);

                if entry.len() >= 2 {
                    continue;
                }

                if !entry.contains(&connection_id) {
                    entry.push(connection_id.clone());
                }

                state
                    .secure_lines
                    .session_notifiers
                    .entry(id.clone())
                    .or_insert_with(|| tx.clone());

                if entry.len() > 1 {
                    let _ = stats_db::record_stats(
                        &state.db,
                        stats_db::RecordType::Message,
                        2,
                    )
                    .await;
                    let _ = stats_db::record_stats(
                        &state.db,
                        stats_db::RecordType::Device,
                        1,
                    )
                    .await;

                    broadcast_to_session(
                        &state,
                        &id,
                        SecureLineNotification::Broadcast {
                            event: "Start".to_string(),
                            data: None,
                        },
                        None,
                    );

                    let first = entry[0].clone();
                    let second = entry[1].clone();
                    drop(entry);

                    init_key_exchange(&state, &first, &second);
                }
            }
            SecureLineClientMessage::Broadcast { event, data, all } => {
                let all = all.unwrap_or(false);
                let exclude = if all {
                    None
                } else {
                    Some(connection_id.as_str())
                };

                if let Some(ref sid) = session_id {
                    let msg_size =
                        event.len() as i64 + data.as_ref().map(|d| d.len()).unwrap_or(0) as i64;
                    let _ = stats_db::record_message_stats(&state.db, msg_size, None).await;

                    broadcast_to_session(
                        &state,
                        sid,
                        SecureLineNotification::Broadcast { event, data },
                        exclude,
                    );
                }
            }
            SecureLineClientMessage::CallPeer {
                peer_id: _,
                method,
                param,
            } => {
                if let Some(ref sid) = session_id {
                    let _ = stats_db::record_stats(
                        &state.db,
                        stats_db::RecordType::Message,
                        1,
                    )
                    .await;

                    broadcast_to_session(
                        &state,
                        sid,
                        SecureLineNotification::Callback {
                            method,
                            data: param,
                        },
                        Some(&connection_id),
                    );
                }
            }
        }
    }

    send_task.abort();

    if let Some(ref sid) = session_id {
        state.secure_lines.connections.remove(&connection_id);

        if let Some(mut entry) = state.secure_lines.sessions.get_mut(sid) {
            broadcast_to_session(
                &state,
                sid,
                SecureLineNotification::Broadcast {
                    event: "GONE".to_string(),
                    data: Some(String::new()),
                },
                Some(&connection_id),
            );
            entry.retain(|c| c != &connection_id);
        }
    }
}

fn broadcast_to_session(
    state: &AppState,
    session_id: &str,
    notification: SecureLineNotification,
    exclude: Option<&str>,
) {
    if let Some(clients) = state.secure_lines.sessions.get(session_id) {
        for cid in clients.iter() {
            if Some(cid.as_str()) == exclude {
                continue;
            }
            if let Some(_conn_sid) = state.secure_lines.connections.get(cid) {
                if let Some(notifier) = state.secure_lines.session_notifiers.get(session_id) {
                    let _ = notifier.send(notification.clone());
                    return;
                }
            }
        }
    }
}

fn init_key_exchange(state: &AppState, first_device: &str, new_device: &str) {
    let pka = Uuid::new_v4().simple().to_string();

    if let Some(notifier) = state
        .secure_lines
        .connections
        .get(first_device)
        .and_then(|sid| state.secure_lines.session_notifiers.get(sid.value()))
    {
        let _ = notifier.send(SecureLineNotification::StartKeyExchange {
            peer_id: new_device.to_string(),
            pka: pka.clone(),
            initiate: false,
        });
    }

    if let Some(notifier) = state
        .secure_lines
        .connections
        .get(new_device)
        .and_then(|sid| state.secure_lines.session_notifiers.get(sid.value()))
    {
        let _ = notifier.send(SecureLineNotification::StartKeyExchange {
            peer_id: first_device.to_string(),
            pka,
            initiate: true,
        });
    }
}
