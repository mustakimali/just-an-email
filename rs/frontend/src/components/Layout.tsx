import { Outlet, Link } from 'react-router'

export default function Layout() {
  return (
    <div className="min-h-screen flex flex-col">
      <nav className="gradient-bg border-t-3 border-green-500 px-4 py-3">
        <div className="max-w-4xl mx-auto flex items-center justify-between">
          <Link to="/" className="text-white font-bold text-lg no-underline">
            Just An Email
          </Link>
          <div className="flex gap-4">
            <Link to="/stats" className="text-white/70 text-sm hover:text-white no-underline">
              Stats
            </Link>
            <a
              href="https://github.com/nicollasricas/just-an-email"
              target="_blank"
              rel="noreferrer"
              className="text-white/70 text-sm hover:text-white no-underline"
            >
              GitHub
            </a>
          </div>
        </div>
      </nav>
      <main className="flex-1">
        <Outlet />
      </main>
      <footer className="text-center text-xs text-gray-400 uppercase py-4">
        <a href="https://tnxfr.com" className="text-gray-500 no-underline">
          tnxfr.com
        </a>
      </footer>
    </div>
  )
}
