import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Check, Copy, ScrollText, Search, SearchX } from 'lucide-react'
import { api, type LogEntry, type LogLevel } from '../api'
import { Badge, Empty, ErrorMessage, Loading, PageHeader } from '../components'

/** How often the page asks again. Logs are not files the live updates stream watches, so the page polls. */
const RefreshMilliseconds = 5000

const levelOptions: LogLevel[] = ['Information', 'Warning', 'Error']

/** The parts of the app that log, named after their folders in the API. */
const areaOptions = ['Research', 'Review', 'Runs', 'Notes', 'Facts', 'Wiki', 'Conversations', 'Agents', 'Files', 'App', 'Framework']

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
      const header = `${entry.at} ${entry.level} ${entry.area} ${entry.category}: ${entry.message}`
      return entry.exception ? `${header}\n${entry.exception}` : header
    })
    .join('\n\n')
}

export function LogsPage() {
  const [level, setLevel] = useState<LogLevel>('Information')
  const [area, setArea] = useState('')
  const [search, setSearch] = useState('')
  const [copied, setCopied] = useState(false)

  const logs = useQuery({
    queryKey: ['logs', level, area, search],
    queryFn: () => api.logs({ level, area: area || undefined, search, limit: 300 }),
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
        <div>
        <PageHeader
          icon={ScrollText}
          title="Logs"
          description="Recent messages from the API, newest first, kept for five days and refreshed every few seconds. Tap a value such as a topic or run id to see every message about it. Searches and agent runs are also kept in full, and each research topic's page shows its history."
        />

        <div className="card log-controls">
          <label>
            Level
            <select value={level} onChange={(event) => setLevel(event.target.value as LogLevel)}>
              {levelOptions.map((option) => (
                <option key={option} value={option}>
                  {option} and above
                </option>
              ))}
            </select>
          </label>
          <label>
            Area
            <select value={area} onChange={(event) => setArea(event.target.value)}>
              <option value="">All areas</option>
              {areaOptions.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>
          <label className="log-search">
            Search
            <span className="search-field">
              <Search aria-hidden />
              <input
                type="search"
                value={search}
                placeholder="Message, topic, run id or exception"
                onChange={(event) => setSearch(event.target.value)}
              />
            </span>
          </label>
          <button type="button" className="secondary" onClick={copy} disabled={!logs.data?.length}>
            {copied ? <Check aria-hidden /> : <Copy aria-hidden />}
            {copied ? 'Copied' : 'Copy shown'}
          </button>
        </div>

        {logs.isPending && <Loading what="logs" />}
        {logs.isError && <ErrorMessage error={logs.error} />}
        {logs.data?.length === 0 && (
          <Empty icon={SearchX}>No messages match. Try a lower level, another area or a different search.</Empty>
        )}
        {logs.data && logs.data.length > 0 && (
          <ul className="log-list">
            {logs.data.map((entry) => (
              <li key={entry.sequence} className={`card log-entry level-${entry.level.toLowerCase()}`}>
                <div className="item-title">
                  <Badge tone={levelTone(entry.level)}>{entry.level}</Badge>
                  <button
                    type="button"
                    className="link log-area"
                    title={`Show only ${entry.area}`}
                    onClick={() => setArea(entry.area)}
                  >
                    {entry.area}
                  </button>
                  <time>{formatLogTime(entry.at)}</time>
                </div>
                <p className="log-message">{entry.message}</p>
                {Object.keys(entry.properties).length > 0 && (
                  <div className="log-properties">
                    {Object.entries(entry.properties).map(([name, value]) => (
                      <button
                        key={name}
                        type="button"
                        className="log-property"
                        title={`Show every message mentioning this ${name}`}
                        onClick={() => setSearch(value)}
                      >
                        <span className="muted">{name}</span> {value}
                      </button>
                    ))}
                  </div>
                )}
                <div className="muted small log-category">{entry.category}</div>
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
        </div>
    </section>
  )
}
