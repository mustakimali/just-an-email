import { Link, useNavigate } from 'react-router'

export default function Home() {
  const navigate = useNavigate()

  return (
    <div>
      <div className="gradient-bg text-center py-16 px-4">
        <h1 className="text-white text-4xl font-bold mb-4">Just An Email</h1>
        <p className="text-gray-300 text-lg mb-8 max-w-xl mx-auto">
          Share text and files between your devices securely. End-to-end encrypted. No account
          needed.
        </p>
        <div className="flex flex-col sm:flex-row gap-4 justify-center">
          <button
            onClick={() => navigate('/app')}
            className="btn btn-primary text-2xl px-8 py-4"
          >
            Start Sharing
          </button>
          <button
            onClick={() => navigate('/app/connect')}
            className="btn border border-white/20 px-8 py-4"
          >
            Connect with PIN
          </button>
        </div>
      </div>
      <div className="max-w-4xl mx-auto px-4 py-12">
        <div className="grid md:grid-cols-3 gap-8">
          <div className="text-center">
            <div className="text-3xl mb-3">🔒</div>
            <h3 className="font-bold mb-2">End-to-End Encrypted</h3>
            <p className="text-sm text-gray-600">
              Messages and files are encrypted in your browser. Nobody can read them, not even us.
            </p>
          </div>
          <div className="text-center">
            <div className="text-3xl mb-3">⚡</div>
            <h3 className="font-bold mb-2">Instant</h3>
            <p className="text-sm text-gray-600">
              No sign up, no download. Just open in any browser and start sharing.
            </p>
          </div>
          <div className="text-center">
            <div className="text-3xl mb-3">🗑️</div>
            <h3 className="font-bold mb-2">Auto-Delete</h3>
            <p className="text-sm text-gray-600">
              Sessions are automatically erased when all devices disconnect or after 24 hours.
            </p>
          </div>
        </div>
        <div className="mt-12 text-center">
          <Link to="/password" className="text-blue-600 hover:underline text-sm">
            Need to share a password? Use our quick share link →
          </Link>
        </div>
      </div>
    </div>
  )
}
