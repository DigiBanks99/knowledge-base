import { useQuery } from '@tanstack/react-query'
import { api } from '../api'
import { Empty, ErrorMessage, Loading, RunCard } from '../components'

export function CurationPage() {
  const runs = useQuery({ queryKey: ['runs', 'Pleb'], queryFn: () => api.runs({ agent: 'Pleb', limit: 30 }) })

  return (
    <>
      <p className="muted">
        The Pleb curates a few waiting topics every research cycle, giving each a brief and its search queries or keeping
        the ones it has. Expand a run to see its tool calls, including any queries the deny-list refused.
      </p>

      {runs.isPending && <Loading what="curation runs" />}
      {runs.isError && <ErrorMessage error={runs.error} />}
      {runs.data?.length === 0 && <Empty>The Pleb has not curated a topic yet.</Empty>}
      {runs.data?.map((run) => <RunCard key={run.id} run={run} defaultOpen={run.status === 'Running'} />)}
    </>
  )
}
