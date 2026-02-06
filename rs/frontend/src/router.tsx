import { Routes, Route } from 'react-router'
import Layout from './components/Layout'
import Home from './pages/Home'
import Password from './pages/Password'
import Stats from './pages/Stats'
import Session from './pages/Session'
import Connect from './pages/Connect'
import LiteSession from './pages/LiteSession'
import LiteSessionDelete from './pages/LiteSessionDelete'
import SecureLine from './pages/SecureLine'
import NotFound from './pages/NotFound'

export default function AppRouter() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route path="/" element={<Home />} />
        <Route path="/password" element={<Password />} />
        <Route path="/stats" element={<Stats />} />
        <Route path="/app" element={<Session />} />
        <Route path="/app/connect" element={<Connect />} />
        <Route path="/app/lite/:id1/:id2" element={<LiteSession />} />
        <Route path="/app/lite/:id1/:id2/delete" element={<LiteSessionDelete />} />
        <Route path="/secure-line" element={<SecureLine />} />
        <Route path="*" element={<NotFound />} />
      </Route>
    </Routes>
  )
}
