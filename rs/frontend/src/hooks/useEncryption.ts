import { useRef, useCallback } from 'react'
import {
  generateRsaKeyPair,
  exportKeyToJwk,
  importPublicKeyFromJwk,
  importPrivateKeyFromJwk,
  generateEcdhKeyPair,
  exportEcdhPublicKey,
  importEcdhPublicKey,
  deriveAesKeyFromEcdh,
  encryptWithDhKey,
  decryptWithDhKey,
  encryptMessage,
  decryptMessage,
  hybridEncrypt,
  decryptFile,
} from '../crypto/encrypt'
import { post } from '../api/client'

interface KeyEntry {
  key: string
  publicKeyJwk?: JsonWebKey
  privateKeyJwk?: JsonWebKey
  publicKey?: CryptoKey
  privateKey?: CryptoKey
  dhKey?: CryptoKey
}

export function useEncryption() {
  const keysRef = useRef<KeyEntry[]>([])
  const keysHashRef = useRef<Record<string, KeyEntry>>({})
  const currentAliasRef = useRef<string>('')
  const ecdhKeyPairRef = useRef<CryptoKeyPair | null>(null)
  const peerIdRef = useRef<string>('')
  const initiatorRef = useRef(false)

  const isEstablished = useCallback(() => keysRef.current.length > 0, [])

  const getCurrentAlias = useCallback(() => currentAliasRef.current, [])

  const uploadPublicKey = useCallback(
    async (alias: string, publicKeyJwk: JsonWebKey, sessionId: string, sessionVerification: string) => {
      try {
        const result = await post<{ id: string }>('/api/app/key', {
          sessionId,
          sessionVerification,
          alias,
          publicKey: JSON.stringify(publicKeyJwk),
        })
        return result?.id
      } catch {
        return null
      }
    },
    []
  )

  const generateOwnKeyPair = useCallback(
    async (sessionId: string, sessionVerification: string) => {
      const alias = Array.from(crypto.getRandomValues(new Uint8Array(16)))
        .map((b) => b.toString(16).padStart(2, '0'))
        .join('')

      currentAliasRef.current = alias

      const keyPair = await generateRsaKeyPair()
      const publicKeyJwk = await exportKeyToJwk(keyPair.publicKey)
      const privateKeyJwk = await exportKeyToJwk(keyPair.privateKey)

      const entry: KeyEntry = {
        key: alias,
        publicKeyJwk,
        privateKeyJwk,
        publicKey: keyPair.publicKey,
        privateKey: keyPair.privateKey,
      }

      keysRef.current.push(entry)
      keysHashRef.current[alias] = entry

      await uploadPublicKey(alias, publicKeyJwk, sessionId, sessionVerification)

      return alias
    },
    [uploadPublicKey]
  )

  const startEcdhExchange = useCallback(
    async (send: (method: string, data: unknown) => void) => {
      ecdhKeyPairRef.current = await generateEcdhKeyPair()
      const pubJwk = await exportEcdhPublicKey(ecdhKeyPairRef.current.publicKey)
      send('ExchangeKey', { publicKey: pubJwk })
    },
    []
  )

  const handleExchangeKey = useCallback(
    async (
      peerPublicKeyJwk: JsonWebKey,
      send: (method: string, data: unknown) => void,
      sessionId: string,
      sessionVerification: string
    ) => {
      ecdhKeyPairRef.current = await generateEcdhKeyPair()
      const peerPublicKey = await importEcdhPublicKey(peerPublicKeyJwk)
      const derivedKey = await deriveAesKeyFromEcdh(
        ecdhKeyPairRef.current.privateKey,
        peerPublicKey
      )
      const pubJwk = await exportEcdhPublicKey(ecdhKeyPairRef.current.publicKey)
      send('DeriveSecret', { publicKey: pubJwk })
      ecdhKeyPairRef.current = null
      await onHandshakeDone(derivedKey, sessionId, sessionVerification)
    },
    []
  )

  const handleDeriveSecret = useCallback(
    async (peerPublicKeyJwk: JsonWebKey, sessionId: string, sessionVerification: string) => {
      if (!ecdhKeyPairRef.current) return
      const peerPublicKey = await importEcdhPublicKey(peerPublicKeyJwk)
      const derivedKey = await deriveAesKeyFromEcdh(
        ecdhKeyPairRef.current.privateKey,
        peerPublicKey
      )
      ecdhKeyPairRef.current = null
      await onHandshakeDone(derivedKey, sessionId, sessionVerification)
    },
    []
  )

  const onHandshakeDone = async (
    dhKey: CryptoKey,
    sessionId: string,
    sessionVerification: string
  ) => {
    const alias = currentAliasRef.current
    const keyPair = await generateRsaKeyPair()
    const publicKeyJwk = await exportKeyToJwk(keyPair.publicKey)
    const privateKeyJwk = await exportKeyToJwk(keyPair.privateKey)

    const entry: KeyEntry = {
      key: alias,
      publicKeyJwk,
      privateKeyJwk,
      publicKey: keyPair.publicKey,
      privateKey: keyPair.privateKey,
      dhKey,
    }

    keysRef.current.push(entry)
    keysHashRef.current[alias] = entry

    await uploadPublicKey(alias, publicKeyJwk, sessionId, sessionVerification)
  }

  const broadcastKeys = useCallback(
    async (send: (method: string, data: unknown) => void) => {
      const keysJson = JSON.stringify(
        keysRef.current.map((k) => ({
          Key: k.key,
          PublicKeyJwk: k.publicKeyJwk,
          PrivateKeyJwk: k.privateKeyJwk,
        }))
      )

      const encKeys = []
      for (const k of keysRef.current) {
        if (k.dhKey) {
          encKeys.push({
            Key: k.key,
            EncryptedSecrets: await encryptWithDhKey(keysJson, k.dhKey),
          })
        }
      }

      send('broadcastKeys', encKeys)
    },
    []
  )

  const receiveKeys = useCallback(async (dataObj: Array<{ Key: string; EncryptedSecrets: string }>) => {
    for (const nk of dataObj) {
      for (const ik of keysRef.current) {
        if (ik.key === nk.Key && ik.dhKey) {
          const decryptedJson = await decryptWithDhKey(nk.EncryptedSecrets, ik.dhKey)
          const incoming: Array<{ Key: string; PublicKeyJwk: JsonWebKey; PrivateKeyJwk: JsonWebKey }> =
            JSON.parse(decryptedJson)

          for (const k of incoming) {
            const existing = keysHashRef.current[k.Key]
            if (existing) {
              existing.publicKeyJwk = k.PublicKeyJwk
              existing.privateKeyJwk = k.PrivateKeyJwk
              existing.publicKey = undefined
              existing.privateKey = undefined

              if (k.Key === currentAliasRef.current) {
                existing.publicKey = await importPublicKeyFromJwk(k.PublicKeyJwk)
                existing.privateKey = await importPrivateKeyFromJwk(k.PrivateKeyJwk)
              }
            } else {
              const entry: KeyEntry = {
                key: k.Key,
                publicKeyJwk: k.PublicKeyJwk,
                privateKeyJwk: k.PrivateKeyJwk,
                publicKey: k.PublicKeyJwk ? await importPublicKeyFromJwk(k.PublicKeyJwk) : undefined,
                privateKey: k.PrivateKeyJwk
                  ? await importPrivateKeyFromJwk(k.PrivateKeyJwk)
                  : undefined,
              }
              keysRef.current.push(entry)
              keysHashRef.current[k.Key] = entry
            }
          }
          return
        }
      }
    }
  }, [])

  const encrypt = useCallback(async (text: string): Promise<string> => {
    const entry = keysRef.current.find((k) => k.publicKey)
    if (!entry?.publicKey) throw new Error('No public key')
    return encryptMessage(text, entry.publicKey)
  }, [])

  const decrypt = useCallback(async (encryptedBase64: string, alias: string): Promise<string | null> => {
    const entry = keysHashRef.current[alias]
    if (!entry) return null

    if (!entry.privateKey && entry.privateKeyJwk) {
      entry.privateKey = await importPrivateKeyFromJwk(entry.privateKeyJwk)
    }
    if (!entry.privateKey) return null

    return decryptMessage(encryptedBase64, entry.privateKey)
  }, [])

  const encryptFileData = useCallback(async (data: Uint8Array): Promise<string> => {
    const entry = keysRef.current.find((k) => k.publicKey)
    if (!entry?.publicKey) throw new Error('No public key')
    const encrypted = await hybridEncrypt(data, entry.publicKey)
    return btoa(JSON.stringify(encrypted))
  }, [])

  const decryptFileData = useCallback(async (encryptedBase64: string, alias: string): Promise<ArrayBuffer | null> => {
    const entry = keysHashRef.current[alias]
    if (!entry) return null

    if (!entry.privateKey && entry.privateKeyJwk) {
      entry.privateKey = await importPrivateKeyFromJwk(entry.privateKeyJwk)
    }
    if (!entry.privateKey) return null

    return decryptFile(encryptedBase64, entry.privateKey)
  }, [])

  return {
    isEstablished,
    getCurrentAlias,
    generateOwnKeyPair,
    startEcdhExchange,
    handleExchangeKey,
    handleDeriveSecret,
    broadcastKeys,
    receiveKeys,
    encrypt,
    decrypt,
    encryptFileData,
    decryptFileData,
    peerIdRef,
    initiatorRef,
    currentAliasRef,
  }
}
