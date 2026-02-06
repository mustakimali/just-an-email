import { useState } from 'react'
import { useNavigate } from 'react-router'
import { post } from '../api/client'
import { toast } from 'sonner'

export default function Connect() {
  const [token, setToken] = useState('')
  const navigate = useNavigate()

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!token.trim()) return

    try {
      const result = await post<{
        sessionId: string
        sessionVerification: string
        isLiteSession: boolean
      }>('/api/app/connect', { token: parseInt(token) })

      if (result.isLiteSession) {
        navigate(`/app/lite/${result.sessionId}/${result.sessionVerification}`)
      } else {
        window.location.hash = result.sessionId + result.sessionVerification
        navigate('/app')
      }
    } catch {
      toast.error('Invalid PIN!')
    }
  }

  return (
    <div className="gradient-bg min-h-[60vh] flex items-center justify-center px-4">
      <div className="max-w-md w-full text-center">
        <h1 className="text-white text-3xl font-bold mb-2">Connect to a Session</h1>
        <p className="text-gray-300 mb-8">Enter the PIN shown on the other device</p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <input
            type="number"
            value={token}
            onChange={(e) => setToken(e.target.value)}
            placeholder="Enter PIN"
            className="w-full px-4 py-4 text-center text-3xl font-mono rounded focus:outline-none tracking-widest"
            autoFocus
            maxLength={6}
          />
          <button type="submit" className="w-full btn btn-success py-3 text-lg">
            Connect
          </button>
        </form>
        <div className="mt-6">
          <a
            href="/app/connect"
            onClick={(e) => {
              e.preventDefault()
              const noJs = confirm('Connect without JavaScript (lite mode)?')
              if (noJs) {
                // Lite mode with form post
              }
            }}
            className="text-white/50 text-xs"
          >
            No JavaScript? Use lite mode
          </a>
        </div>
      </div>
    </div>
  )
}
