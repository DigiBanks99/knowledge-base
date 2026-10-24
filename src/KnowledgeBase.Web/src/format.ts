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

/** Resolves a relative markdown link against the page it appears on, e.g. ../people/index.md from me/index.md. */
export function resolveWikiPath(fromPage: string, href: string): string {
  const parts = fromPage.split('/').slice(0, -1)
  for (const segment of href.split('#')[0].split('/')) {
    if (segment === '..') parts.pop()
    else if (segment !== '.' && segment !== '') parts.push(segment)
  }
  return parts.join('/')
}
