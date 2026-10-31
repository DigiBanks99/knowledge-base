import { useQuery } from '@tanstack/react-query'
import { Link, useParams, useSearchParams } from 'react-router-dom'
import { api } from '../api'
import { Empty, ErrorMessage, Loading, RunCard } from '../components'
import { TopicCard } from './TopicsPage'

/** One topic: its curation and actions, then the runs research has made for it, newest first. */
export function TopicPage() {
  const { slug = '' } = useParams()
  const topics = useQuery({ queryKey: ['topics'], queryFn: api.topics })
  const topic = topics.data?.find((candidate) => candidate.slug === slug)

  return (
    <>
      <p>
        <Link to="/research/topics">← All topics</Link>
      </p>
      {topics.isPending && <Loading what="the topic" />}
      {topics.isError && <ErrorMessage error={topics.error} />}
      {topics.data && !topic && <Empty>There is no research topic named {slug}.</Empty>}
      {topic && (
        <ul className="topics">
          <TopicCard topic={topic} />
        </ul>
      )}
      {/* Not waiting for the topic: its runs load alongside it and stay readable after the topic is pruned. */}
      <TopicRuns slug={slug} />
    </>
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
      <h2>Runs</h2>
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
        <div className="actions">
          <button className="secondary" disabled={page === 1} onClick={() => goTo(page - 1)}>
            Newer
          </button>
          <span className="muted small">Page {page}</span>
          <button className="secondary" disabled={!runs.data.hasOlder} onClick={() => goTo(page + 1)}>
            Older
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
