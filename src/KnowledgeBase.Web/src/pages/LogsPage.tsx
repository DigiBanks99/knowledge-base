import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { api, type LogEntry, type LogLevel } from '../api'
import { Badge, Empty, ErrorMessage, Loading } from '../components'

/** How often the page asks again. Logs are not files the live updates stream watches, so the page polls. */
const RefreshMilliseconds = 5000

const levelOptions: LogLevel[] = ['Information', 'Warning', 'Error']

function levelTone(level: LogLevel): string {
  switch (level) {
    case 'Critical':
    case 'Error':
      return 'failed'
    case 'Warning':
      return 'degraded'
    case 'Information':
      return 'idle'
    default:
      return 'muted'
  }
}

/** Seconds are shown, unlike elsewhere, because log lines from one search are often a second apart. */
function formatLogTime(value: string): string {
  return new Date(value).toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'medium' })
}

/** The entries as plain text, one per message, so they can be pasted into a bug report or a chat. */
function asText(entries: LogEntry[]): string {
  return entries
    .map((entry) => {
      const header = `${entry.at} ${entry.level} ${entry.category}: ${entry.message}`
      return entry.exception ? `${header}\n${entry.exception}` : header
    })
    .join('\n\n')
}

export function LogsPage() {
  const [level, setLevel] = useState<LogLevel>('Information')
  const [search, setSearch] = useState('')
  const [copied, setCopied] = useState(false)

  const logs = useQuery({
    queryKey: ['logs', level, search],
    queryFn: () => api.logs({ level, search, limit: 300 }),
    refetchInterval: RefreshMilliseconds,
  })

  async function copy() {
    if (!logs.data) return
    try {
      await navigator.clipboard.writeText(asText(logs.data))
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // The clipboard can be unavailable (an insecure page or a denied permission); the entries stay on screen to copy by hand.
    }
  }

  return (
    <section className="page">
      <h1>Logs</h1>
      <p className="muted">
        Recent messages from the API, newest first. They are kept in memory and refresh every few seconds; a restart
        clears them. Searches and agent runs are kept on disk, and each research topic's page shows its history.
      </p>

      <div className="log-controls">
        <label className="small">
          Level{' '}
          <select value={level} onChange={(event) => setLevel(event.target.value as LogLevel)}>
            {levelOptions.map((option) => (
              <option key={option} value={option}>
                {option} and above
              </option>
            ))}
          </select>
        </label>
        <label className="small log-search">
          Search{' '}
          <input
            type="search"
            value={search}
            placeholder="Message, category or exception"
            onChange={(event) => setSearch(event.target.value)}
          />
        </label>
        <button type="button" className="secondary" onClick={copy} disabled={!logs.data?.length}>
          {copied ? 'Copied' : 'Copy shown'}
        </button>
      </div>

      {logs.isPending && <Loading what="logs" />}
      {logs.isError && <ErrorMessage error={logs.error} />}
      {logs.data?.length === 0 && <Empty>No messages match. Try a lower level or a different search.</Empty>}
      {logs.data && logs.data.length > 0 && (
        <ul className="signals">
          {logs.data.map((entry) => (
            <li key={entry.sequence} className="card log-entry">
              <div className="item-title">
                <Badge tone={levelTone(entry.level)}>{entry.level}</Badge>
                <span className="muted small">{formatLogTime(entry.at)}</span>
                <span className="small log-category">{entry.category}</span>
              </div>
              <p className="log-message">{entry.message}</p>
              {entry.exception && (
                <details>
                  <summary className="small">Exception</summary>
                  <pre className="log-exception">{entry.exception}</pre>
                </details>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}
