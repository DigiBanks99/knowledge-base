import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { Empty, ErrorMessage, Loading, RunCard } from '../components'
import { FindingList } from './ReviewsPage'

export function EditsPage() {
  const runs = useQuery({ queryKey: ['runs', 'Editor'], queryFn: () => api.runs({ agent: 'Editor', limit: 30 }) })
  const closedFindings = useQuery({ queryKey: ['findings', 'Closed'], queryFn: () => api.findings('Closed') })

  return (
    <section className="page">
      <h1>Edits</h1>
      <p className="muted">
        The Editor runs whenever a review opens findings. Expand a run to see every page it read and changed.
      </p>

      <h2>Editor runs</h2>
      {runs.isPending && <Loading what="runs" />}
      {runs.isError && <ErrorMessage error={runs.error} />}
      {runs.data?.length === 0 && <Empty>The Editor has not run yet.</Empty>}
      {runs.data?.map((run) => <RunCard key={run.id} run={run} defaultOpen={run.status === 'Running'} />)}

      <h2>Resolved findings</h2>
      {closedFindings.isPending && <Loading what="findings" />}
      {closedFindings.data?.length === 0 && <Empty>No findings have been resolved yet.</Empty>}
      {closedFindings.data && (
        <FindingList
          findings={[...closedFindings.data].sort((a, b) => (b.resolvedAt ?? '').localeCompare(a.resolvedAt ?? ''))}
        />
      )}
    </section>
  )
}
