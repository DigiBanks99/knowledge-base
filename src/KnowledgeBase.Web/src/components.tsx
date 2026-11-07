import { useState, type ReactNode } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Link } from 'react-router-dom'
import {
  ArrowLeft,
  Bot,
  ChevronRight,
  CircleAlert,
  ClipboardCheck,
  Compass,
  FileText,
  Inbox,
  LibraryBig,
  LoaderCircle,
  PenLine,
  ScanSearch,
  Stamp,
  Waypoints,
  Zap,
  type LucideIcon,
} from 'lucide-react'
import type { AgentName, AgentRun } from './api'
import { formatDate, formatDuration, resolveWikiPath } from './format'

/** Each agent's icon, so a run, a setting or an answer can be told apart at a glance. */
const agentIcons: Record<AgentName, LucideIcon> = {
  FactFinder: ScanSearch,
  ThreadWeaver: Waypoints,
  Reviewer: ClipboardCheck,
  Editor: PenLine,
  Librarian: LibraryBig,
  Pleb: Compass,
  Clerk: Stamp,
}

export function AgentIcon({ agent }: { agent: string }) {
  const Icon = agentIcons[agent as AgentName] ?? Bot
  return (
    <span className="agent-icon" aria-hidden>
      <Icon />
    </span>
  )
}

export function Badge({ tone, children }: { tone: string; children: ReactNode }) {
  return <span className={`badge badge-${tone.toLowerCase()}`}>{children}</span>
}

export function Loading({ what }: { what: string }) {
  return (
    <p className="loading" role="status">
      <LoaderCircle className="spin" aria-hidden />
      Loading {what}…
    </p>
  )
}

export function ErrorMessage({ error }: { error: unknown }) {
  return (
    <p className="error" role="alert">
      <CircleAlert aria-hidden />
      <span>{error instanceof Error ? error.message : 'Something went wrong.'}</span>
    </p>
  )
}

export function Empty({
  children,
  icon: Icon = Inbox,
  hero = false,
}: {
  children: ReactNode
  icon?: LucideIcon
  /** A larger, borderless empty state for a pane that has nothing else in it. */
  hero?: boolean
}) {
  return (
    <div className={`empty${hero ? ' hero' : ''}`}>
      <span className="empty-icon" aria-hidden>
        <Icon />
      </span>
      <div>{children}</div>
    </div>
  )
}

/** A page's title with its icon, an optional line about what the page is for, and actions on the right. */
export function PageHeader({
  icon: Icon,
  title,
  description,
  actions,
}: {
  icon: LucideIcon
  title: string
  description?: ReactNode
  actions?: ReactNode
}) {
  return (
    <>
      <header className="page-header">
        <div className="page-title">
          <span className="page-icon" aria-hidden>
            <Icon />
          </span>
          <h1>{title}</h1>
        </div>
        {actions && <div className="header-actions">{actions}</div>}
      </header>
      {description && <p className="page-description">{description}</p>}
    </>
  )
}

export function SectionTitle({ icon: Icon, count, children }: { icon?: LucideIcon; count?: number; children: ReactNode }) {
  return (
    <h2 className="section-title">
      {Icon && <Icon aria-hidden />}
      {children}
      {count !== undefined && <span className="count">{count}</span>}
    </h2>
  )
}

/** Returns from a detail to its list on a phone; the list is beside the detail on wider screens. */
export function BackLink({ to, children }: { to: string; children: ReactNode }) {
  return (
    <Link to={to} className="back mobile-only">
      <ArrowLeft aria-hidden />
      {children}
    </Link>
  )
}

const writeTools = new Set(['CreateFile', 'PatchFile', 'MoveFile', 'DeleteFile', 'CreateDirectory'])

/** One agent run: status, timing, outcome, and (expandable) every tool call it made. */
export function RunCard({ run, defaultOpen = false }: { run: AgentRun; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  const changes = run.toolCalls.filter((call) => writeTools.has(call.tool) && call.succeeded)

  return (
    <article className="card run">
      <button type="button" className="run-header" aria-expanded={open} onClick={() => setOpen(!open)}>
        <AgentIcon agent={run.agent} />
        <span className="run-heading">
          <span className="item-title">
            <strong>{run.agent}</strong>
            <Badge tone={run.status}>{run.status}</Badge>
          </span>
          <span className="muted">
            {formatDate(run.startedAt)} · {formatDuration(run)} · {run.toolCalls.length} tool calls
            {changes.length > 0 && ` · ${changes.length} changes`}
          </span>
        </span>
        <ChevronRight className="toggle" aria-hidden />
      </button>
      <p className="trigger">
        <Zap size={12} aria-hidden />
        Triggered by {run.trigger.kind}: <code>{run.trigger.reference}</code>
      </p>
      {run.error && <ErrorMessage error={new Error(run.error)} />}
      {open && (run.summary || run.toolCalls.length > 0) && (
        <div className="run-body">
          {run.summary && (
            <div className="run-summary">
              <Markdown content={run.summary} />
            </div>
          )}
          {run.toolCalls.length > 0 && (
            <ol className="tool-calls">
              {run.toolCalls.map((call, index) => (
                <li
                  key={index}
                  className={[call.succeeded ? '' : 'failed', writeTools.has(call.tool) ? 'write' : ''].join(' ').trim()}
                >
                  <time>{new Date(call.at).toLocaleTimeString()}</time>{' '}
                  <code className={writeTools.has(call.tool) ? 'tool-write' : ''}>{call.tool}</code>{' '}
                  {call.target && <WikiTarget target={call.target} tool={call.tool} />}
                  {call.detail && <span className="muted"> — {call.detail}</span>}
                  {call.error && <span className="error"> — {call.error}</span>}
                </li>
              ))}
            </ol>
          )}
        </div>
      )}
    </article>
  )
}

// A search run's Note line names a research note in the notes inbox, not a wiki page.
const nonWikiTools = new Set(['DeleteFile', 'Note'])

function WikiTarget({ target, tool }: { target: string; tool: string }) {
  if (/^https?:\/\//i.test(target)) {
    return (
      <a href={target} target="_blank" rel="noreferrer">
        {target}
      </a>
    )
  }
  const isWikiPage = !target.includes('→') && target.endsWith('.md') && !nonWikiTools.has(tool)
  return isWikiPage ? <WikiLink path={target} /> : <span>{target}</span>
}

/** A link to a page in the wiki browser, shown by its path. */
export function WikiLink({ path }: { path: string }) {
  return (
    <Link className="page-link" to={`/wiki?page=${encodeURIComponent(path)}`}>
      <FileText aria-hidden />
      {path}
    </Link>
  )
}

/**
 * Renders markdown. Relative .md links are treated as wiki links and open inside the wiki browser; `basePath` is the
 * page the content belongs to (links in Librarian answers are relative to the wiki root).
 */
export function Markdown({
  content,
  basePath = '',
  withoutImages = false,
}: {
  content: string
  basePath?: string
  /** Drops images, for content from the open web whose images would otherwise be loaded from third parties. */
  withoutImages?: boolean
}) {
  return (
    <div className="markdown">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        disallowedElements={withoutImages ? ['img'] : undefined}
        components={{
          a: ({ href, children }) => {
            const isExternal = !href || /^[a-z]+:/i.test(href) || href.startsWith('#')
            if (isExternal || !href.split('#')[0].endsWith('.md')) {
              return (
                <a href={href} target="_blank" rel="noreferrer">
                  {children}
                </a>
              )
            }
            const page = resolveWikiPath(basePath || 'index.md', href)
            return <Link to={`/wiki?page=${encodeURIComponent(page)}`}>{children}</Link>
          },
        }}
      >
        {content}
      </ReactMarkdown>
    </div>
  )
}
