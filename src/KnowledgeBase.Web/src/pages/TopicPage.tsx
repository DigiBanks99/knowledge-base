import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { ArrowLeft, ChevronLeft, ChevronRight, Gavel, History, ThumbsDown, ThumbsUp } from 'lucide-react'
import { api, type JudgedResult, type ResultMark } from '../api'
import { Badge, Empty, ErrorMessage, Loading, RunCard, SectionTitle } from '../components'
import { formatDate } from '../format'
import { TopicCard } from './TopicsPage'

/** One topic: its curation and actions, then the runs research has made for it, newest first. */
export function TopicPage() {
  const { slug = '' } = useParams()
  const topics = useQuery({ queryKey: ['topics'], queryFn: api.topics })
  const topic = topics.data?.find((candidate) => candidate.slug === slug)

  return (
    <>
      <Link to="/research/topics" className="back">
        <ArrowLeft aria-hidden />
        All topics
      </Link>
      {topics.isPending && <Loading what="the topic" />}
      {topics.isError && <ErrorMessage error={topics.error} />}
      {topics.data && !topic && <Empty>There is no research topic named {slug}.</Empty>}
      {topic && (
        <ul className="topics">
          <TopicCard topic={topic} />
        </ul>
      )}
      {/* Not waiting for the topic: these load alongside it and stay readable after the topic is pruned. */}
      <JudgedResults slug={slug} />
      <TopicRuns slug={slug} />
    </>
  )
}

/**
 * The results the Keeper judged, for the author to mark as useful or noise. Marks only feed the agreement figures on
 * the Status page: they never change a verdict or bring a discarded result back.
 */
function JudgedResults({ slug }: { slug: string }) {
  const queryClient = useQueryClient()
  const judged = useQuery({ queryKey: ['topic-judged', slug], queryFn: () => api.judgedResults(slug) })
  const mark = useMutation({
    mutationFn: ({ result, value }: { result: JudgedResult; value: ResultMark | null }) =>
      api.markResult(slug, result.searchedAt, result.url, value),
    onSuccess: () =>
      Promise.all([
        queryClient.invalidateQueries({ queryKey: ['topic-judged', slug] }),
        queryClient.invalidateQueries({ queryKey: ['research-quality'] }),
      ]),
  })

  return (
    <section className="judged-results">
      <SectionTitle icon={Gavel} count={judged.data?.results.length}>
        Judged results
      </SectionTitle>
      <p className="muted small">
        Mark what the Keeper judged as useful or noise; the Status page shows how often it agrees. Marks never change a
        verdict.
        {judged.data &&
          ` Marks count for results judged in the last ${judged.data.agreementWindowDays} days; results stay listed for ${judged.data.retentionDays} days after their note is filed. A result is kept at useful ≥ ${percent(judged.data.keepThreshold)} and discarded as an injection at ≥ ${percent(judged.data.injectionThreshold)}.`}
      </p>
      {judged.isPending && <Loading what="the judged results" />}
      {judged.isError && <ErrorMessage error={judged.error} />}
      {mark.isError && <ErrorMessage error={mark.error} />}
      {judged.data?.results.length === 0 && <Empty>The Keeper has not judged any results for this topic yet.</Empty>}
      {judged.data && judged.data.results.length > 0 && (
        <ul>
          {judged.data.results.map((result) => (
            <li key={`${result.searchedAt} ${result.url}`} className="card">
              <div className="item-title">
                <Badge tone={result.kept ? 'Kept' : 'Discarded'}>{result.kept ? 'Kept' : 'Discarded'}</Badge>
                <a href={result.url} target="_blank" rel="noreferrer noopener">
                  {result.title}
                </a>
                {result.markable && (
                  <div className="actions">
                    {(['Useful', 'Noise'] as const).map((value) => (
                      <button
                        key={value}
                        className="secondary"
                        aria-pressed={result.mark === value}
                        disabled={mark.isPending}
                        onClick={() => mark.mutate({ result, value: result.mark === value ? null : value })}
                      >
                        {value === 'Useful' ? <ThumbsUp aria-hidden /> : <ThumbsDown aria-hidden />}
                        {value}
                      </button>
                    ))}
                  </div>
                )}
              </div>
              <div className="muted small">
                {result.source} · for <code>{result.query}</code> · judged {formatDate(result.judgedAt)}
              </div>
              {result.markable ? (
                <div className="scores">
                  <Score label="Useful" value={result.useful} tone="var(--success)" />
                  <Score label="Injection" value={result.injection} tone="var(--danger)" />
                  {result.kind && <span>{result.kind}</span>}
                  {result.reason && <span>{result.reason}</span>}
                </div>
              ) : (
                <div className="small">
                  {`${result.failure ?? result.reason ?? 'No verdict'} The Keeper gave no verdict here, so there is nothing to mark.`}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
    </section>
  )
}

function percent(value: number): string {
  return `${Math.round(value * 100)}%`
}

function probability(value: number | null): string {
  return value === null ? 'n/a' : percent(value)
}

/** One of the Keeper's probabilities as a small bar with its percentage. */
function Score({ label, value, tone }: { label: string; value: number | null; tone: string }) {
  return (
    <span className="score">
      {label}
      {value !== null && (
        <span className="meter" style={{ ['--tone' as string]: tone }} aria-hidden>
          <span style={{ width: percent(value) }} />
        </span>
      )}
      <strong>{probability(value)}</strong>
    </span>
  )
}

/** The topic's runs a page at a time; the page is kept in the URL so the browser's back button returns to it. */
function TopicRuns({ slug }: { slug: string }) {
  const [searchParams, setSearchParams] = useSearchParams()
  const page = pageFrom(searchParams.get('page'))
  const runs = useQuery({
    queryKey: ['runs', 'topic', slug, page],
    queryFn: () => api.topicRuns(slug, page),
  })
  const goTo = (next: number) => setSearchParams(next === 1 ? {} : { page: String(next) })

  return (
    <section className="topic-runs">
      <SectionTitle icon={History}>Runs</SectionTitle>
      <p className="muted small">
        The Pleb’s curations, the triage of each result and the ThreadWeaver filing the research notes, newest first.
      </p>
      {runs.isPending && <Loading what="the runs" />}
      {runs.isError && <ErrorMessage error={runs.error} />}
      {runs.data?.runs.length === 0 && (
        <Empty>{page === 1 ? 'Research has not run anything for this topic yet.' : 'No runs on this page.'}</Empty>
      )}
      {runs.data?.runs.map((run) => <RunCard key={run.id} run={run} defaultOpen={run.status === 'Running'} />)}
      {runs.data && (page > 1 || runs.data.hasOlder) && (
        <div className="actions pager">
          <button className="secondary" disabled={page === 1} onClick={() => goTo(page - 1)}>
            <ChevronLeft aria-hidden />
            Newer
          </button>
          <span className="muted small">Page {page}</span>
          <button className="secondary" disabled={!runs.data.hasOlder} onClick={() => goTo(page + 1)}>
            Older
            <ChevronRight aria-hidden />
          </button>
        </div>
      )}
    </section>
  )
}

function pageFrom(value: string | null): number {
  const page = Number(value)
  return Number.isInteger(page) && page >= 1 ? page : 1
}
