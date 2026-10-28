import { useState, type ReactNode } from 'react'
import ReactMarkdown from 'react-markdown'
import remarkGfm from 'remark-gfm'
import { Link } from 'react-router-dom'
import type { AgentRun } from './api'
import { formatDate, formatDuration, resolveWikiPath } from './format'

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
