import { useState, useEffect, useCallback, useRef } from 'react'
import { useWebSocket } from '../hooks/useWebSocket'
import {
  generateEcdhKeyPair,
  exportEcdhPublicKey,
  importEcdhPublicKey,
  deriveAesKeyFromEcdh,
  encryptMessage,
  decryptMessage,
} from '../crypto/encrypt'
import { generateGuid } from '../crypto/helpers'
import { post, get } from '../api/client'

export default function SecureLine() {
  const [sessionId, setSessionId] = useState('')
  const [status, setStatus] = useState<'init' | 'waiting' | 'connected' | 'disconnected'>('init')
  const [messages, setMessages] = useState<Array<{ from: 'me' | 'peer'; text: string }>>([])
  const [text, setText] = useState('')
  const [isInitiator, setIsInitiator] = useState(true)

  const derivedKeyRef = useRef<CryptoKey | null>(null)
  const ecdhKeyPairRef = useRef<CryptoKeyPair | null>(null)

  const { send, lastMessage } = useWebSocket('/ws/secure-line')

  useEffect(() => {
    const hash = window.location.hash
    if (hash && hash.length >= 33) {
      setSessionId(hash.substring(1))
    } else {
      const id = generateGuid() + generateGuid()
      setSessionId(id)
      window.location.hash = id
    }
  }, [])

  useEffect(() => {
    if (!sessionId) return
    send({ type: 'init', sessionId })
    setStatus('waiting')
  }, [sessionId, send])

  const performKeyExchange = useCallback(
    async (peerPublicKeyJwk: JsonWebKey) => {
      if (!ecdhKeyPairRef.current) return
      const peerPub = await importEcdhPublicKey(peerPublicKeyJwk)
      derivedKeyRef.current = await deriveAesKeyFromEcdh(
        ecdhKeyPairRef.current.privateKey,
        peerPub
      )
      ecdhKeyPairRef.current = null
      setStatus('connected')
    },
    []
  )

  useEffect(() => {
    if (!lastMessage) return

    switch (lastMessage.type) {
      case 'connected':
        break

      case 'startKeyExchange': {
        const initiate = lastMessage.initiate || false
        setIsInitiator(initiate)

        if (initiate) {
          generateEcdhKeyPair().then(async (kp) => {
            ecdhKeyPairRef.current = kp
            const pubJwk = await exportEcdhPublicKey(kp.publicKey)
            send({
              type: 'callPeer',
              peerId: 'ALL',
              method: 'ExchangeKey',
              param: JSON.stringify({ publicKey: pubJwk }),
            })
          })
        }
        break
      }

      case 'callback': {
        const method = lastMessage.method || ''
        const data = lastMessage.data || ''

        switch (method) {
          case 'ExchangeKey': {
            const parsed = JSON.parse(data)
            generateEcdhKeyPair().then(async (kp) => {
              ecdhKeyPairRef.current = kp
              const peerPub = await importEcdhPublicKey(parsed.publicKey)
              derivedKeyRef.current = await deriveAesKeyFromEcdh(kp.privateKey, peerPub)
              const pubJwk = await exportEcdhPublicKey(kp.publicKey)
              send({
                type: 'callPeer',
                peerId: 'ALL',
                method: 'DeriveSecret',
                param: JSON.stringify({ publicKey: pubJwk }),
              })
              ecdhKeyPairRef.current = null
              setStatus('connected')
            })
            break
          }
          case 'DeriveSecret': {
            const parsed = JSON.parse(data)
            performKeyExchange(parsed.publicKey)
            break
          }
          case 'GET': {
            if (!derivedKeyRef.current) break
            get<{ event: string; data: string }>(`/api/secure-line/message?id=${data}`).then(
              async (result) => {
                if (!derivedKeyRef.current) return
                const decryptedEvent = await decryptMessage(result.event, derivedKeyRef.current)
                const decryptedData = result.data
                  ? await decryptMessage(result.data, derivedKeyRef.current)
                  : ''

                if (decryptedEvent === 'message') {
                  setMessages((prev) => [...prev, { from: 'peer', text: decryptedData || '' }])
                }
              }
            ).catch(console.error)
            break
          }
        }
        break
      }

    }
  }, [lastMessage, send, performKeyExchange])

  const handleSend = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!text.trim() || !derivedKeyRef.current) return

    const encEvent = await encryptMessage('message', derivedKeyRef.current)
    const encData = await encryptMessage(text, derivedKeyRef.current)

    const msgId = generateGuid()
    await post('/api/secure-line/message', {
      id: msgId,
      data: JSON.stringify({ event: encEvent, data: encData }),
    })

    send({
      type: 'callPeer',
      peerId: 'ALL',
      method: 'GET',
      param: msgId,
    })

    setMessages((prev) => [...prev, { from: 'me', text }])
    setText('')
  }

  const shareUrl = `${window.location.origin}/secure-line#${sessionId}`

  return (
    <div className="max-w-2xl mx-auto px-4 py-8">
      <h1 className="text-2xl font-bold mb-2">Secure Line</h1>
      <p className="text-gray-600 mb-6 text-sm">
        End-to-end encrypted real-time messaging. Share the link below with the other party.
      </p>

      {status === 'waiting' && (
        <div className="bg-blue-50 border border-blue-200 rounded p-4 mb-6">
          <p className="text-sm text-blue-800 mb-2">Waiting for the other party to connect...</p>
          <div className="flex items-center gap-2">
            <input
              type="text"
              value={shareUrl}
              readOnly
              className="flex-1 px-3 py-2 border rounded text-sm bg-white"
              onClick={(e) => (e.target as HTMLInputElement).select()}
            />
            <button
              onClick={() => {
                navigator.clipboard.writeText(shareUrl)
              }}
              className="btn btn-primary text-sm px-3 py-2"
            >
              Copy
            </button>
          </div>
        </div>
      )}

      {status === 'connected' && (
        <div className="bg-green-50 border border-green-200 rounded p-2 mb-4 text-sm text-green-800">
          Secure line established. Messages are end-to-end encrypted.
        </div>
      )}

      {status === 'disconnected' && (
        <div className="bg-red-50 border border-red-200 rounded p-2 mb-4 text-sm text-red-800">
          The other party has disconnected.
        </div>
      )}

      <div className="border rounded mb-4 min-h-[300px] max-h-[500px] overflow-y-auto p-4 space-y-3">
        {messages.length === 0 && status === 'connected' && (
          <div className="text-center text-gray-400 py-8">Send a message to get started</div>
        )}
        {messages.map((msg, i) => (
          <div
            key={i}
            className={`flex ${msg.from === 'me' ? 'justify-end' : 'justify-start'}`}
          >
            <div
              className={`max-w-[70%] rounded-lg px-4 py-2 text-sm ${
                msg.from === 'me'
                  ? 'bg-blue-600 text-white'
                  : 'bg-gray-100 text-gray-900'
              }`}
            >
              {msg.text}
            </div>
          </div>
        ))}
      </div>

      {status === 'connected' && (
        <form onSubmit={handleSend} className="flex gap-2">
          <input
            type="text"
            value={text}
            onChange={(e) => setText(e.target.value)}
            placeholder="Type a message..."
            className="flex-1 border border-gray-300 rounded px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
            autoFocus
          />
          <button
            type="submit"
            disabled={!text.trim()}
            className="px-4 py-2 bg-green-600 text-white rounded text-sm hover:bg-green-700 disabled:opacity-50"
          >
            Send
          </button>
        </form>
      )}
    </div>
  )
}
