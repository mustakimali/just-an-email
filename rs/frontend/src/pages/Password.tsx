import { useState } from 'react'
import { useNavigate } from 'react-router'
import { generateGuid } from '../crypto/helpers'

export default function Password() {
  const [password, setPassword] = useState('')
  const navigate = useNavigate()

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!password.trim()) return

    const id = generateGuid()
    const id2 = generateGuid()
    const encoded = btoa(password)
    navigate(`/app#${id}${id2}/${encoded}`)
  }

  return (
    <div className="gradient-bg min-h-[60vh] flex items-center justify-center px-4">
      <div className="max-w-md w-full">
        <h1 className="text-white text-3xl font-bold text-center mb-2">Share a Password</h1>
        <p className="text-gray-300 text-center mb-8">
          Enter your password below. A secure sharing session will be created automatically.
        </p>
        <form onSubmit={handleSubmit} className="space-y-4">
          <input
            type="text"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            placeholder="Enter password or secret..."
            className="w-full px-4 py-3 rounded text-lg focus:outline-none"
            autoFocus
          />
          <button
            type="submit"
            className="w-full btn btn-success py-3 text-lg"
            disabled={!password.trim()}
          >
            Share Securely
          </button>
        </form>
      </div>
    </div>
  )
}
