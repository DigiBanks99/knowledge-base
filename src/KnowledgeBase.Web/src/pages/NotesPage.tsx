import { useMutation, useQuery } from '@tanstack/react-query'
import { useSearchParams } from 'react-router-dom'
import { api, type AgentRun, type NoteState } from '../api'
import { Badge, Empty, ErrorMessage, Loading, Markdown, RunCard, formatDate } from '../components'

const states: NoteState[] = ['New', 'Processing', 'Failed', 'Processed']

export function NotesPage() {
  const [params, setParams] = useSearchParams()
  const selected = params.get('note')
  const notes = useQuery({ queryKey: ['notes'], queryFn: api.notes })

  return (
    <section className="page split">
      <aside className="list-pane">
        <h1>Notes</h1>
        {notes.isPending && <Loading what="notes" />}
        {notes.isError && <ErrorMessage error={notes.error} />}
        {notes.data?.length === 0 && <Empty>No notes yet. Capture one to get started.</Empty>}
        {states.map((state) => {
          const inState = notes.data?.filter((note) => note.state === state) ?? []
          if (inState.length === 0) return null
          return (
            <div key={state} className="group">
              <h2>
                {state} <span className="muted">({inState.length})</span>
              </h2>
              <ul className="items">
                {inState.map((note) => (
                  <li
                    key={note.name}
                    className={note.name === selected ? 'selected' : ''}
                    onClick={() => setParams({ note: note.name })}
                  >
                    <div className="item-title">
                      <Badge tone={note.state}>{note.state}</Badge>
                      <span className="muted small">{formatDate(note.capturedAt)}</span>
                    </div>
                    <p className="preview">{note.preview}</p>
                  </li>
                ))}
              </ul>
            </div>
          )
        })}
      </aside>
      <div className="detail-pane">{selected ? <NoteDetail name={selected} /> : <Empty>Select a note.</Empty>}</div>
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

  if (note.isPending) return <Loading what="note" />
  if (note.isError) return <ErrorMessage error={note.error} />

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
    <>
      <header className="detail-header">
        <h2>{summary.name}</h2>
        <Badge tone={summary.state}>{summary.state}</Badge>
        {summary.state === 'Failed' && (
          <button onClick={() => retry.mutate()} disabled={retry.isPending}>
            Retry
          </button>
        )}
      </header>
      <p className="muted small">
        Captured {formatDate(summary.capturedAt)} · updated {formatDate(summary.updatedAt)}
      </p>
      {retry.isError && <ErrorMessage error={retry.error} />}
      <div className="card">
        <Markdown content={content} />
      </div>
      <h3>Processing</h3>
      {runs.length === 0 ? (
        <Empty>{summary.state === 'New' ? 'Waiting to be processed.' : 'No runs recorded for this note.'}</Empty>
      ) : (
        runs.map((run) => <RunCard key={run.id} run={run} defaultOpen={run.status === 'Running'} />)
      )}
    </>
  )
}
