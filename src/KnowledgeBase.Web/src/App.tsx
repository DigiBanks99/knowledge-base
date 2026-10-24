import { useQuery } from '@tanstack/react-query'
import { NavLink, Navigate, Route, Routes } from 'react-router-dom'
import { api } from './api'
import { useLiveUpdates } from './useLiveUpdates'
import { CapturePage } from './pages/CapturePage'
import { NotesPage } from './pages/NotesPage'
import { ReviewsPage } from './pages/ReviewsPage'
import { EditsPage } from './pages/EditsPage'
import { WikiPage } from './pages/WikiPage'
import { AskPage } from './pages/AskPage'

const sections = [
  { to: '/capture', label: 'Capture' },
  { to: '/notes', label: 'Notes' },
  { to: '/reviews', label: 'Reviews' },
  { to: '/edits', label: 'Edits' },
  { to: '/wiki', label: 'Wiki' },
  { to: '/ask', label: 'Ask' },
]

export function App() {
  const connected = useLiveUpdates()
  const me = useQuery({ queryKey: ['me'], queryFn: api.me, retry: false, staleTime: Infinity })

  return (
    <div className="app">
      <nav className="nav">
        <span className="brand">Knowledge Base</span>
        {sections.map((section) => (
          <NavLink key={section.to} to={section.to}>
            {section.label}
          </NavLink>
        ))}
        <span className="spacer" />
        <span className={`live ${connected ? 'on' : 'off'}`} title={connected ? 'Live updates on' : 'Reconnecting…'}>
          ●
        </span>
        {me.data && (
          <>
            <span className="muted small">{me.data.name}</span>
            <a className="small" href="/auth/logout">
              Sign out
            </a>
          </>
        )}
      </nav>
      <main>
        <Routes>
          <Route path="/" element={<Navigate to="/capture" replace />} />
          <Route path="/capture" element={<CapturePage />} />
          <Route path="/notes" element={<NotesPage />} />
          <Route path="/reviews" element={<ReviewsPage />} />
          <Route path="/edits" element={<EditsPage />} />
          <Route path="/wiki" element={<WikiPage />} />
          <Route path="/ask" element={<AskPage />} />
          <Route path="/ask/:conversationId" element={<AskPage />} />
          <Route path="*" element={<Navigate to="/capture" replace />} />
        </Routes>
      </main>
    </div>
  )
}
