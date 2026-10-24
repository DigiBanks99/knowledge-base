import { useState, type ReactNode } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Link } from 'react-router-dom'
import type { AgentRun } from './api'

export function formatDate(value: string | null | undefined): string {
  if (!value) return '—'
  return new Date(value).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}

export function formatDuration(run: AgentRun): string {
  const end = run.completedAt ? new Date(run.completedAt).getTime() : Date.now()
  const seconds = Math.max(0, Math.round((end - new Date(run.startedAt).getTime()) / 1000))
  return seconds < 60 ? `${seconds}s` : `${Math.floor(seconds / 60)}m ${seconds % 60}s`
}

export function Badge({ tone, children }: { tone: string; children: ReactNode }) {
  return <span className={`badge badge-${tone.toLowerCase()}`}>{children}</span>
}

export function Loading({ what }: { what: string }) {
  return <p className="muted">Loading {what}…</p>
}

export function ErrorMessage({ error }: { error: unknown }) {
  return <p className="error">{error instanceof Error ? error.message : 'Something went wrong.'}</p>
}

export function Empty({ children }: { children: ReactNode }) {
  return <p className="muted empty">{children}</p>
}

const writeTools = new Set(['CreateFile', 'PatchFile', 'MoveFile', 'DeleteFile', 'CreateDirectory'])

/** One agent run: status, timing, outcome, and (expandable) every tool call it made. */
export function RunCard({ run, defaultOpen = false }: { run: AgentRun; defaultOpen?: boolean }) {
  const [open, setOpen] = useState(defaultOpen)
  const changes = run.toolCalls.filter((call) => writeTools.has(call.tool) && call.succeeded)

  return (
    <article className="card run">
      <header className="run-header" onClick={() => setOpen(!open)}>
        <Badge tone={run.status}>{run.status}</Badge>
        <strong>{run.agent}</strong>
        <span className="muted">
          {formatDate(run.startedAt)} · {formatDuration(run)} · {run.toolCalls.length} tool calls
          {changes.length > 0 && ` · ${changes.length} changes`}
        </span>
        <span className="toggle">{open ? '▾' : '▸'}</span>
      </header>
      <p className="muted small">
        Triggered by {run.trigger.kind}: <code>{run.trigger.reference}</code>
      </p>
      {run.error && <p className="error">{run.error}</p>}
      {open && (
        <>
          {run.summary && (
            <div className="run-summary">
              <Markdown content={run.summary} />
            </div>
          )}
          {run.toolCalls.length > 0 && (
            <ol className="tool-calls">
              {run.toolCalls.map((call, index) => (
                <li key={index} className={call.succeeded ? '' : 'failed'}>
                  <span className="muted">{new Date(call.at).toLocaleTimeString()}</span>{' '}
                  <code className={writeTools.has(call.tool) ? 'tool-write' : ''}>{call.tool}</code>{' '}
                  {call.target && <WikiTarget target={call.target} tool={call.tool} />}
                  {call.error && <span className="error"> — {call.error}</span>}
                </li>
              ))}
            </ol>
          )}
        </>
      )}
    </article>
  )
}

function WikiTarget({ target, tool }: { target: string; tool: string }) {
  const isWikiPage = !target.includes('→') && target.endsWith('.md') && tool !== 'DeleteFile'
  return isWikiPage ? <Link to={`/wiki?page=${encodeURIComponent(target)}`}>{target}</Link> : <span>{target}</span>
}

/** Resolves a relative markdown link against the page it appears on, e.g. ../people/index.md from me/index.md. */
export function resolveWikiPath(fromPage: string, href: string): string {
  const parts = fromPage.split('/').slice(0, -1)
  for (const segment of href.split('#')[0].split('/')) {
    if (segment === '..') parts.pop()
    else if (segment !== '.' && segment !== '') parts.push(segment)
  }
  return parts.join('/')
}

/**
 * Renders markdown. Relative .md links are treated as wiki links and open inside the wiki browser; `basePath` is the
 * page the content belongs to (links in Librarian answers are relative to the wiki root).
 */
export function Markdown({ content, basePath = '' }: { content: string; basePath?: string }) {
  return (
    <div className="markdown">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
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
