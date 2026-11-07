import { useMutation, useQuery } from '@tanstack/react-query'
import { CircleCheck, ClipboardCheck, FileBarChart, History, Lightbulb, LoaderCircle, Play, SearchCheck } from 'lucide-react'
import { api, type ReviewFinding } from '../api'
import { Badge, Empty, ErrorMessage, Loading, Markdown, PageHeader, RunCard, SectionTitle, WikiLink } from '../components'
import { formatDate } from '../format'

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
      <div>
        <PageHeader
          icon={ClipboardCheck}
          title="Reviews"
          description="The Reviewer reads the wiki for gaps, contradictions and stale pages, and opens findings for the Editor."
          actions={
            <button onClick={() => requestReview.mutate()} disabled={requestReview.isPending || !!running}>
              {running ? <LoaderCircle className="spin" aria-hidden /> : <Play aria-hidden />}
              {running ? 'Review running…' : 'Request review'}
            </button>
          }
        />
        {requestReview.isError && <ErrorMessage error={requestReview.error} />}

        <SectionTitle icon={SearchCheck} count={openFindings.data?.length}>
          Open findings
        </SectionTitle>
        {openFindings.isPending && <Loading what="findings" />}
        {openFindings.isError && <ErrorMessage error={openFindings.error} />}
        {openFindings.data?.length === 0 && (
          <Empty icon={CircleCheck}>No open findings. The wiki is in good shape.</Empty>
        )}
        {openFindings.data && <FindingList findings={openFindings.data} />}

        <SectionTitle icon={History}>Review runs</SectionTitle>
        {runs.isPending && <Loading what="runs" />}
        {runs.isError && <ErrorMessage error={runs.error} />}
        {runs.data?.length === 0 && <Empty>No reviews have run yet.</Empty>}
        {runs.data?.map((run) => <RunCard key={run.id} run={run} defaultOpen={run.status === 'Running'} />)}

        <SectionTitle icon={FileBarChart}>Reports</SectionTitle>
        {reports.isPending && <Loading what="reports" />}
        {reports.data?.length === 0 && <Empty>No reports yet.</Empty>}
        {reports.data?.map((report) => (
          <details key={report.id} className="card">
            <summary>
              <strong>{formatDate(report.reviewDate)}</strong>
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
      </div>
    </section>
  )
}

export function FindingList({ findings }: { findings: ReviewFinding[] }) {
  return (
    <ul className="findings">
      {findings.map((finding) => (
        <li key={finding.id} className={`card finding priority-${finding.priority.toLowerCase()}`}>
          <div className="item-title">
            <Badge tone={finding.priority}>{finding.priority}</Badge>
            <WikiLink path={finding.pagePath} />
            {finding.resolvedAt && <Badge tone="Succeeded">Resolved {formatDate(finding.resolvedAt)}</Badge>}
          </div>
          <p>{finding.finding}</p>
          <p className="recommendation">
            <Lightbulb aria-hidden />
            <span>{finding.recommendation}</span>
          </p>
          {finding.resolution && (
            <p className="resolution">
              <CircleCheck aria-hidden />
              <span>{finding.resolution}</span>
            </p>
          )}
        </li>
      ))}
    </ul>
  )
}
