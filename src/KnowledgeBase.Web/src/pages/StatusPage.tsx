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
    </section>
  )
}
