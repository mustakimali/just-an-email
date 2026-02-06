import { useParams, useNavigate } from 'react-router'
import { post } from '../api/client'
import { toast } from 'sonner'

export default function LiteSessionDelete() {
  const { id1, id2 } = useParams<{ id1: string; id2: string }>()
  const navigate = useNavigate()

  const handleErase = async () => {
    if (!id1 || !id2) return

    try {
      await post('/api/app/lite/erase-session', { id: id1, id2 })
      toast.success('Session erased')
      navigate('/')
    } catch {
      toast.error('Failed to erase session')
    }
  }

  return (
    <div className="gradient-bg min-h-[60vh] flex items-center justify-center px-4">
      <div className="max-w-md w-full text-center">
        <h1 className="text-white text-3xl font-bold mb-4">Erase Session?</h1>
        <p className="text-gray-300 mb-8">
          All messages and files in this session will be permanently deleted. This cannot be undone.
        </p>
        <div className="flex flex-col gap-3">
          <button onClick={handleErase} className="btn btn-danger py-3 text-lg">
            Erase Everything
          </button>
          <button
            onClick={() => navigate(`/app/lite/${id1}/${id2}`)}
            className="btn border border-white/20 py-3"
          >
            Go Back
          </button>
        </div>
      </div>
    </div>
  )
}
