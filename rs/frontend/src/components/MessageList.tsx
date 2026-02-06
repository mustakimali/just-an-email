import type { Message } from '../types'
import MessageItem from './MessageItem'

interface Props {
  messages: Message[]
  onDecrypt: (text: string, alias: string) => Promise<string | null>
  onDownload: (messageId: string, sessionId: string, alias?: string) => void
  sessionId: string
}

export default function MessageList({ messages, onDecrypt, onDownload, sessionId }: Props) {
  if (messages.length === 0) {
    return <div className="text-center text-gray-400 py-8">No messages yet</div>
  }

  return (
    <div className="space-y-2">
      {messages.map((msg) => (
        <MessageItem
          key={msg.Id}
          message={msg}
          onDecrypt={onDecrypt}
          onDownload={onDownload}
          sessionId={sessionId}
        />
      ))}
    </div>
  )
}
