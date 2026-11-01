import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api, type AuthorGuidance, type FetchedPage, type TopicState, type TopicSummary } from '../api'
import { Badge, Empty, ErrorMessage, Loading, Markdown } from '../components'
import { formatDate, formatInterval } from '../format'

const stateLabels: Record<TopicState, string> = {
  Idle: 'Idle',
  AwaitingCuration: 'Awaiting curation',
  CurationFailed: 'Curation failed',
  Muted: 'Muted',
  NeedsReview: 'Needs review',
  Triaging: 'Results pending',
  Failing: 'Failing',
}

export function TopicsPage() {
  const topics = useQuery({ queryKey: ['topics'], queryFn: api.topics })

  return (
    <>
      <p className="muted">
        Every top-level folder or page under projects, learning and plans is a topic. Topics appear as the wiki grows
        and disappear when their page is deleted or moved. Mute one to stop researching it without moving it.
      </p>
      <p className="muted">
        Review each curation: approve a good one, mark a wrong one invalid with a reason, answer the Pleb when it asks
        what a topic is about, or edit the brief and queries yourself. Your answers and reasons go into every later
        curation of the topic. An edited topic stays as you set it until you recurate it. Open a topic to see the runs
        research has made for it.
      </p>

      {topics.isPending && <Loading what="the topics" />}
      {topics.isError && <ErrorMessage error={topics.error} />}
      {topics.data?.length === 0 && <Empty>No topics yet.</Empty>}
      {topics.data && topics.data.length > 0 && (
        <ul className="topics">
          {topics.data.map((topic) => (
            <TopicCard key={topic.slug} topic={topic} linked />
          ))}
        </ul>
      )}
    </>
  )
}

/**
 * A topic with its curation and every action the author can take on it, as the Topics list and the topic's own page
 * both show it. Render it inside a `ul.topics`.
 */
export function TopicCard({ topic, linked = false }: { topic: TopicSummary; linked?: boolean }) {
  const queryClient = useQueryClient()
  const [form, setForm] = useState<'none' | 'reject' | 'edit'>('none')
  const invalidateTopics = () => queryClient.invalidateQueries({ queryKey: ['topics'] })
  const review = useMutation({
    mutationFn: (action: () => Promise<unknown>) => action(),
    onSuccess: () => {
      setForm('none')
      return invalidateTopics()
    },
  })
  const setMuted = useMutation({
    mutationFn: (muted: boolean) => api.setTopicMuted(topic.slug, muted),
    onSuccess: invalidateTopics,
  })
  const researchNow = useMutation({
    mutationFn: () => api.researchNow(topic.slug),
    onSuccess: invalidateTopics,
  })
  const canResearch =
    !topic.muted && topic.reviewQuestion === null && topic.queries.length > 0 && topic.pendingResults === 0
  const hasCuration = topic.queries.length > 0
  const busy = setMuted.isPending || researchNow.isPending
  const reviewBusy = busy || review.isPending

  return (
    <li className="card">
      <div className="item-title">
        <Badge tone={topic.state}>{stateLabels[topic.state]}</Badge>
        {topic.pinned && <Badge tone="pinned">Edited by you</Badge>}
        {topic.approvedAt && <Badge tone="approved">Approved {formatDate(topic.approvedAt)}</Badge>}
        <strong>
          {linked ? <Link to={`/research/topics/${encodeURIComponent(topic.slug)}`}>{topic.path}</Link> : topic.path}
        </strong>
        <span className="actions">
          <button
            className="secondary"
            disabled={busy || !canResearch}
            title={canResearch ? undefined : researchUnavailableReason(topic)}
            onClick={() => researchNow.mutate()}
          >
            Research now
          </button>
          <button className="secondary" disabled={busy} onClick={() => setMuted.mutate(!topic.muted)}>
            {topic.muted ? 'Unmute' : 'Mute'}
          </button>
        </span>
      </div>
      {setMuted.isError && <ErrorMessage error={setMuted.error} />}
      {researchNow.isError && <ErrorMessage error={researchNow.error} />}
      {topic.brief && <p className="small">{topic.brief}</p>}
      <p className="muted small">
        {topic.lastCuratedAt ? `Last curated ${formatDate(topic.lastCuratedAt)}` : 'Not curated yet.'}
        {topic.lastSearchedAt ? ` · Last searched ${formatDate(topic.lastSearchedAt)}` : ''}
        {topic.nextSearchAt && !topic.muted && topic.pendingResults === 0
          ? ` · Due ${formatDate(topic.nextSearchAt)}`
          : ''}
        {` · Checked every ${formatInterval(topic.interval)}`}
        {topic.lastPositiveAt ? ` · Last positive ${formatDate(topic.lastPositiveAt)}` : ' · Nothing filed yet'}
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
      {topic.reviewQuestion && (
        <p className="question small">
          <strong>The Pleb asks:</strong> {topic.reviewQuestion}
        </p>
      )}
      {topic.state === 'NeedsReview' && (
        <TextForm
          label="Your answer"
          placeholder="Say what the topic is about: expand the acronym, name the product and who makes it."
          submit="Clarify"
          required
          busy={reviewBusy}
          onSubmit={(answer) => review.mutate(() => api.clarifyTopic(topic.slug, answer))}
        />
      )}
      <div className="actions">
        <button
          className="secondary"
          disabled={reviewBusy || !hasCuration || topic.reviewQuestion !== null || topic.approvedAt !== null}
          title={hasCuration ? undefined : 'There is no curation to approve yet.'}
          onClick={() => review.mutate(() => api.approveTopic(topic.slug))}
        >
          Good
        </button>
        <button
          className="secondary"
          disabled={reviewBusy || !hasCuration}
          onClick={() => setForm(form === 'reject' ? 'none' : 'reject')}
        >
          Invalid
        </button>
        <button
          className="secondary"
          disabled={reviewBusy}
          title={topic.pinned ? 'Lets the Pleb replace your edit.' : undefined}
          onClick={() => review.mutate(() => api.recurateTopic(topic.slug))}
        >
          Recurate
        </button>
        <button className="secondary" disabled={reviewBusy} onClick={() => setForm(form === 'edit' ? 'none' : 'edit')}>
          Edit
        </button>
      </div>
      {form === 'reject' && (
        <TextForm
          label="Why is it wrong? (optional)"
          placeholder="For example: OKF here means the Open Knowledge Foundation's format, not the Google project."
          submit="Mark invalid"
          busy={reviewBusy}
          onSubmit={(reason) => review.mutate(() => api.rejectTopic(topic.slug, reason || null))}
        />
      )}
      {form === 'edit' && (
        <EditForm
          topic={topic}
          busy={reviewBusy}
          onSubmit={(brief, queries) => review.mutate(() => api.editTopicCuration(topic.slug, brief, queries))}
        />
      )}
      {review.isError && <ErrorMessage error={review.error} />}
      {topic.guidance.length > 0 && <Guidance guidance={topic.guidance} />}
      {topic.pendingResults > 0 && <PendingResults slug={topic.slug} count={topic.pendingResults} />}
    </li>
  )
}

/** A single text box with a submit button, for an answer or a reason. */
function TextForm({
  label,
  placeholder,
  submit,
  required = false,
  busy,
  onSubmit,
}: {
  label: string
  placeholder: string
  submit: string
  required?: boolean
  busy: boolean
  onSubmit: (text: string) => void
}) {
  const [text, setText] = useState('')

  return (
    <form
      className="review-form"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit(text.trim())
      }}
    >
      <label className="small">
        {label}
        <textarea rows={2} value={text} placeholder={placeholder} onChange={(event) => setText(event.target.value)} />
      </label>
      <div className="actions">
        <button type="submit" disabled={busy || (required && text.trim() === '')}>
          {submit}
        </button>
      </div>
    </form>
  )
}

/** The brief and up to three queries, set by hand; the API checks them against the deny-list. */
function EditForm({
  topic,
  busy,
  onSubmit,
}: {
  topic: TopicSummary
  busy: boolean
  onSubmit: (brief: string, queries: string[]) => void
}) {
  const [brief, setBrief] = useState(topic.brief ?? '')
  const [queries, setQueries] = useState<string[]>(() => [0, 1, 2].map((index) => topic.queries[index] ?? ''))

  return (
    <form
      className="review-form"
      onSubmit={(event) => {
        event.preventDefault()
        onSubmit(
          brief.trim(),
          queries.map((query) => query.trim()).filter((query) => query !== ''),
        )
      }}
    >
      <label className="small">
        Brief
        <textarea rows={3} value={brief} onChange={(event) => setBrief(event.target.value)} />
      </label>
      {queries.map((query, index) => (
        <input
          key={index}
          aria-label={`Query ${index + 1}`}
          placeholder={`Query ${index + 1}${index === 0 ? '' : ' (optional)'}`}
          value={query}
          onChange={(event) => setQueries(queries.map((old, at) => (at === index ? event.target.value : old)))}
        />
      ))}
      <div className="actions">
        <button type="submit" disabled={busy || brief.trim() === '' || queries.every((query) => query.trim() === '')}>
          Save and pin
        </button>
      </div>
    </form>
  )
}

/** What the author has told the Pleb about the topic, oldest first, as every curation of it sees it. */
function Guidance({ guidance }: { guidance: AuthorGuidance[] }) {
  return (
    <details className="guidance">
      <summary>Your guidance to the Pleb ({guidance.length})</summary>
      <ul>
        {guidance.map((entry) => (
          <li key={entry.at}>
            <span className="muted">{formatDate(entry.at)} · </span>
            {entry.kind === 'Clarification' ? (
              <>
                {entry.question ? `Answered “${entry.question}”: ` : 'Answered: '}
                {entry.text}
              </>
            ) : (
              <>
                Rejected{' '}
                {entry.rejectedQueries && entry.rejectedQueries.length > 0 ? (
                  <code>{entry.rejectedQueries.join('; ')}</code>
                ) : (
                  'the curation'
                )}
                {entry.text ? `: ${entry.text}` : ' (no reason given)'}
              </>
            )}
          </li>
        ))}
      </ul>
    </details>
  )
}

function researchUnavailableReason(topic: TopicSummary): string {
  if (topic.muted) return 'Unmute the topic to research it.'
  if (topic.reviewQuestion !== null) return 'Answer the Pleb’s question first.'
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
          {results.data.results.map(({ url, query, result, page }) => (
            <li key={url}>
              <a href={result.url} target="_blank" rel="noreferrer noopener">
                {result.title}
              </a>
              <div className="muted small">
                {result.source}
                {result.publishedAt ? ` · ${formatDate(result.publishedAt)}` : ''} · for <code>{query}</code>
              </div>
              {result.snippet && <p className="small">{result.snippet}</p>}
              <FetchedContent page={page} />
            </li>
          ))}
        </ul>
      )}
    </details>
  )
}

/** The page the fetcher reduced a result to, as the Keeper and the Clerk see it, or why there is none. */
function FetchedContent({ page }: { page: FetchedPage | null }) {
  if (!page) return <p className="muted small">Page not fetched yet.</p>
  if (page.error !== null || page.markdown === null)
    return <p className="muted small fetch-failed">Not fetched: {page.error}</p>

  return (
    <details className="fetched-content">
      <summary>
        Fetched content{page.truncated ? ' (cut to the triage budget)' : ''} · {formatDate(page.fetchedAt)}
      </summary>
      <Markdown content={page.markdown} withoutImages />
    </details>
  )
}
