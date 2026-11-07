import { useQuery } from '@tanstack/react-query'
import { Activity, CircleCheck, CircleX, Gauge, ShieldCheck, TriangleAlert, type LucideIcon } from 'lucide-react'
import { api, type StatusSignalState } from '../api'
import { Badge, Empty, ErrorMessage, Loading, PageHeader, SectionTitle } from '../components'

const signalIcons: Record<StatusSignalState, LucideIcon> = {
  Healthy: CircleCheck,
  Degraded: TriangleAlert,
  Unhealthy: CircleX,
}

export function StatusPage() {
  const status = useQuery({ queryKey: ['status'], queryFn: api.status })

  return (
    <section className="page narrow">
      <div>
        <PageHeader
          icon={Activity}
          title="Status"
          description="Whether the background machinery is healthy. Signals are checked every 30 seconds; this page updates by itself when one changes."
        />

        {status.isPending && <Loading what="status" />}
        {status.isError && <ErrorMessage error={status.error} />}
        {status.data && (
          <div className={`overall${status.data.isDegraded ? ' degraded' : ''}`} role="status">
            <span className="overall-icon" aria-hidden>
              {status.data.isDegraded ? <TriangleAlert /> : <ShieldCheck />}
            </span>
            <div>
              <strong>{status.data.isDegraded ? 'Something needs attention' : 'Everything is healthy'}</strong>
              <span className="muted small">
                {status.data.signals.length} {status.data.signals.length === 1 ? 'signal' : 'signals'} checked
              </span>
            </div>
          </div>
        )}
        {status.data?.signals.length === 0 && <Empty>There are no status signals.</Empty>}
        {status.data && status.data.signals.length > 0 && (
          <ul className="signals grid">
            {status.data.signals.map((signal) => {
              const Icon = signalIcons[signal.state] ?? CircleCheck
              return (
                <li key={signal.name} className={`card signal signal-${signal.state.toLowerCase()}`}>
                  <Icon className="signal-icon" aria-hidden />
                  <div>
                    <div className="item-title">
                      <strong>{signal.name}</strong>
                      <Badge tone={signal.state}>{signal.state}</Badge>
                    </div>
                    {signal.detail && <p className="muted">{signal.detail}</p>}
                  </div>
                </li>
              )
            })}
          </ul>
        )}

        <ResearchQualitySection />
      </div>
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
      <SectionTitle icon={Gauge}>Research quality</SectionTitle>
      {quality.isPending && <Loading what="research quality" />}
      {quality.isError && <ErrorMessage error={quality.error} />}
      {figures && (
        <>
          <p className="muted small">
            Keeper <code>{figures.keeperModel}</code>, last {figures.windowDays} days. Mark results on a topic’s page to
            measure it.
          </p>
          <dl className="stats">
            <Stat
              label="Agreement with your marks"
              value={figures.marked === 0 ? '—' : share(figures.agreed, figures.marked)}
              detail={figures.marked === 0 ? 'No marks yet' : `${figures.agreed} of ${figures.marked}`}
            />
            <Stat label="Noise kept" value={String(figures.noiseKept)} detail="Kept, but marked noise" />
            <Stat label="Useful discarded" value={String(figures.usefulDiscarded)} detail="Discarded, but marked useful" />
            <Stat
              label="Research notes filed"
              value={notes === 0 ? '—' : share(figures.notesFiled, notes)}
              detail={
                notes === 0
                  ? 'No research notes resolved yet'
                  : `${figures.notesFiled} filed, ${figures.notesEmpty} empty, ${figures.notesFailed} failed`
              }
            />
          </dl>
        </>
      )}
    </section>
  )
}

function Stat({ label, value, detail }: { label: string; value: string; detail: string }) {
  return (
    <div className="card stat">
      <dt className="stat-label">{label}</dt>
      <dd className="stat-value">{value}</dd>
      <dd className="stat-detail">{detail}</dd>
    </div>
  )
}

function share(part: number, whole: number): string {
  return `${Math.round((part / whole) * 100)}%`
}
