import { useMutation, useQuery } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api, type ReviewFinding } from '../api'
import { Badge, Empty, ErrorMessage, Loading, Markdown, RunCard, formatDate } from '../components'

export function ReviewsPage() {
  const runs = useQuery({ queryKey: ['runs', 'Reviewer'], queryFn: () => api.runs({ agent: 'Reviewer', limit: 20 }) })
  const reports = useQuery({ queryKey: ['reports'], queryFn: api.reports })
  const openFindings = useQuery({ queryKey: ['findings', 'Open'], queryFn: () => api.findings('Open') })
  const closedFindings = useQuery({ queryKey: ['findings', 'Closed'], queryFn: () => api.findings('Closed') })
  const requestReview = useMutation({ mutationFn: api.requestReview })

  const findingsById = new Map(
    [...(openFindings.data ?? []), ...(closedFindings.data ?? [])].map((finding) => [finding.id, finding]),
  )
  const running = runs.data?.find((run) => run.status === 'Running')

  return (
    <section className="page">
      <header className="page-header">
        <h1>Reviews</h1>
        <button onClick={() => requestReview.mutate()} disabled={requestReview.isPending || !!running}>
          {running ? 'Review running…' : 'Request review'}
        </button>
      </header>
      {requestReview.isError && <ErrorMessage error={requestReview.error} />}

      <h2>Open findings</h2>
      {openFindings.isPending && <Loading what="findings" />}
      {openFindings.isError && <ErrorMessage error={openFindings.error} />}
      {openFindings.data?.length === 0 && <Empty>No open findings. The wiki is in good shape.</Empty>}
      {openFindings.data && <FindingList findings={openFindings.data} />}

      <h2>Review runs</h2>
      {runs.isPending && <Loading what="runs" />}
      {runs.isError && <ErrorMessage error={runs.error} />}
      {runs.data?.length === 0 && <Empty>No reviews have run yet.</Empty>}
      {runs.data?.map((run) => <RunCard key={run.id} run={run} defaultOpen={run.status === 'Running'} />)}

      <h2>Reports</h2>
      {reports.isPending && <Loading what="reports" />}
      {reports.data?.length === 0 && <Empty>No reports yet.</Empty>}
      {reports.data?.map((report) => (
        <details key={report.id} className="card">
          <summary>
            <strong>{formatDate(report.reviewDate)}</strong>{' '}
            <span className="muted">
              · {report.findingIds.length} {report.findingIds.length === 1 ? 'finding' : 'findings'}
            </span>
          </summary>
          <Markdown content={report.summary} />
          <FindingList
            findings={report.findingIds.map((id) => findingsById.get(id)).filter((f): f is ReviewFinding => !!f)}
          />
        </details>
      ))}
    </section>
  )
}

export function FindingList({ findings }: { findings: ReviewFinding[] }) {
  return (
    <ul className="findings">
      {findings.map((finding) => (
        <li key={finding.id} className="card finding">
          <div className="item-title">
            <Badge tone={finding.priority}>{finding.priority}</Badge>
            <Link to={`/wiki?page=${encodeURIComponent(finding.pagePath)}`}>{finding.pagePath}</Link>
            {finding.resolvedAt && <Badge tone="Succeeded">Resolved {formatDate(finding.resolvedAt)}</Badge>}
          </div>
          <p>{finding.finding}</p>
          <p className="muted">
            <strong>Recommendation:</strong> {finding.recommendation}
          </p>
          {finding.resolution && (
            <p>
              <strong>Resolution:</strong> {finding.resolution}
            </p>
          )}
        </li>
      ))}
    </ul>
  )
}
