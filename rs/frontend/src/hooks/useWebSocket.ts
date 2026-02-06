import { useEffect, useRef, useCallback, useState } from 'react'
import { WsClient } from '../api/ws'
import type { ServerMessage } from '../types'

export function useWebSocket(path: string) {
  const wsRef = useRef<WsClient | null>(null)
  const [connected, setConnected] = useState(false)
  const [lastMessage, setLastMessage] = useState<ServerMessage | null>(null)

  useEffect(() => {
    const ws = new WsClient(path)
    wsRef.current = ws

    ws.onMessage((msg) => {
      setLastMessage(msg)
      if (msg.type === 'connected') setConnected(true)
    })

    ws.connect()

    return () => {
      ws.close()
      wsRef.current = null
    }
  }, [path])

  const send = useCallback((msg: Record<string, unknown>) => {
    wsRef.current?.send(msg)
  }, [])

  return { send, connected, lastMessage, ws: wsRef }
}
