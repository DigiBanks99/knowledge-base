import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type TopicState, type TopicSummary } from '../api'
import { Badge, Empty, ErrorMessage, Loading } from '../components'
import { formatDate } from '../format'

const stateLabels: Record<TopicState, string> = {
  Idle: 'Idle',
  AwaitingCuration: 'Awaiting curation',
  CurationFailed: 'Curation failed',
  Muted: 'Muted',
  Triaging: 'Results pending',
}

export function TopicsPage() {
  const queryClient = useQueryClient()
  const topics = useQuery({ queryKey: ['topics'], queryFn: api.topics })
  const setMuted = useMutation({
    mutationFn: ({ slug, muted }: { slug: string; muted: boolean }) => api.setTopicMuted(slug, muted),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['topics'] }),
  })
  const researchNow = useMutation({
    mutationFn: (slug: string) => api.researchNow(slug),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['topics'] }),
  })

  return (
    <>
      <p className="muted">
        Every top-level folder or page under projects, learning and plans is a topic. Topics appear as the wiki grows
        and disappear when their page is deleted or moved. Mute one to stop researching it without moving it.
      </p>

      {setMuted.isError && <ErrorMessage error={setMuted.error} />}
      {researchNow.isError && <ErrorMessage error={researchNow.error} />}
      {topics.isPending && <Loading what="the topics" />}
      {topics.isError && <ErrorMessage error={topics.error} />}
      {topics.data?.length === 0 && <Empty>No topics yet.</Empty>}
      {topics.data && topics.data.length > 0 && (
        <ul className="topics">
          {topics.data.map((topic) => (
            <TopicItem
              key={topic.slug}
              topic={topic}
              busy={
                (setMuted.isPending && setMuted.variables?.slug === topic.slug) ||
                (researchNow.isPending && researchNow.variables === topic.slug)
              }
              onToggle={(muted) => setMuted.mutate({ slug: topic.slug, muted })}
              onResearch={() => researchNow.mutate(topic.slug)}
            />
          ))}
        </ul>
      )}
    </>
  )
}

function TopicItem({
  topic,
  busy,
  onToggle,
  onResearch,
}: {
  topic: TopicSummary
  busy: boolean
  onToggle: (muted: boolean) => void
  onResearch: () => void
}) {
  const canResearch = !topic.muted && topic.queries.length > 0 && topic.pendingResults === 0

  return (
    <li className="card">
      <div className="item-title">
        <Badge tone={topic.state}>{stateLabels[topic.state]}</Badge>
        <strong>{topic.path}</strong>
        <span className="actions">
          <button
            className="secondary"
            disabled={busy || !canResearch}
            title={canResearch ? undefined : researchUnavailableReason(topic)}
            onClick={onResearch}
          >
            Research now
          </button>
          <button className="secondary" disabled={busy} onClick={() => onToggle(!topic.muted)}>
            {topic.muted ? 'Unmute' : 'Mute'}
          </button>
        </span>
      </div>
      {topic.brief && <p className="small">{topic.brief}</p>}
      <p className="muted small">
        {topic.lastCuratedAt ? `Last curated ${formatDate(topic.lastCuratedAt)}` : 'Not curated yet.'}
        {topic.lastSearchedAt ? ` · Last searched ${formatDate(topic.lastSearchedAt)}` : ''}
        {topic.nextSearchAt && !topic.muted && topic.pendingResults === 0
          ? ` · Due ${formatDate(topic.nextSearchAt)}`
          : ''}
      </p>
      {topic.queries.length > 0 && (
        <ul className="queries">
          {topic.queries.map((query) => (
            <li key={query}>
              <code>{query}</code>
            </li>
          ))}
        </ul>
      )}
      {topic.pendingResults > 0 && <PendingResults slug={topic.slug} count={topic.pendingResults} />}
    </li>
  )
}

function researchUnavailableReason(topic: TopicSummary): string {
  if (topic.muted) return 'Unmute the topic to research it.'
  if (topic.queries.length === 0) return 'The topic has no queries until the Pleb curates it.'
  return 'Its last results are still waiting to be triaged.'
}

/** The results waiting for triage, loaded only once the author opens them. */
function PendingResults({ slug, count }: { slug: string; count: number }) {
  const [open, setOpen] = useState(false)
  const results = useQuery({
    queryKey: ['topic-results', slug],
    queryFn: () => api.topicResults(slug),
    enabled: open,
  })

  return (
    <details className="pending-results" onToggle={(event) => setOpen(event.currentTarget.open)}>
      <summary>
        {count} {count === 1 ? 'result' : 'results'} waiting for triage
      </summary>
      {results.isPending && open && <Loading what="the results" />}
      {results.isError && <ErrorMessage error={results.error} />}
      {results.data && (
        <ul>
          {results.data.results.map(({ url, query, result }) => (
            <li key={url}>
              <a href={result.url} target="_blank" rel="noreferrer noopener">
                {result.title}
              </a>
              <div className="muted small">
                {result.source}
                {result.publishedAt ? ` · ${formatDate(result.publishedAt)}` : ''} · for <code>{query}</code>
              </div>
              {result.snippet && <p className="small">{result.snippet}</p>}
            </li>
          ))}
        </ul>
      )}
    </details>
  )
}
