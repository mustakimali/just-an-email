import { Link } from 'react-router'

export default function NotFound() {
  return (
    <div className="gradient-bg min-h-[60vh] flex items-center justify-center px-4">
      <div className="text-center">
        <h1 className="text-white text-6xl font-bold mb-4">404</h1>
        <p className="text-gray-300 text-lg mb-8">Page not found</p>
        <Link to="/" className="btn btn-primary px-8 py-3">
          Go Home
        </Link>
      </div>
    </div>
  )
}
