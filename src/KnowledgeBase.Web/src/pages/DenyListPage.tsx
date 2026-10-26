import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, type DenyListEntry, type DenyListState } from '../api'
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
        allow one you do want researched. A choice you make here is kept: automatic classification never changes it.
      </p>

      {setState.isError && <ErrorMessage error={setState.error} />}
      {denyList.isPending && <Loading what="the deny-list" />}
      {denyList.isError && <ErrorMessage error={denyList.error} />}
      {denyList.data?.length === 0 && <Empty>The deny-list is empty.</Empty>}
      {denyList.data && denyList.data.length > 0 && (
        <ul className="deny-list">
          {denyList.data.map((entry) => (
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

function DenyListItem({
  entry,
  busy,
  onToggle,
}: {
  entry: DenyListEntry
  busy: boolean
  onToggle: (state: DenyListState) => void
}) {
  const denied = entry.state === 'Denied'
  return (
    <li className="card">
      <div className="item-title">
        <Badge tone={entry.state}>{entry.state}</Badge>
        <strong>{entry.name}</strong>
        <span className="muted small">
          {entry.kind} · {entry.source === 'Manual' ? 'set by you' : 'automatic'}
        </span>
        <button className="secondary" disabled={busy} onClick={() => onToggle(denied ? 'Allowed' : 'Denied')}>
          {denied ? 'Allow' : 'Deny'}
        </button>
      </div>
    </li>
  )
}
