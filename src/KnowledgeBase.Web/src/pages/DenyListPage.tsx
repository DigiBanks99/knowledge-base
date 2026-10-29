import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { api, type DenyListEntry, type DenyListState, type SubjectKind } from '../api'
import { Badge, Empty, ErrorMessage, Loading } from '../components'

export function DenyListPage() {
  const queryClient = useQueryClient()
  const denyList = useQuery({ queryKey: ['deny-list'], queryFn: api.denyList })
  const setState = useMutation({
    mutationFn: ({ name, state }: { name: string; state: DenyListState }) => api.setDenyListState(name, state),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['deny-list'] }),
  })

  return (
    <>
      <p className="muted">
        Names that never go into a search query. People and organisations from your own notes are added automatically;
        allow one you do want researched. Public figures, the authors and experts behind what you read, are allowed
        automatically; someone who might be one is pending, and kept out of queries, until you decide. A choice you make
        here is kept: automatic classification never changes it. Add a name to decide about it before any note
        mentions it.
      </p>

      <AddName />

      {setState.isError && <ErrorMessage error={setState.error} />}
      {denyList.isPending && <Loading what="the deny-list" />}
      {denyList.isError && <ErrorMessage error={denyList.error} />}
      {denyList.data?.length === 0 && <Empty>The deny-list is empty.</Empty>}
      {denyList.data && denyList.data.length > 0 && (
        <ul className="deny-list">
          {pendingFirst(denyList.data).map((entry) => (
            <DenyListItem
              key={entry.name}
              entry={entry}
              busy={setState.isPending && setState.variables?.name === entry.name}
              onToggle={(state) => setState.mutate({ name: entry.name, state })}
            />
          ))}
        </ul>
      )}
    </>
  )
}

/** Decides about a name before any note names it, such as allowing a public figure up front. */
function AddName() {
  const queryClient = useQueryClient()
  const [name, setName] = useState('')
  const [kind, setKind] = useState<SubjectKind>('PublicFigure')
  const add = useMutation({
    mutationFn: (state: DenyListState) => api.addDenyListEntry(name.trim(), kind, state),
    onSuccess: () => {
      setName('')
      return queryClient.invalidateQueries({ queryKey: ['deny-list'] })
    },
  })
  const disabled = !name.trim() || add.isPending

  return (
    <div className="card add-name">
      <input
        aria-label="Name"
        placeholder="Name"
        value={name}
        onChange={(event) => setName(event.target.value)}
      />
      <select aria-label="Kind" value={kind} onChange={(event) => setKind(event.target.value as SubjectKind)}>
        <option value="PublicFigure">Public figure</option>
        <option value="Person">Person</option>
        <option value="Organisation">Organisation</option>
      </select>
      <span className="actions">
        <button className="secondary" disabled={disabled} onClick={() => add.mutate('Allowed')}>
          Allow
        </button>
        <button className="secondary" disabled={disabled} onClick={() => add.mutate('Denied')}>
          Deny
        </button>
      </span>
      {add.isError && <ErrorMessage error={add.error} />}
    </div>
  )
}

function DenyListItem({
  entry,
  busy,
  onToggle,
}: {
  entry: DenyListEntry
  busy: boolean
  onToggle: (state: DenyListState) => void
}) {
  return (
    <li className="card">
      <div className="item-title">
        <Badge tone={entry.state}>{entry.state}</Badge>
        <strong>{entry.name}</strong>
        <span className="muted small">
          {kindLabel(entry.kind)} · {entry.source === 'Manual' ? 'set by you' : 'automatic'}
        </span>
        <span className="actions">
          {entry.state === 'Pending' ? (
            <>
              <button className="secondary" disabled={busy} onClick={() => onToggle('Allowed')}>
                Public figure
              </button>
              <button className="secondary" disabled={busy} onClick={() => onToggle('Denied')}>
                Private person
              </button>
            </>
          ) : (
            <button
              className="secondary"
              disabled={busy}
              onClick={() => onToggle(entry.state === 'Denied' ? 'Allowed' : 'Denied')}
            >
              {entry.state === 'Denied' ? 'Allow' : 'Deny'}
            </button>
          )}
        </span>
      </div>
    </li>
  )
}

/** Names waiting on the author's decision come first, keeping the rest in their stored order. */
function pendingFirst(entries: DenyListEntry[]) {
  return [...entries.filter((entry) => entry.state === 'Pending'), ...entries.filter((entry) => entry.state !== 'Pending')]
}

function kindLabel(kind: SubjectKind) {
  return kind === 'PublicFigure' ? 'Public figure' : kind
}
