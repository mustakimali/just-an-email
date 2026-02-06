use axum::{
    Router,
    extract::{Path, State},
    http::{StatusCode, HeaderMap},
    response::IntoResponse,
    routing::get,
};
use base64::{Engine as _, engine::general_purpose::STANDARD as BASE64};

use crate::db::app_db;
use crate::state::AppState;

async fn get_public_key(
    State(state): State<AppState>,
    Path(id): Path<String>,
) -> impl IntoResponse {
    match app_db::get_public_key_by_id(&state.db, &id).await {
        Ok(Some(pk)) => (
            StatusCode::OK,
            [("content-type", "application/json")],
            pk.public_key_json,
        )
            .into_response(),
        _ => (
            StatusCode::NOT_FOUND,
            axum::Json(serde_json::json!({"error": "Public key not found"})),
        )
            .into_response(),
    }
}

async fn get_upload_script(
    State(state): State<AppState>,
    Path(id): Path<String>,
    headers: HeaderMap,
) -> impl IntoResponse {
    let pk = match app_db::get_public_key_by_id(&state.db, &id).await {
        Ok(Some(pk)) => pk,
        _ => return (StatusCode::NOT_FOUND, "# Error: Public key not found").into_response(),
    };

    let host = headers
        .get("host")
        .and_then(|v| v.to_str().ok())
        .unwrap_or("localhost:8080");
    let base_url = format!("https://{}", host);

    let jwk_b64 = BASE64.encode(pk.public_key_json.as_bytes());
    let session_id_b64 = BASE64.encode(pk.session_id.as_bytes());
    let alias_b64 = BASE64.encode(pk.alias.as_bytes());

    let script = format!(
        r#"#!/usr/bin/env python3
# Usage: curl -s {base_url}/k/{id}/upload | python3 - file.txt
import sys, json, base64, os
from cryptography.hazmat.primitives.asymmetric import rsa, padding
from cryptography.hazmat.primitives import hashes
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.backends import default_backend
import urllib.request

if len(sys.argv) < 2:
    print('Usage: curl -s {base_url}/k/{id}/upload | python3 - <file>', file=sys.stderr)
    sys.exit(1)

jwk = json.loads(base64.b64decode('{jwk_b64}').decode())
session_id = base64.b64decode('{session_id_b64}').decode()
alias = base64.b64decode('{alias_b64}').decode()

n = int.from_bytes(base64.urlsafe_b64decode(jwk['n'] + '=='), 'big')
e = int.from_bytes(base64.urlsafe_b64decode(jwk['e'] + '=='), 'big')
pub = rsa.RSAPublicNumbers(e, n).public_key(default_backend())

filepath = sys.argv[1]
filename = os.path.basename(filepath)
with open(filepath, 'rb') as f:
    data = f.read()

aes_key = os.urandom(32)
iv = os.urandom(12)
encrypted_data = AESGCM(aes_key).encrypt(iv, data, None)
encrypted_key = pub.encrypt(aes_key, padding.OAEP(padding.MGF1(hashes.SHA256()), hashes.SHA256(), None))

payload = base64.b64encode(json.dumps({{
    'encryptedKey': base64.b64encode(encrypted_key).decode(),
    'iv': base64.b64encode(iv).decode(),
    'encryptedData': base64.b64encode(encrypted_data).decode()
}}).encode()).decode()

fn_iv = os.urandom(12)
fn_encrypted = AESGCM(aes_key).encrypt(fn_iv, filename.encode(), None)
fn_key_enc = pub.encrypt(aes_key, padding.OAEP(padding.MGF1(hashes.SHA256()), hashes.SHA256(), None))
fn_payload = base64.b64encode(json.dumps({{
    'encryptedKey': base64.b64encode(fn_key_enc).decode(),
    'iv': base64.b64encode(fn_iv).decode(),
    'encryptedData': base64.b64encode(fn_encrypted).decode()
}}).encode()).decode()

boundary = '----PythonFormBoundary'
body = (
    f'--{{boundary}}\r\nContent-Disposition: form-data; name="SessionId"\r\n\r\n{{session_id}}\r\n'
    f'--{{boundary}}\r\nContent-Disposition: form-data; name="EncryptionPublicKeyAlias"\r\n\r\n{{alias}}\r\n'
    f'--{{boundary}}\r\nContent-Disposition: form-data; name="ComposerText"\r\n\r\n{{fn_payload}}\r\n'
    f'--{{boundary}}\r\nContent-Disposition: form-data; name="file"; filename="{{filename}}.enc"\r\nContent-Type: application/octet-stream\r\n\r\n'
).encode() + payload.encode() + f'\r\n--{{boundary}}--\r\n'.encode()

req = urllib.request.Request('{base_url}/api/app/post/files-stream', body, {{
    'Content-Type': f'multipart/form-data; boundary={{boundary}}'
}})
urllib.request.urlopen(req)
print(f'Uploaded: {{filename}}')
"#
    );

    (StatusCode::OK, [("content-type", "text/plain")], script).into_response()
}

pub fn routes() -> Router<AppState> {
    Router::new()
        .route("/api/k/{id}", get(get_public_key))
        .route("/api/k/{id}/upload", get(get_upload_script))
}
