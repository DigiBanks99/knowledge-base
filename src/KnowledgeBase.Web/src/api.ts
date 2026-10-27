// Types and calls for the knowledge base API. All calls are same-origin; the sign-in cookie travels with them.

export type NoteState = 'New' | 'Processing' | 'Processed' | 'Failed'
export interface NoteSummary {
  name: string
  state: NoteState
  capturedAt: string
  updatedAt: string
  preview: string
}
export interface NoteDetail {
  summary: NoteSummary
  content: string
}

export type AgentName = 'FactFinder' | 'ThreadWeaver' | 'Reviewer' | 'Editor' | 'Librarian' | 'Pleb'
export type AgentRunStatus = 'Running' | 'Succeeded' | 'Failed'
export type AgentRunTriggerKind = 'Note' | 'Facts' | 'Curation' | 'ReviewRequest' | 'Findings'
export interface AgentToolCall {
  tool: string
  target: string | null
  at: string
  succeeded: boolean
  error: string | null
}
export interface AgentRun {
  id: string
  agent: AgentName
  trigger: { kind: AgentRunTriggerKind; reference: string }
  status: AgentRunStatus
  startedAt: string
  completedAt: string | null
  summary: string | null
  error: string | null
  toolCalls: AgentToolCall[]
}

export type PriorityLevel = 'Low' | 'Medium' | 'High'
export type FindingState = 'Open' | 'Closed'
export interface ReviewFinding {
  id: string
  reviewDate: string
  pagePath: string
  finding: string
  recommendation: string
  priority: PriorityLevel
  resolution: string | null
  resolvedAt: string | null
}
export interface ReviewReport {
  id: string
  reviewDate: string
  summary: string
  findingIds: string[]
}

export interface WikiNode {
  name: string
  path: string
  type: 'Directory' | 'Page'
  updatedAt: string
  children: WikiNode[]
}
export interface WikiPage {
  path: string
  content: string
  updatedAt: string
}

export interface ConversationMessage {
  role: 'User' | 'Assistant'
  text: string
  at: string
}
export interface ConversationSummary {
  id: string
  title: string
  createdAt: string
  updatedAt: string
}
export interface Conversation extends ConversationSummary {
  messages: ConversationMessage[]
}

export type StatusSignalState = 'Healthy' | 'Degraded' | 'Unhealthy'
export interface StatusSignal {
  name: string
  state: StatusSignalState
  detail: string | null
}
export interface StatusReport {
  signals: StatusSignal[]
  isDegraded: boolean
  checkedAt: string
}

export type SubjectKind = 'Person' | 'Organisation' | 'Other'
export type DenyListSource = 'Auto' | 'Manual'
export type DenyListState = 'Denied' | 'Allowed'
export interface DenyListEntry {
  name: string
  kind: SubjectKind
  source: DenyListSource
  state: DenyListState
}

export type TopicState = 'Idle' | 'AwaitingCuration' | 'CurationFailed' | 'Muted'
export interface TopicSummary {
  path: string
  slug: string
  state: TopicState
  brief: string | null
  queries: string[]
  muted: boolean
  lastCuratedAt: string | null
}

export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/** Sends the user to sign in, returning them to the current page afterwards. */
export function signIn(): void {
  const returnUrl = encodeURIComponent(window.location.pathname + window.location.search)
  window.location.assign(`/auth/login?returnUrl=${returnUrl}`)
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: {
      // Marks the request as coming from this app; the API rejects state-changing requests without it (CSRF).
      'X-Requested-With': 'knowledgebase',
      ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
      ...init?.headers,
    },
  })

  if (response.status === 401) {
    signIn()
    throw new ApiError(401, 'Signing in…')
  }

  if (!response.ok) {
    let message = response.statusText
    try {
      const problem = await response.json()
      message = problem.detail ?? problem.title ?? message
    } catch {
      // Not a problem-details body.
    }
    throw new ApiError(response.status, message)
  }

  return response.status === 204 || response.status === 202
    ? (undefined as T)
    : ((await response.json()) as T)
}

const post = <T>(path: string, body?: unknown) =>
  request<T>(path, { method: 'POST', body: body === undefined ? undefined : JSON.stringify(body) })
const put = <T>(path: string, body: unknown) => request<T>(path, { method: 'PUT', body: JSON.stringify(body) })

export const api = {
  me: () => request<{ name: string }>('/api/me'),

  notes: () => request<NoteSummary[]>('/api/notes'),
  note: (name: string) => request<NoteDetail>(`/api/notes/${encodeURIComponent(name)}`),
  captureNote: (content: string) => post<{ name: string }>('/api/notes', { content }),
  retryNote: (name: string) => post<void>(`/api/notes/${encodeURIComponent(name)}/retry`),

  runs: (filter: { agent?: AgentName; trigger?: string; limit?: number } = {}) => {
    const query = new URLSearchParams()
    if (filter.agent) query.set('agent', filter.agent)
    if (filter.trigger) query.set('trigger', filter.trigger)
    if (filter.limit) query.set('limit', String(filter.limit))
    return request<AgentRun[]>(`/api/runs?${query}`)
  },

  findings: (state: FindingState) => request<ReviewFinding[]>(`/api/review?state=${state}`),
  reports: () => request<ReviewReport[]>('/api/review/reports'),
  requestReview: () => post<{ filePath: string }>('/api/review', { reviewDate: new Date().toISOString() }),

  wikiTree: () => request<WikiNode>('/api/wiki/tree'),
  wikiPage: (path: string) => request<WikiPage>(`/api/wiki/page?path=${encodeURIComponent(path)}`),

  status: () => request<StatusReport>('/api/status'),

  denyList: () => request<DenyListEntry[]>('/api/research/deny-list'),
  setDenyListState: (name: string, state: DenyListState) =>
    put<DenyListEntry>('/api/research/deny-list/state', { name, state }),
  topics: () => request<TopicSummary[]>('/api/research/topics'),
  setTopicMuted: (slug: string, muted: boolean) =>
    put<unknown>(`/api/research/topics/${encodeURIComponent(slug)}/muted`, { muted }),

  conversations: () => request<ConversationSummary[]>('/api/conversations'),
  conversation: (id: string) => request<Conversation>(`/api/conversations/${id}`),
  createConversation: () => post<Conversation>('/api/conversations'),
  deleteConversation: (id: string) =>
    request<void>(`/api/conversations/${id}`, { method: 'DELETE' }),
}

export interface AskHandlers {
  onDelta: (text: string) => void
  onDone: (message: ConversationMessage) => void
  onError: (message: string) => void
}

/**
 * Asks the Librarian a question and streams the answer. The endpoint is a POST that answers with server-sent
 * events, which EventSource cannot send, so the stream is read and parsed directly.
 */
export async function ask(conversationId: string, text: string, handlers: AskHandlers, signal: AbortSignal) {
  const response = await fetch(`/api/conversations/${conversationId}/messages`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'knowledgebase' },
    body: JSON.stringify({ text }),
    signal,
  })

  if (response.status === 401) {
    signIn()
    return
  }
  if (!response.ok || !response.body) {
    let message = response.statusText
    try {
      message = (await response.json()).detail ?? message
    } catch {
      // Not a problem-details body.
    }
    handlers.onError(message)
    return
  }

  const reader = response.body.pipeThrough(new TextDecoderStream()).getReader()
  let buffer = ''
  for (;;) {
    const { value, done } = await reader.read()
    if (done) break
    buffer += value

    let boundary: number
    while ((boundary = buffer.indexOf('\n\n')) >= 0) {
      const block = buffer.slice(0, boundary)
      buffer = buffer.slice(boundary + 2)

      let event = 'message'
      const data: string[] = []
      for (const line of block.split('\n')) {
        if (line.startsWith('event:')) event = line.slice(6).trim()
        else if (line.startsWith('data:')) data.push(line.slice(5).trimStart())
      }
      if (data.length === 0) continue

      const payload = JSON.parse(data.join('\n'))
      if (event === 'delta') handlers.onDelta(payload.text)
      else if (event === 'done') handlers.onDone(payload.message)
      else if (event === 'error') handlers.onError(payload.error)
    }
  }
}
