import { arrayBufferToBase64, base64ToArrayBuffer } from './helpers'

export async function generateRsaKeyPair(): Promise<CryptoKeyPair> {
  return crypto.subtle.generateKey(
    {
      name: 'RSA-OAEP',
      modulusLength: 2048,
      publicExponent: new Uint8Array([1, 0, 1]),
      hash: 'SHA-256',
    },
    true,
    ['encrypt', 'decrypt']
  )
}

export async function exportKeyToJwk(key: CryptoKey): Promise<JsonWebKey> {
  return crypto.subtle.exportKey('jwk', key)
}

export async function importPublicKeyFromJwk(jwk: JsonWebKey): Promise<CryptoKey> {
  return crypto.subtle.importKey('jwk', jwk, { name: 'RSA-OAEP', hash: 'SHA-256' }, true, [
    'encrypt',
  ])
}

export async function importPrivateKeyFromJwk(jwk: JsonWebKey): Promise<CryptoKey> {
  return crypto.subtle.importKey('jwk', jwk, { name: 'RSA-OAEP', hash: 'SHA-256' }, true, [
    'decrypt',
  ])
}

interface HybridEncrypted {
  encryptedKey: string
  iv: string
  encryptedData: string
}

export async function hybridEncrypt(
  data: string | Uint8Array,
  publicKey: CryptoKey
): Promise<HybridEncrypted> {
  const aesKey = await crypto.subtle.generateKey({ name: 'AES-GCM', length: 256 }, true, [
    'encrypt',
    'decrypt',
  ])

  const iv = crypto.getRandomValues(new Uint8Array(12))
  const dataBytes = typeof data === 'string' ? new TextEncoder().encode(data) : data

  const encryptedData = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, aesKey, dataBytes.buffer as ArrayBuffer)

  const aesKeyRaw = await crypto.subtle.exportKey('raw', aesKey)
  const encryptedKey = await crypto.subtle.encrypt({ name: 'RSA-OAEP' }, publicKey, aesKeyRaw)

  return {
    encryptedKey: arrayBufferToBase64(encryptedKey),
    iv: arrayBufferToBase64(iv),
    encryptedData: arrayBufferToBase64(encryptedData),
  }
}

export async function hybridDecrypt(
  encrypted: HybridEncrypted,
  privateKey: CryptoKey
): Promise<ArrayBuffer> {
  const encKey = base64ToArrayBuffer(encrypted.encryptedKey)
  const iv = base64ToArrayBuffer(encrypted.iv)
  const encData = base64ToArrayBuffer(encrypted.encryptedData)

  const aesKeyRaw = await crypto.subtle.decrypt({ name: 'RSA-OAEP' }, privateKey, encKey)

  const aesKey = await crypto.subtle.importKey('raw', aesKeyRaw, { name: 'AES-GCM' }, false, [
    'decrypt',
  ])

  return crypto.subtle.decrypt({ name: 'AES-GCM', iv: new Uint8Array(iv) }, aesKey, encData)
}

// ECDH for key exchange
export async function generateEcdhKeyPair(): Promise<CryptoKeyPair> {
  return crypto.subtle.generateKey({ name: 'ECDH', namedCurve: 'P-256' }, false, ['deriveKey'])
}

export async function exportEcdhPublicKey(key: CryptoKey): Promise<JsonWebKey> {
  return crypto.subtle.exportKey('jwk', key)
}

export async function importEcdhPublicKey(jwk: JsonWebKey): Promise<CryptoKey> {
  return crypto.subtle.importKey('jwk', jwk, { name: 'ECDH', namedCurve: 'P-256' }, true, [])
}

export async function deriveAesKeyFromEcdh(
  ecdhPrivateKey: CryptoKey,
  ecdhPublicKey: CryptoKey
): Promise<CryptoKey> {
  return crypto.subtle.deriveKey(
    { name: 'ECDH', public: ecdhPublicKey },
    ecdhPrivateKey,
    { name: 'AES-GCM', length: 256 },
    false,
    ['encrypt', 'decrypt']
  )
}

export async function encryptWithDhKey(data: string, aesKey: CryptoKey): Promise<string> {
  const iv = crypto.getRandomValues(new Uint8Array(12))
  const dataBytes = new TextEncoder().encode(data)
  const encrypted = await crypto.subtle.encrypt({ name: 'AES-GCM', iv }, aesKey, dataBytes)
  return JSON.stringify({
    iv: arrayBufferToBase64(iv),
    data: arrayBufferToBase64(encrypted),
  })
}

export async function decryptWithDhKey(encryptedStr: string, aesKey: CryptoKey): Promise<string> {
  const obj = JSON.parse(encryptedStr)
  const iv = base64ToArrayBuffer(obj.iv)
  const data = base64ToArrayBuffer(obj.data)
  const decrypted = await crypto.subtle.decrypt(
    { name: 'AES-GCM', iv: new Uint8Array(iv) },
    aesKey,
    data
  )
  return new TextDecoder().decode(decrypted)
}

export async function encryptMessage(data: string, publicKey: CryptoKey): Promise<string> {
  const encrypted = await hybridEncrypt(data, publicKey)
  return btoa(JSON.stringify(encrypted))
}

export async function decryptMessage(
  encryptedBase64: string,
  privateKey: CryptoKey
): Promise<string | null> {
  try {
    const encrypted: HybridEncrypted = JSON.parse(atob(encryptedBase64))
    const decrypted = await hybridDecrypt(encrypted, privateKey)
    return new TextDecoder().decode(decrypted)
  } catch {
    return null
  }
}

export async function encryptFile(
  data: Uint8Array,
  publicKey: CryptoKey
): Promise<{ encryptedBlob: Blob; encryptedFileName: string; originalName: string }> {
  const encrypted = await hybridEncrypt(data, publicKey)
  const encryptedStr = btoa(JSON.stringify(encrypted))
  return {
    encryptedBlob: new Blob([encryptedStr], { type: 'application/octet-stream' }),
    encryptedFileName: '',
    originalName: '',
  }
}

export async function decryptFile(
  encryptedBase64: string,
  privateKey: CryptoKey
): Promise<ArrayBuffer | null> {
  try {
    const encrypted: HybridEncrypted = JSON.parse(atob(encryptedBase64))
    return await hybridDecrypt(encrypted, privateKey)
  } catch {
    return null
  }
}
