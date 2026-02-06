import { useState, useCallback, useRef } from 'react'
import { generateGuid } from '../crypto/helpers'
import { post } from '../api/client'
import type { Message } from '../types'

export function useSession() {
  const [sessionId, setSessionId] = useState('')
  const [sessionVerification, setSessionVerification] = useState('')
  const [connectionId, setConnectionId] = useState('')
  const [messages, setMessages] = useState<Message[]>([])
  const [shareToken, setShareToken] = useState<string | null>(null)
  const [numDevices, setNumDevices] = useState(0)
  const lastEpochRef = useRef(-1)

  const initSession = useCallback(async () => {
    let id = ''
    let id2 = ''

    const hash = window.location.hash
    if (hash && hash.length >= 65) {
      id = hash.substring(1, 33)
      id2 = hash.substring(33, 65)
    } else {
      id = generateGuid()
      id2 = generateGuid()
      window.history.replaceState(null, '', '/app')
      window.location.hash = id + id2
    }

    setSessionId(id)
    setSessionVerification(id2)

    await post('/api/app/new', { id, id2 })
    return { id, id2 }
  }, [])

  const loadMessages = useCallback(
    async (sid?: string, sv?: string) => {
      const id = sid || sessionId
      const id2 = sv || sessionVerification
      if (!id || !id2) return

      try {
        const msgs = await post<Message[]>('/api/app/messages', {
          id,
          id2,
          from: lastEpochRef.current,
        })
        if (msgs && msgs.length > 0) {
          setMessages((prev) => {
            const existingIds = new Set(prev.map((m) => m.Id))
            const newMsgs = msgs.filter((m) => !existingIds.has(m.Id))
            return [...newMsgs, ...prev]
          })
          const maxEpoch = Math.max(...msgs.map((m) => m.DateSentEpoch))
          if (maxEpoch > lastEpochRef.current) {
            lastEpochRef.current = maxEpoch
          }
        }
      } catch {}
    },
    [sessionId, sessionVerification]
  )

  return {
    sessionId,
    sessionVerification,
    connectionId,
    setConnectionId,
    messages,
    setMessages,
    shareToken,
    setShareToken,
    numDevices,
    setNumDevices,
    initSession,
    loadMessages,
  }
}
