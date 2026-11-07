import { Suspense, lazy, useEffect, useRef } from 'react'
import { useQuery } from '@tanstack/react-query'
import { NavLink, Navigate, Route, Routes, useLocation } from 'react-router-dom'
import {
  Activity,
  BookOpen,
  ClipboardCheck,
  FilePen,
  Feather,
  LayoutGrid,
  LogOut,
  MessagesSquare,
  ScrollText,
  Settings2,
  Sparkles,
  StickyNote,
  Telescope,
  TriangleAlert,
  type LucideIcon,
} from 'lucide-react'
import { api } from './api'
import { useLiveUpdates } from './useLiveUpdates'
import { Loading } from './components'
import { CapturePage } from './pages/CapturePage'
import { NotesPage } from './pages/NotesPage'
import { WikiPage } from './pages/WikiPage'
import { AskPage } from './pages/AskPage'

// The everyday sections load with the app; the rest load when first opened, keeping the first load small on a phone.
const ReviewsPage = lazy(() => import('./pages/ReviewsPage').then((module) => ({ default: module.ReviewsPage })))
const EditsPage = lazy(() => import('./pages/EditsPage').then((module) => ({ default: module.EditsPage })))
const StatusPage = lazy(() => import('./pages/StatusPage').then((module) => ({ default: module.StatusPage })))
const LogsPage = lazy(() => import('./pages/LogsPage').then((module) => ({ default: module.LogsPage })))
const DenyListPage = lazy(() => import('./pages/DenyListPage').then((module) => ({ default: module.DenyListPage })))
const ResearchPage = lazy(() => import('./pages/ResearchPage').then((module) => ({ default: module.ResearchPage })))
const TopicsPage = lazy(() => import('./pages/TopicsPage').then((module) => ({ default: module.TopicsPage })))
const TopicPage = lazy(() => import('./pages/TopicPage').then((module) => ({ default: module.TopicPage })))
const SettingsPage = lazy(() => import('./pages/SettingsPage').then((module) => ({ default: module.SettingsPage })))

interface Section {
  to: string
  label: string
  icon: LucideIcon
}

/** The everyday sections: the phone's tab bar and the top of the sidebar. */
const primary: Section[] = [
  { to: '/capture', label: 'Capture', icon: Feather },
  { to: '/notes', label: 'Notes', icon: StickyNote },
  { to: '/wiki', label: 'Wiki', icon: BookOpen },
  { to: '/ask', label: 'Ask', icon: MessagesSquare },
]

const agentSections: Section[] = [
  { to: '/reviews', label: 'Reviews', icon: ClipboardCheck },
  { to: '/edits', label: 'Edits', icon: FilePen },
  { to: '/research', label: 'Research', icon: Telescope },
]

const systemSections: Section[] = [
  { to: '/settings', label: 'Settings', icon: Settings2 },
  { to: '/logs', label: 'Logs', icon: ScrollText },
  { to: '/status', label: 'Status', icon: Activity },
]

/** What a phone keeps behind the tab bar's More button. */
const secondary = [...agentSections, ...systemSections]

export function App() {
  const connected = useLiveUpdates()
  const me = useQuery({ queryKey: ['me'], queryFn: api.me, retry: false, staleTime: Infinity })
  const status = useQuery({ queryKey: ['status'], queryFn: api.status })
  const degraded = status.data?.isDegraded === true
  const name = me.data?.name

  return (
    <div className="app">
      <Sidebar
        connected={connected}
        degraded={degraded}
        name={name}
        statusTitle={statusTitle(status.data?.isDegraded, status.isError)}
      />
      <header className="topbar">
        <Brand />
        <span className="spacer" />
        <LiveIndicator connected={connected} />
        {degraded && (
          <NavLink to="/status" className="status-alert" title="Something is degraded" aria-label="Something is degraded">
            <TriangleAlert />
          </NavLink>
        )}
        {name && <Avatar name={name} />}
      </header>
      <main>
        <Suspense
          fallback={
            <section className="page">
              <Loading what="the page" />
            </section>
          }
        >
          <Routes>
            <Route path="/" element={<Navigate to="/capture" replace />} />
            <Route path="/capture" element={<CapturePage name={name} />} />
            <Route path="/notes" element={<NotesPage />} />
            <Route path="/reviews" element={<ReviewsPage />} />
            <Route path="/edits" element={<EditsPage />} />
            <Route path="/wiki" element={<WikiPage />} />
            <Route path="/ask" element={<AskPage />} />
            <Route path="/ask/:conversationId" element={<AskPage />} />
            <Route path="/research" element={<ResearchPage />}>
              <Route index element={<Navigate to="topics" replace />} />
              <Route path="topics" element={<TopicsPage />} />
              <Route path="topics/:slug" element={<TopicPage />} />
              {/* Each topic's page now shows its curations, so the old Curation tab leads to the topics. */}
              <Route path="curation" element={<Navigate to="/research/topics" replace />} />
              <Route path="deny-list" element={<DenyListPage />} />
            </Route>
            <Route path="/settings" element={<SettingsPage />} />
            <Route path="/status" element={<StatusPage />} />
            <Route path="/logs" element={<LogsPage />} />
            <Route path="*" element={<Navigate to="/capture" replace />} />
          </Routes>
        </Suspense>
      </main>
      <TabBar degraded={degraded} name={name} />
    </div>
  )
}

function Brand() {
  return (
    <NavLink to="/capture" className="brand">
      <span className="brand-mark" aria-hidden>
        <Sparkles />
      </span>
      Knowledge Base
    </NavLink>
  )
}

function LiveIndicator({ connected }: { connected: boolean }) {
  return (
    <span className={`live ${connected ? 'on' : 'off'}`} title={connected ? 'Live updates on' : 'Reconnecting…'}>
      <span className="live-label">{connected ? 'Live' : 'Reconnecting'}</span>
    </span>
  )
}

function Avatar({ name }: { name: string }) {
  return (
    <span className="avatar" title={name} aria-hidden>
      {initials(name)}
    </span>
  )
}

function Sidebar({
  connected,
  degraded,
  name,
  statusTitle,
}: {
  connected: boolean
  degraded: boolean
  name: string | undefined
  statusTitle: string
}) {
  return (
    <nav className="sidebar" aria-label="Sections">
      <Brand />
      <NavGroup label="Workspace" sections={primary} />
      <NavGroup label="Agents" sections={agentSections} />
      <NavGroup label="System" sections={systemSections} degraded={degraded} statusTitle={statusTitle} />
      <div className="sidebar-footer">
        <LiveIndicator connected={connected} />
        {name && (
          <div className="sidebar-user">
            <Avatar name={name} />
            <span className="name">{name}</span>
            <a className="sign-out" href="/auth/logout" title="Sign out" aria-label="Sign out">
              <LogOut />
            </a>
          </div>
        )}
      </div>
    </nav>
  )
}

function NavGroup({
  label,
  sections,
  degraded = false,
  statusTitle,
}: {
  label: string
  sections: Section[]
  degraded?: boolean
  statusTitle?: string
}) {
  return (
    <div className="nav-group">
      <span className="nav-group-label">{label}</span>
      {sections.map(({ to, label: text, icon: Icon }) => (
        <NavLink key={to} to={to} title={to === '/status' ? statusTitle : undefined}>
          <Icon aria-hidden />
          {text}
          {to === '/status' && degraded && (
            <span className="count" aria-label="degraded">
              <TriangleAlert />
            </span>
          )}
        </NavLink>
      ))}
    </div>
  )
}

/** The phone's navigation: the everyday sections, and a sheet with the rest. */
function TabBar({ degraded, name }: { degraded: boolean; name: string | undefined }) {
  const sheet = useRef<HTMLDialogElement>(null)
  const location = useLocation()
  const inSecondary = secondary.some((section) => location.pathname.startsWith(section.to))

  // Choosing a section in the sheet navigates; the sheet then gets out of the way.
  useEffect(() => {
    sheet.current?.close()
  }, [location.pathname])

  return (
    <>
      <nav className="tabbar" aria-label="Sections">
        {primary.map(({ to, label, icon: Icon }) => (
          <NavLink key={to} to={to}>
            <Icon aria-hidden />
            {label}
          </NavLink>
        ))}
        <button
          type="button"
          className={inSecondary ? 'active' : undefined}
          aria-haspopup="dialog"
          onClick={() => sheet.current?.showModal()}
        >
          <LayoutGrid aria-hidden />
          More
          {degraded && <span className="dot" aria-label="Something is degraded" />}
        </button>
      </nav>
      <dialog
        ref={sheet}
        className="sheet"
        aria-label="More sections"
        // A click on the backdrop lands on the dialog itself, not on anything inside it.
        onClick={(event) => {
          if (event.target === event.currentTarget) event.currentTarget.close()
        }}
      >
        <div className="sheet-handle" aria-hidden />
        <div className="sheet-grid">
          {secondary.map(({ to, label, icon: Icon }) => (
            <NavLink key={to} to={to}>
              <Icon aria-hidden />
              {label}
              {to === '/status' && degraded && <span className="dot" aria-label="degraded" />}
            </NavLink>
          ))}
        </div>
        {name && (
          <div className="sheet-user">
            <Avatar name={name} />
            <span className="name">{name}</span>
            <a className="sign-out" href="/auth/logout">
              <LogOut aria-hidden />
              Sign out
            </a>
          </div>
        )}
      </dialog>
    </>
  )
}

function initials(name: string): string {
  const parts = name.trim().split(/\s+/)
  const letters = parts.length > 1 ? parts[0][0] + parts[parts.length - 1][0] : parts[0].slice(0, 2)
  return letters.toUpperCase()
}

function statusTitle(isDegraded: boolean | undefined, isError: boolean): string {
  if (isError) return 'The status could not be loaded'
  if (isDegraded === undefined) return 'Loading the status…'
  return isDegraded ? 'Something is degraded' : 'Everything is healthy'
}
