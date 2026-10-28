import { useEffect, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'

// Which cached queries go stale when an area of the knowledge base changes on disk.
const queriesByArea: Record<string, string[][]> = {
  notes: [['notes'], ['note']],
  facts: [['notes']],
  runs: [['runs']],
  review: [['findings'], ['reports']],
  wiki: [['wiki-tree'], ['wiki-page']],
  conversations: [['conversations']],
  status: [['status']],
  research: [['deny-list'], ['topics'], ['topic-results']],
}

/**
 * Subscribes to the API's change stream and refreshes whatever the changed area affects. EventSource reconnects by
 * itself; everything is refreshed after a reconnect in case changes were missed while disconnected.
 */
export function useLiveUpdates(): boolean {
  const queryClient = useQueryClient()
  const [connected, setConnected] = useState(false)

  useEffect(() => {
    const events = new EventSource('/api/events')
    let hasConnected = false

    events.onopen = () => {
      if (hasConnected) void queryClient.invalidateQueries()
      hasConnected = true
      setConnected(true)
    }
    events.onerror = () => setConnected(false)
    events.addEventListener('change', (event) => {
      const { area } = JSON.parse((event as MessageEvent<string>).data) as { area: string }
      for (const queryKey of queriesByArea[area] ?? []) {
        void queryClient.invalidateQueries({ queryKey })
      }
    })

    return () => events.close()
  }, [queryClient])

  return connected
}
