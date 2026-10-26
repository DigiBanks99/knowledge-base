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
import { StatusPage } from './pages/StatusPage'
import { DenyListPage } from './pages/DenyListPage'
import { ResearchPage } from './pages/ResearchPage'
import { TopicsPage } from './pages/TopicsPage'

const sections = [
  { to: '/capture', label: 'Capture' },
  { to: '/notes', label: 'Notes' },
  { to: '/reviews', label: 'Reviews' },
  { to: '/edits', label: 'Edits' },
  { to: '/wiki', label: 'Wiki' },
  { to: '/ask', label: 'Ask' },
  { to: '/research', label: 'Research' },
]

export function App() {
  const connected = useLiveUpdates()
  const me = useQuery({ queryKey: ['me'], queryFn: api.me, retry: false, staleTime: Infinity })
  const status = useQuery({ queryKey: ['status'], queryFn: api.status })

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
        <NavLink to="/status" title={statusTitle(status.data?.isDegraded, status.isError)}>
          Status
          {status.data?.isDegraded && <span className="status-badge">!</span>}
        </NavLink>
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
          <Route path="/research" element={<ResearchPage />}>
            <Route index element={<Navigate to="topics" replace />} />
            <Route path="topics" element={<TopicsPage />} />
            <Route path="deny-list" element={<DenyListPage />} />
          </Route>
          <Route path="/status" element={<StatusPage />} />
          <Route path="*" element={<Navigate to="/capture" replace />} />
        </Routes>
      </main>
    </div>
  )
}

function statusTitle(isDegraded: boolean | undefined, isError: boolean): string {
  if (isError) return 'The status could not be loaded'
  if (isDegraded === undefined) return 'Loading the status…'
  return isDegraded ? 'Something is degraded' : 'Everything is healthy'
}
