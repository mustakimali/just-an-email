import { useState, useEffect, useRef, useCallback } from 'react'
import { useParams, useNavigate, Link } from 'react-router'
import { post, postForm } from '../api/client'
import { toast } from 'sonner'
import type { Message, LitePollResponse } from '../types'

export default function LiteSession() {
  const { id1, id2 } = useParams<{ id1: string; id2: string }>()
  const navigate = useNavigate()
  const [messages, setMessages] = useState<Message[]>([])
  const [shareToken, setShareToken] = useState<string | null>(null)
  const [hasSession, setHasSession] = useState(true)
  const [text, setText] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)
  const lastEpochRef = useRef(-1)
  const pollRef = useRef<ReturnType<typeof setInterval> | null>(null)

  const poll = useCallback(async () => {
    if (!id1 || !id2) return

    try {
      const result = await post<LitePollResponse>('/api/app/lite/poll', {
        id: id1,
        id2,
        from: lastEpochRef.current,
      })

      if (!result.hasSession) {
        setHasSession(false)
        navigate('/')
        return
      }

      if (result.messages && result.messages.length > 0) {
        setMessages((prev) => {
          const existingIds = new Set(prev.map((m) => m.Id))
          const newMsgs = result.messages.filter((m) => !existingIds.has(m.Id))
          return [...newMsgs, ...prev]
        })
        const maxEpoch = Math.max(...result.messages.map((m) => m.DateSentEpoch))
        if (maxEpoch > lastEpochRef.current) {
          lastEpochRef.current = maxEpoch
        }
      }

      if (result.hasToken && result.token) {
        setShareToken(result.token)
      } else {
        setShareToken(null)
      }
    } catch {}
  }, [id1, id2, navigate])

  useEffect(() => {
    if (!id1 || !id2) return
    post('/api/app/new', { id: id1, id2 }).then(() => poll())
    pollRef.current = setInterval(poll, 5000)
    return () => {
      if (pollRef.current) clearInterval(pollRef.current)
    }
  }, [id1, id2, poll])

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!id1 || !id2) return
    if (!text.trim() && !selectedFile) return

    try {
      if (selectedFile) {
        const formData = new FormData()
        formData.append('id', id1)
        formData.append('id2', id2)
        formData.append('text', selectedFile.name)
        formData.append('file', selectedFile)
        formData.append('fileSize', selectedFile.size.toString())
        await postForm('/api/app/lite/post/files', formData)
      } else {
        await post('/api/app/post', {
          id: id1,
          id2,
          text,
        })
      }

      setText('')
      setSelectedFile(null)
      if (fileRef.current) fileRef.current.value = ''
      await poll()
    } catch {
      toast.error('Failed to send')
    }
  }

  const handleShareToken = async () => {
    if (!id1 || !id2) return
    try {
      await post('/api/app/lite/share-token/new', { id: id1, id2 })
      await poll()
    } catch {
      toast.error('Failed to create share token')
    }
  }

  const handleCancelToken = async () => {
    if (!id1 || !id2) return
    try {
      await post('/api/app/lite/share-token/cancel', { id: id1, id2 })
      setShareToken(null)
    } catch {}
  }

  if (!hasSession) return null

  return (
    <div className="max-w-3xl mx-auto px-4 py-4">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-lg font-bold">Lite Session</h1>
        <div className="flex gap-2">
          {!shareToken ? (
            <button onClick={handleShareToken} className="btn btn-primary text-sm px-3 py-1">
              Share
            </button>
          ) : (
            <div className="flex items-center gap-2">
              <span className="font-mono text-lg font-bold">{shareToken}</span>
              <button onClick={handleCancelToken} className="text-sm text-red-600 hover:underline">
                Cancel
              </button>
            </div>
          )}
          <Link
            to={`/app/lite/${id1}/${id2}/delete`}
            className="btn btn-danger text-sm px-3 py-1"
          >
            Erase
          </Link>
        </div>
      </div>

      <div className="bg-yellow-50 border border-yellow-200 rounded p-3 mb-4 text-sm text-yellow-800">
        Lite mode: No encryption. Messages are sent in plain text. Polling every 5 seconds.
      </div>

      <div className="mb-4 space-y-2">
        {messages.length === 0 ? (
          <div className="text-center text-gray-400 py-8">No messages yet</div>
        ) : (
          messages.map((msg) => (
            <div key={msg.Id} className="group">
              <div className="text-xs text-gray-500 bg-gray-100 px-3 py-1">
                {new Date(msg.DateSent + 'Z').toLocaleTimeString()}
              </div>
              <div className="msg-content">
                {msg.HasFile ? (
                  <div>
                    <span>{msg.Text}</span>
                    <a
                      href={`/api/app/file/${msg.Id}/${id1}`}
                      className="ml-2 text-blue-600 hover:text-blue-800 text-sm underline"
                      download
                    >
                      Download
                    </a>
                  </div>
                ) : (
                  <div className="whitespace-pre-wrap break-words">{msg.Text}</div>
                )}
              </div>
            </div>
          ))
        )}
      </div>

      <div className="sticky bottom-0 bg-white py-3 border-t">
        <form onSubmit={handleSubmit} className="flex gap-2 items-end">
          <input
            type="text"
            value={selectedFile ? selectedFile.name : text}
            onChange={(e) => setText(e.target.value)}
            placeholder="Type a message..."
            className="flex-1 border border-gray-300 rounded px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
            readOnly={!!selectedFile}
          />
          <input
            ref={fileRef}
            type="file"
            onChange={(e) => {
              const file = e.target.files?.[0]
              if (file) {
                setSelectedFile(file)
                setText(file.name)
              }
            }}
            className="hidden"
          />
          <button
            type="button"
            onClick={() => fileRef.current?.click()}
            className="px-3 py-2 border border-gray-300 rounded text-gray-600 hover:bg-gray-100 text-sm"
          >
            📎
          </button>
          <button
            type="submit"
            disabled={!text.trim() && !selectedFile}
            className="px-4 py-2 bg-green-600 text-white rounded text-sm hover:bg-green-700 disabled:opacity-50"
          >
            Send
          </button>
        </form>
      </div>
    </div>
  )
}
