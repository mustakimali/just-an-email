use axum::{
    extract::{State, WebSocketUpgrade, ws},
    response::IntoResponse,
};
use futures::{SinkExt, StreamExt};
use tokio::sync::broadcast;
use uuid::Uuid;

use crate::db::{app_db, stats_db};
use crate::models::message::Message;
use crate::models::share_token::SessionShareToken;
use crate::services::{cleanup, helpers};
use crate::state::{AppState, ConversationConnection, WsNotification};
use crate::ws::protocol::{ClientMessage, ServerMessage};

pub async fn ws_handler(
    ws: WebSocketUpgrade,
    State(state): State<AppState>,
) -> impl IntoResponse {
    ws.on_upgrade(|socket| handle_socket(socket, state))
}

async fn handle_socket(socket: ws::WebSocket, state: AppState) {
    let (mut sender, mut receiver) = socket.split();

    let connection_id = Uuid::new_v4().simple().to_string();
    let (tx, _) = broadcast::channel::<WsNotification>(64);
    let mut session_id: Option<String> = None;

    let _ = stats_db::record_stats(&state.db, stats_db::RecordType::Device, 1).await;

    let _conn_id = connection_id.clone();
    let _state_clone = state.clone();

    let mut rx = tx.subscribe();
    let send_task = tokio::spawn(async move {
        while let Ok(notification) = rx.recv().await {
            let msg = match notification {
                WsNotification::RequestReloadMessage => ServerMessage::RequestReloadMessage,
                WsNotification::ShowSharePanel { token } => {
                    ServerMessage::ShowSharePanel { token }
                }
                WsNotification::HideSharePanel => ServerMessage::HideSharePanel,
                WsNotification::SessionDeleted => ServerMessage::SessionDeleted,
                WsNotification::SetNumberOfDevices { count } => {
                    ServerMessage::SetNumberOfDevices { count }
                }
                WsNotification::StartKeyExchange {
                    peer_id,
                    pka,
                    initiate,
                } => ServerMessage::StartKeyExchange {
                    peer_id,
                    pka,
                    initiate,
                },
                WsNotification::Callback { method, data } => {
                    ServerMessage::Callback { method, data }
                }
                WsNotification::Connected { connection_id } => {
                    ServerMessage::Connected { connection_id }
                }
            };

            let json = serde_json::to_string(&msg).unwrap_or_default();
            if sender.send(ws::Message::Text(json.into())).await.is_err() {
                break;
            }
        }
    });

    let _ = tx.send(WsNotification::Connected {
        connection_id: connection_id.clone(),
    });

    while let Some(Ok(msg)) = receiver.next().await {
        let text = match msg {
            ws::Message::Text(t) => t.to_string(),
            ws::Message::Close(_) => break,
            _ => continue,
        };

        let client_msg: ClientMessage = match serde_json::from_str(&text) {
            Ok(m) => m,
            Err(e) => {
                tracing::warn!("Invalid WS message: {} - {}", text, e);
                continue;
            }
        };

        match client_msg {
            ClientMessage::Connect { session_id: sid } => {
                session_id = Some(sid.clone());

                let _ = app_db::track_client(&state.db, &sid, &connection_id).await;

                state.conversations.connections.insert(
                    connection_id.clone(),
                    ConversationConnection {
                        session_id: sid.clone(),
                        tx: tx.clone(),
                    },
                );
                state
                    .conversations
                    .session_notifiers
                    .entry(sid.clone())
                    .or_insert_with(|| tx.clone());

                if let Ok(Some(sst)) =
                    app_db::kv_get::<SessionShareToken>(&state.db, &sid).await
                {
                    let _ = tx.send(WsNotification::ShowSharePanel {
                        token: helpers::format_token(sst.token),
                    });
                }

                let num_devices = app_db::find_client(&state.db, &sid)
                    .await
                    .map(|c| c.len())
                    .unwrap_or(0);

                broadcast_to_session(&state, &sid, WsNotification::SetNumberOfDevices {
                    count: num_devices,
                });

                if num_devices > 1 {
                    init_key_exchange(&state, &sid, &connection_id).await;
                    cancel_share_internal(&state, &sid).await;
                }
            }
            ClientMessage::CallPeer {
                peer_id,
                method,
                param,
            } => {
                if peer_id == "ALL" {
                    if let Some(ref sid) = session_id {
                        broadcast_to_session_except(
                            &state,
                            sid,
                            &connection_id,
                            WsNotification::Callback {
                                method: method.clone(),
                                data: param.clone(),
                            },
                        );

                        let num_devices = app_db::find_client(&state.db, sid)
                            .await
                            .map(|c| c.len())
                            .unwrap_or(0);

                        let mut notif_msg = "A new device connected.<br/><i class=\"fa fa-lock\"></i> Message is end to end encrypted.".to_string();
                        if num_devices == 2 {
                            notif_msg.push_str("<hr/><div class='text-info'>Frequently share data between these devices?<br/><span class='small'>Bookmark this page on each devices to quickly connect your devices.</span></div>");
                        }
                        add_session_notification(&state, sid, &notif_msg, &connection_id).await;
                    }
                } else {
                    if let Some(conn) = state.conversations.connections.get(&peer_id) {
                        let _ = conn.tx.send(WsNotification::Callback {
                            method,
                            data: param,
                        });
                    }
                }
            }
            ClientMessage::Share => {
                if let Some(ref sid) = session_id {
                    if let Ok(Some(sst)) =
                        app_db::kv_get::<SessionShareToken>(&state.db, sid).await
                    {
                        broadcast_to_session(
                            &state,
                            sid,
                            WsNotification::ShowSharePanel {
                                token: helpers::format_token(sst.token),
                            },
                        );
                    } else if let Ok(token) =
                        app_db::create_new_share_token(&state.db, sid).await
                    {
                        broadcast_to_session(
                            &state,
                            sid,
                            WsNotification::ShowSharePanel {
                                token: helpers::format_token(token),
                            },
                        );
                    }
                }
            }
            ClientMessage::CancelShare => {
                if let Some(ref sid) = session_id {
                    cancel_share_internal(&state, sid).await;
                }
            }
            ClientMessage::EraseSession => {
                if let Some(ref sid) = session_id {
                    let _cids =
                        cleanup::erase_session_return_connection_ids(&state.db, &state, sid)
                            .await;
                    broadcast_to_session(&state, sid, WsNotification::SessionDeleted);
                }
            }
        }
    }

    send_task.abort();

    if let Some(ref sid) = session_id {
        let _ = app_db::untrack_client_return_session_id(&state.db, &connection_id).await;
        state.conversations.connections.remove(&connection_id);

        if let Ok(Some(session)) = app_db::get_session_by_id(&state.db, sid).await {
            if session.is_lite_session {
                return;
            }
        }

        let num_devices = app_db::find_client(&state.db, sid)
            .await
            .map(|c| c.len())
            .unwrap_or(0);

        broadcast_to_session(
            &state,
            sid,
            WsNotification::SetNumberOfDevices {
                count: num_devices,
            },
        );

        if num_devices == 0 {
            let _cids =
                cleanup::erase_session_return_connection_ids(&state.db, &state, sid).await;
            broadcast_to_session(&state, sid, WsNotification::SessionDeleted);
        } else {
            add_session_notification(&state, sid, "A device was disconnected.", &connection_id)
                .await;
        }
    }
}

fn broadcast_to_session(state: &AppState, session_id: &str, notification: WsNotification) {
    for entry in state.conversations.connections.iter() {
        if entry.session_id == session_id {
            let _ = entry.tx.send(notification.clone());
        }
    }
}

fn broadcast_to_session_except(
    state: &AppState,
    session_id: &str,
    except_id: &str,
    notification: WsNotification,
) {
    for entry in state.conversations.connections.iter() {
        if entry.session_id == session_id && entry.key() != except_id {
            let _ = entry.tx.send(notification.clone());
        }
    }
}

async fn init_key_exchange(state: &AppState, session_id: &str, new_device_id: &str) {
    let clients = app_db::find_client(&state.db, session_id)
        .await
        .unwrap_or_default();

    let first_device_id = clients
        .iter()
        .find(|cid| cid.as_str() != new_device_id);

    let Some(first_id) = first_device_id else {
        return;
    };

    let pka = Uuid::new_v4().simple().to_string();

    if let Some(conn) = state.conversations.connections.get(first_id) {
        let _ = conn.tx.send(WsNotification::StartKeyExchange {
            peer_id: new_device_id.to_string(),
            pka: pka.clone(),
            initiate: false,
        });
    }

    if let Some(conn) = state.conversations.connections.get(new_device_id) {
        let _ = conn.tx.send(WsNotification::StartKeyExchange {
            peer_id: first_id.clone(),
            pka,
            initiate: true,
        });
    }
}

async fn cancel_share_internal(state: &AppState, session_id: &str) {
    if let Ok(Some(sst)) = app_db::kv_get::<SessionShareToken>(&state.db, session_id).await {
        let _ = app_db::kv_remove(&state.db, &sst.token.to_string()).await;
        let _ = app_db::kv_remove(&state.db, session_id).await;
        broadcast_to_session(state, session_id, WsNotification::HideSharePanel);
    }
}

async fn add_session_notification(
    state: &AppState,
    session_id: &str,
    message: &str,
    connection_id: &str,
) {
    let msg = Message {
        id: app_db::new_guid(),
        session_id: session_id.to_string(),
        session_id_verification: None,
        socket_connection_id: Some(connection_id.to_string()),
        encryption_public_key_alias: None,
        text: message.to_string(),
        file_name: None,
        date_sent: helpers::now_iso(),
        has_file: false,
        file_size_bytes: None,
        is_notification: true,
        date_sent_epoch: helpers::to_epoch(),
    };

    let _ = app_db::insert_message(&state.db, &msg).await;
    broadcast_to_session(state, session_id, WsNotification::RequestReloadMessage);
}
