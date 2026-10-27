import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type TopicState, type TopicSummary } from '../api'
import { Badge, Empty, ErrorMessage, Loading } from '../components'
import { formatDate } from '../format'

const stateLabels: Record<TopicState, string> = {
  Idle: 'Idle',
  AwaitingCuration: 'Awaiting curation',
  CurationFailed: 'Curation failed',
  Muted: 'Muted',
}

export function TopicsPage() {
  const queryClient = useQueryClient()
  const topics = useQuery({ queryKey: ['topics'], queryFn: api.topics })
  const setMuted = useMutation({
    mutationFn: ({ slug, muted }: { slug: string; muted: boolean }) => api.setTopicMuted(slug, muted),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['topics'] }),
  })

  return (
    <>
      <p className="muted">
        Every top-level folder or page under projects, learning and plans is a topic. Topics appear as the wiki grows
        and disappear when their page is deleted or moved. Mute one to stop researching it without moving it.
      </p>

      {setMuted.isError && <ErrorMessage error={setMuted.error} />}
      {topics.isPending && <Loading what="the topics" />}
      {topics.isError && <ErrorMessage error={topics.error} />}
      {topics.data?.length === 0 && <Empty>No topics yet.</Empty>}
      {topics.data && topics.data.length > 0 && (
        <ul className="topics">
          {topics.data.map((topic) => (
            <TopicItem
              key={topic.slug}
              topic={topic}
              busy={setMuted.isPending && setMuted.variables?.slug === topic.slug}
              onToggle={(muted) => setMuted.mutate({ slug: topic.slug, muted })}
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
}: {
  topic: TopicSummary
  busy: boolean
  onToggle: (muted: boolean) => void
}) {
  return (
    <li className="card">
      <div className="item-title">
        <Badge tone={topic.state}>{stateLabels[topic.state]}</Badge>
        <strong>{topic.path}</strong>
        <button className="secondary" disabled={busy} onClick={() => onToggle(!topic.muted)}>
          {topic.muted ? 'Unmute' : 'Mute'}
        </button>
      </div>
      {topic.brief && <p className="small">{topic.brief}</p>}
      <p className="muted small">
        {topic.lastCuratedAt ? `Last curated ${formatDate(topic.lastCuratedAt)}` : 'Not curated yet.'}
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
    </li>
  )
}
