import { useState, useRef } from 'react'
import TextareaAutosize from 'react-textarea-autosize'

interface Props {
  onSend: (text: string, file?: File) => void
  disabled?: boolean
}

export default function Composer({ onSend, disabled }: Props) {
  const [text, setText] = useState('')
  const [selectedFile, setSelectedFile] = useState<File | null>(null)
  const fileRef = useRef<HTMLInputElement>(null)

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault()
    if (!text.trim() && !selectedFile) return
    onSend(text, selectedFile || undefined)
    setText('')
    setSelectedFile(null)
    if (fileRef.current) fileRef.current.value = ''
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && e.ctrlKey) {
      e.preventDefault()
      handleSubmit(e)
    }
  }

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) {
      setSelectedFile(file)
      setText(file.name)
    }
  }

  const clearFile = () => {
    setSelectedFile(null)
    setText('')
    if (fileRef.current) fileRef.current.value = ''
  }

  return (
    <form onSubmit={handleSubmit} className="flex gap-2 items-end">
      <div className="flex-1 relative">
        <TextareaAutosize
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={handleKeyDown}
          placeholder="Type a message..."
          className="w-full border border-gray-300 rounded px-3 py-2 text-sm focus:outline-none focus:border-blue-500"
          readOnly={!!selectedFile}
          minRows={1}
          maxRows={6}
        />
        {selectedFile && (
          <button
            type="button"
            onClick={clearFile}
            className="absolute right-2 top-2 text-gray-400 hover:text-red-500"
          >
            ✕
          </button>
        )}
      </div>
      <input
        ref={fileRef}
        type="file"
        onChange={handleFileChange}
        className="hidden"
      />
      <button
        type="button"
        onClick={() => fileRef.current?.click()}
        className="px-3 py-2 border border-gray-300 rounded text-gray-600 hover:bg-gray-100 text-sm"
        title="Attach file"
      >
        📎
      </button>
      <button
        type="submit"
        disabled={disabled || (!text.trim() && !selectedFile)}
        className="px-4 py-2 bg-green-600 text-white rounded text-sm hover:bg-green-700 disabled:opacity-50 disabled:cursor-not-allowed"
      >
        Send
      </button>
    </form>
  )
}
