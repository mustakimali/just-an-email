import { useState, useEffect } from 'react'
import Linkify from 'linkify-react'
import type { Message } from '../types'

interface Props {
  message: Message
  onDecrypt: (text: string, alias: string) => Promise<string | null>
  onDownload: (messageId: string, sessionId: string, alias?: string) => void
  sessionId: string
}

function formatFileSize(bytes: number): string {
  const sizes = ['B', 'KB', 'MB', 'GB']
  let i = 0
  let b = bytes
  while (b >= 1024 && i < sizes.length - 1) {
    b /= 1024
    i++
  }
  return `${b.toFixed(i === 0 ? 0 : 2)} ${sizes[i]}`
}

export default function MessageItem({ message, onDecrypt, onDownload, sessionId }: Props) {
  const [displayText, setDisplayText] = useState(message.Text)
  const [decrypted, setDecrypted] = useState(false)
  const [decryptFailed, setDecryptFailed] = useState(false)

  useEffect(() => {
    if (message.IsNotification || !message.EncryptionPublicKeyAlias) {
      setDisplayText(message.Text)
      setDecrypted(true)
      return
    }

    onDecrypt(message.Text, message.EncryptionPublicKeyAlias).then((result) => {
      if (result !== null) {
        setDisplayText(result)
        setDecrypted(true)
      } else {
        setDecryptFailed(true)
      }
    })
  }, [message.Text, message.EncryptionPublicKeyAlias, message.IsNotification, onDecrypt])

  if (message.IsNotification) {
    return (
      <div className="msg-notification" dangerouslySetInnerHTML={{ __html: displayText }} />
    )
  }

  const time = new Date(message.DateSent + 'Z').toLocaleTimeString()

  return (
    <div className="group">
      <div className="text-xs text-gray-500 bg-gray-100 px-3 py-1 group-hover:bg-blue-100 group-hover:text-white transition-colors">
        {time}
      </div>
      <div className="msg-content group-hover:border-blue-400">
        {decryptFailed ? (
          <span className="encrypted-badge">🔒 Encrypted</span>
        ) : message.HasFile ? (
          <div>
            <span className="file-name">{displayText}</span>
            {message.FileSizeBytes && (
              <span className="text-gray-400 text-sm ml-2">
                ({formatFileSize(message.FileSizeBytes)})
              </span>
            )}
            <button
              onClick={() =>
                onDownload(
                  message.Id,
                  sessionId,
                  message.EncryptionPublicKeyAlias || undefined
                )
              }
              className="ml-2 text-blue-600 hover:text-blue-800 text-sm underline"
            >
              Download
            </button>
          </div>
        ) : (
          <div className="whitespace-pre-wrap break-words">
            {decrypted ? (
              <Linkify options={{ target: '_blank', className: 'text-blue-600 underline' }}>
                {displayText}
              </Linkify>
            ) : (
              displayText
            )}
          </div>
        )}
      </div>
    </div>
  )
}
