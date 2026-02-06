use serde::{Deserialize, Serialize};

#[derive(Debug, Deserialize)]
#[serde(tag = "type")]
pub enum ClientMessage {
    #[serde(rename = "connect")]
    Connect {
        #[serde(rename = "sessionId")]
        session_id: String,
    },
    #[serde(rename = "callPeer")]
    CallPeer {
        #[serde(rename = "peerId")]
        peer_id: String,
        method: String,
        param: String,
    },
    #[serde(rename = "share")]
    Share,
    #[serde(rename = "cancelShare")]
    CancelShare,
    #[serde(rename = "eraseSession")]
    EraseSession,
}

#[derive(Debug, Serialize, Clone)]
#[serde(tag = "type")]
pub enum ServerMessage {
    #[serde(rename = "connected")]
    Connected {
        #[serde(rename = "connectionId")]
        connection_id: String,
    },
    #[serde(rename = "requestReloadMessage")]
    RequestReloadMessage,
    #[serde(rename = "showSharePanel")]
    ShowSharePanel { token: String },
    #[serde(rename = "hideSharePanel")]
    HideSharePanel,
    #[serde(rename = "sessionDeleted")]
    SessionDeleted,
    #[serde(rename = "setNumberOfDevices")]
    SetNumberOfDevices { count: usize },
    #[serde(rename = "startKeyExchange")]
    StartKeyExchange {
        #[serde(rename = "peerId")]
        peer_id: String,
        pka: String,
        initiate: bool,
    },
    #[serde(rename = "callback")]
    Callback { method: String, data: String },
}

#[derive(Debug, Deserialize)]
#[serde(tag = "type")]
pub enum SecureLineClientMessage {
    #[serde(rename = "init")]
    Init { id: String },
    #[serde(rename = "broadcast")]
    Broadcast {
        event: String,
        data: Option<String>,
        all: Option<bool>,
    },
    #[serde(rename = "callPeer")]
    CallPeer {
        #[serde(rename = "peerId")]
        #[allow(dead_code)]
        peer_id: String,
        method: String,
        param: String,
    },
}

#[derive(Debug, Serialize, Clone)]
#[serde(tag = "type")]
pub enum SecureLineServerMessage {
    #[serde(rename = "broadcast")]
    Broadcast {
        event: String,
        data: Option<String>,
    },
    #[serde(rename = "startKeyExchange")]
    StartKeyExchange {
        #[serde(rename = "peerId")]
        peer_id: String,
        pka: String,
        initiate: bool,
    },
    #[serde(rename = "callback")]
    Callback { method: String, data: String },
}
