import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { Badge, Empty, ErrorMessage, Loading } from '../components'

export function StatusPage() {
  const status = useQuery({ queryKey: ['status'], queryFn: api.status })

  return (
    <section className="page narrow">
      <h1>Status</h1>
      <p className="muted">
        Whether the background machinery is healthy. Signals are checked every 30 seconds; this page updates by itself
        when one changes.
      </p>

      {status.isPending && <Loading what="status" />}
      {status.isError && <ErrorMessage error={status.error} />}
      {status.data?.signals.length === 0 && <Empty>There are no status signals.</Empty>}
      {status.data && status.data.signals.length > 0 && (
        <ul className="signals">
          {status.data.signals.map((signal) => (
            <li key={signal.name} className="card">
              <div className="item-title">
                <Badge tone={signal.state}>{signal.state}</Badge>
                <strong>{signal.name}</strong>
              </div>
              {signal.detail && <p className="muted">{signal.detail}</p>}
            </li>
          ))}
        </ul>
      )}

      <ResearchQualitySection />
    </section>
  )
}

/** A measure of the research loop on production traffic, not a health signal: it never makes the page degraded. */
function ResearchQualitySection() {
  const quality = useQuery({ queryKey: ['research-quality'], queryFn: api.researchQuality })
  const figures = quality.data
  const notes = figures ? figures.notesFiled + figures.notesEmpty + figures.notesFailed : 0

  return (
    <section className="research-quality">
      <h2>Research quality</h2>
      {quality.isPending && <Loading what="research quality" />}
      {quality.isError && <ErrorMessage error={quality.error} />}
      {figures && (
        <div className="card">
          <p className="muted small">
            Keeper <code>{figures.keeperModel}</code>, last {figures.windowDays} days. Mark results on a topic’s page to
            measure it.
          </p>
          <dl>
            <dt>Agreement with your marks</dt>
            <dd>
              {figures.marked === 0
                ? 'No marks yet'
                : `${share(figures.agreed, figures.marked)} (${figures.agreed} of ${figures.marked})`}
            </dd>
            <dt>Noise kept</dt>
            <dd>{figures.noiseKept}</dd>
            <dt>Useful discarded</dt>
            <dd>{figures.usefulDiscarded}</dd>
            <dt>Research notes filed from</dt>
            <dd>
              {notes === 0
                ? 'No research notes resolved yet'
                : `${share(figures.notesFiled, notes)} (${figures.notesFiled} filed, ${figures.notesEmpty} empty, ${figures.notesFailed} failed)`}
            </dd>
          </dl>
        </div>
      )}
    </section>
  )
}

function share(part: number, whole: number): string {
  return `${Math.round((part / whole) * 100)}%`
}
