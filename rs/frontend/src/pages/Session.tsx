import { useState, useEffect, useCallback, useRef } from 'react'
import { useNavigate } from 'react-router'
import { toast } from 'sonner'
import { useSession } from '../hooks/useSession'
import { useWebSocket } from '../hooks/useWebSocket'
import { useEncryption } from '../hooks/useEncryption'
import { post, postForm } from '../api/client'
import MessageList from '../components/MessageList'
import Composer from '../components/Composer'
import SharePanel from '../components/SharePanel'

export default function Session() {
  const navigate = useNavigate()
  const [initialized, setInitialized] = useState(false)
  const [sending, setSending] = useState(false)
  const initCalledRef = useRef(false)

  const session = useSession()
  const { send, lastMessage } = useWebSocket('/ws/conversation')
  const encryption = useEncryption()

  const callPeer = useCallback(
    (method: string, data: unknown) => {
      send({
        type: 'callPeer',
        peerId: encryption.peerIdRef.current,
        method,
        param: typeof data === 'string' ? data : JSON.stringify(data),
      })
    },
    [send, encryption.peerIdRef]
  )

  useEffect(() => {
    if (initCalledRef.current) return
    initCalledRef.current = true

    session.initSession().then(({ id, id2 }) => {
      encryption.generateOwnKeyPair(id, id2).then(() => {
        setInitialized(true)

        const hash = window.location.hash
        if (hash.includes('/')) {
          const secret = atob(hash.split('/')[1])
          if (secret) {
            handleSendMessage(secret, undefined, id, id2)
          }
        }
      })
    })
  }, [])

  useEffect(() => {
    if (!initialized || !session.sessionId) return
    send({ type: 'connect', sessionId: session.sessionId })
  }, [initialized, session.sessionId, send])

  useEffect(() => {
    if (!lastMessage) return

    switch (lastMessage.type) {
      case 'connected':
        session.setConnectionId(lastMessage.connectionId || '')
        break

      case 'requestReloadMessage':
        session.loadMessages()
        break

      case 'showSharePanel':
        session.setShareToken(lastMessage.token || null)
        break

      case 'hideSharePanel':
        session.setShareToken(null)
        break

      case 'sessionDeleted':
        toast.info('Session has been erased')
        navigate('/')
        break

      case 'setNumberOfDevices':
        session.setNumDevices(lastMessage.count || 0)
        break

      case 'startKeyExchange': {
        const peerId = lastMessage.peerId || ''
        const initiate = lastMessage.initiate || false
        encryption.peerIdRef.current = peerId

        if (initiate) {
          encryption.startEcdhExchange(callPeer)
        }
        break
      }

      case 'callback': {
        const method = lastMessage.method || ''
        const data = lastMessage.data || ''

        switch (method) {
          case 'ExchangeKey': {
            const parsed = JSON.parse(data)
            encryption.handleExchangeKey(
              parsed.publicKey,
              callPeer,
              session.sessionId,
              session.sessionVerification
            )
            break
          }
          case 'DeriveSecret': {
            const parsed = JSON.parse(data)
            encryption.handleDeriveSecret(
              parsed.publicKey,
              session.sessionId,
              session.sessionVerification
            )
            break
          }
          case 'broadcastKeys': {
            const parsed = JSON.parse(data)
            encryption.receiveKeys(parsed)
            session.loadMessages()
            break
          }
        }
        break
      }
    }
  }, [lastMessage])

  const handleSendMessage = async (
    text: string,
    file?: File,
    sid?: string,
    sv?: string
  ) => {
    const sessionId = sid || session.sessionId
    const sessionVerification = sv || session.sessionVerification
    if (!sessionId || !sessionVerification) return

    setSending(true)
    try {
      const alias = encryption.getCurrentAlias()

      if (file) {
        const fileData = new Uint8Array(await file.arrayBuffer())
        const encryptedFile = await encryption.encryptFileData(fileData)
        const encryptedName = await encryption.encrypt(file.name)

        const blob = new Blob([encryptedFile], { type: 'application/octet-stream' })
        const encFile = new File([blob], 'encrypted.enc', { type: 'application/octet-stream' })

        const formData = new FormData()
        formData.append('id', sessionId)
        formData.append('id2', sessionVerification)
        formData.append('pka', alias)
        formData.append('text', encryptedName)
        formData.append('file', encFile)
        formData.append('fileSize', file.size.toString())

        await postForm('/api/app/post/files-stream', formData)
      } else {
        const encryptedText = await encryption.encrypt(text)

        await post('/api/app/post', {
          id: sessionId,
          id2: sessionVerification,
          pka: alias,
          text: encryptedText,
        })
      }

      session.loadMessages(sessionId, sessionVerification)
    } catch (e) {
      toast.error('Failed to send message')
      console.error(e)
    } finally {
      setSending(false)
    }
  }

  const handleDecrypt = useCallback(
    async (text: string, alias: string) => {
      return encryption.decrypt(text, alias)
    },
    [encryption]
  )

  const handleDownload = useCallback(
    async (messageId: string, sessionId: string, alias?: string) => {
      try {
        const res = await fetch(`/api/app/file/${messageId}/${sessionId}`)
        if (!res.ok) throw new Error('Download failed')

        const encText = await res.text()

        if (alias) {
          const decrypted = await encryption.decryptFileData(encText, alias)
          if (!decrypted) {
            toast.error('Failed to decrypt file')
            return
          }

          const msg = session.messages.find((m) => m.Id === messageId)
          let fileName = 'download'
          if (msg?.Text && msg.EncryptionPublicKeyAlias) {
            const decName = await encryption.decrypt(msg.Text, msg.EncryptionPublicKeyAlias)
            if (decName) fileName = decName
          }

          const blob = new Blob([decrypted])
          const url = URL.createObjectURL(blob)
          const a = document.createElement('a')
          a.href = url
          a.download = fileName
          a.click()
          URL.revokeObjectURL(url)
        } else {
          const blob = await res.blob()
          const url = URL.createObjectURL(blob)
          const a = document.createElement('a')
          a.href = url
          a.download = 'download'
          a.click()
          URL.revokeObjectURL(url)
        }
      } catch (e) {
        toast.error('Download failed')
        console.error(e)
      }
    },
    [encryption, session.messages]
  )

  const handleShare = () => {
    send({ type: 'share' })
  }

  const handleCancelShare = () => {
    send({ type: 'cancelShare' })
    session.setShareToken(null)
  }

  const handleErase = () => {
    if (confirm('Erase this session? All messages and files will be permanently deleted.')) {
      send({ type: 'eraseSession' })
    }
  }

  if (!initialized) {
    return (
      <div className="flex items-center justify-center py-20">
        <div className="text-gray-500">Setting up secure session...</div>
      </div>
    )
  }

  const sessionUrl = `${window.location.origin}/app#${session.sessionId}${session.sessionVerification}`

  return (
    <div className="max-w-3xl mx-auto px-4 py-4">
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <span className="text-sm text-gray-500">
            {session.numDevices} device{session.numDevices !== 1 ? 's' : ''} connected
          </span>
        </div>
        <div className="flex gap-2">
          {!session.shareToken && (
            <button onClick={handleShare} className="btn btn-primary text-sm px-3 py-1">
              Share
            </button>
          )}
          <button onClick={handleErase} className="btn btn-danger text-sm px-3 py-1">
            Erase
          </button>
        </div>
      </div>

      {session.shareToken && (
        <SharePanel
          token={session.shareToken}
          onCancel={handleCancelShare}
          sessionUrl={sessionUrl}
        />
      )}

      <div className="mb-4">
        <MessageList
          messages={session.messages}
          onDecrypt={handleDecrypt}
          onDownload={handleDownload}
          sessionId={session.sessionId}
        />
      </div>

      <div className="sticky bottom-0 bg-white py-3 border-t">
        <Composer onSend={handleSendMessage} disabled={sending} />
      </div>
    </div>
  )
}
