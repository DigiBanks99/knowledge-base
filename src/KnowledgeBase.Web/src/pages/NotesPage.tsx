import { useMutation, useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { CalendarClock, Clock, History, MousePointerClick, RotateCcw, StickyNote } from 'lucide-react'
import { api, type AgentRun, type NoteState } from '../api'
import { BackLink, Badge, Empty, ErrorMessage, Loading, Markdown, RunCard, SectionTitle } from '../components'
import { formatDate } from '../format'

const states: NoteState[] = ['New', 'Processing', 'Failed', 'Processed']

export function NotesPage() {
  const [params, setParams] = useSearchParams()
  const selected = params.get('note')
  const notes = useQuery({ queryKey: ['notes'], queryFn: api.notes })

  return (
    <section className={`page split${selected ? ' show-detail' : ''}`}>
      <aside className="list-pane">
        <header className="list-header">
          <span className="page-icon" aria-hidden>
            <StickyNote />
          </span>
          <h1>Notes</h1>
        </header>
        {notes.isPending && <Loading what="notes" />}
        {notes.isError && <ErrorMessage error={notes.error} />}
        {notes.data?.length === 0 && <Empty icon={StickyNote}>No notes yet. Capture one to get started.</Empty>}
        {states.map((state) => {
          const inState = notes.data?.filter((note) => note.state === state) ?? []
          if (inState.length === 0) return null
          return (
            <div key={state} className="group">
              <h2 className="group-label">
                {state} <span>{inState.length}</span>
              </h2>
              <ul className="items">
                {inState.map((note) => (
                  <li key={note.name} className={note.name === selected ? 'selected' : ''}>
                    <button type="button" className="row" onClick={() => setParams({ note: note.name })}>
                      <span className="row-meta">
                        <Badge tone={note.state}>{note.state}</Badge>
                        {formatDate(note.capturedAt)}
                      </span>
                      <p className="preview">{note.preview}</p>
                    </button>
                  </li>
                ))}
              </ul>
            </div>
          )
        })}
      </aside>
      <div className="detail-pane">
        {selected ? (
          <NoteDetail name={selected} />
        ) : (
          <Empty icon={MousePointerClick} hero>
            Select a note to see what the agents made of it.
          </Empty>
        )}
      </div>
    </section>
  )
}

function NoteDetail({ name }: { name: string }) {
  const note = useQuery({ queryKey: ['note', name], queryFn: () => api.note(name) })
  const factFinderRuns = useQuery({ queryKey: ['runs', 'note', name], queryFn: () => api.runs({ trigger: name }) })
  const weaverRuns = useQuery({
    queryKey: ['runs', 'ThreadWeaver'],
    queryFn: () => api.runs({ agent: 'ThreadWeaver', limit: 200 }),
  })
  const retry = useMutation({ mutationFn: () => api.retryNote(name) })

  const back = <BackLink to="/notes">All notes</BackLink>
  if (note.isPending)
    return (
      <>
        {back}
        <Loading what="note" />
      </>
    )
  if (note.isError)
    return (
      <>
        {back}
        <ErrorMessage error={note.error} />
      </>
    )

  // Weaver runs are triggered by fact files, which are named after what the Fact Finder wrote for this note.
  const factFiles = new Set(
    (factFinderRuns.data ?? []).flatMap((run) =>
      run.toolCalls.filter((call) => call.tool === 'FactWriterTool' && call.succeeded).map((call) => `${call.target}.json`),
    ),
  )
  const runs: AgentRun[] = [
    ...(factFinderRuns.data ?? []),
    ...(weaverRuns.data ?? []).filter((run) => factFiles.has(run.trigger.reference)),
  ].sort((a, b) => b.startedAt.localeCompare(a.startedAt))

  const { summary, content } = note.data
  return (
    <div>
      {back}
      <header className="detail-header">
        <h2>{summary.name}</h2>
        <Badge tone={summary.state}>{summary.state}</Badge>
        {summary.state === 'Failed' && (
          <button onClick={() => retry.mutate()} disabled={retry.isPending}>
            <RotateCcw aria-hidden />
            Retry
          </button>
        )}
      </header>
      <p className="meta">
        <span>
          <CalendarClock aria-hidden />
          Captured {formatDate(summary.capturedAt)}
        </span>
        <span>
          <Clock aria-hidden />
          Updated {formatDate(summary.updatedAt)}
        </span>
      </p>
      {retry.isError && <ErrorMessage error={retry.error} />}
      <div className="card">
        <Markdown content={content} />
      </div>
      <SectionTitle icon={History} count={runs.length}>
        Processing
      </SectionTitle>
      {runs.length === 0 ? (
        <Empty>{summary.state === 'New' ? 'Waiting to be processed.' : 'No runs recorded for this note.'}</Empty>
      ) : (
        runs.map((run) => <RunCard key={run.id} run={run} defaultOpen={run.status === 'Running'} />)
      )}
    </div>
  )
}
