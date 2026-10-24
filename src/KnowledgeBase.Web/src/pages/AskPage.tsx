import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { api, ask, type Conversation, type ConversationMessage } from '../api'
import { Empty, ErrorMessage, Loading, Markdown } from '../components'
import { formatDate } from '../format'

export function AskPage() {
  const { conversationId } = useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const conversations = useQuery({ queryKey: ['conversations'], queryFn: api.conversations })
  const create = useMutation({
    mutationFn: api.createConversation,
    onSuccess: (conversation) => {
      void queryClient.invalidateQueries({ queryKey: ['conversations'] })
      navigate(`/ask/${conversation.id}`)
    },
  })
  const remove = useMutation({
    mutationFn: api.deleteConversation,
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['conversations'] })
      navigate('/ask')
    },
  })

  return (
    <section className="page split">
      <aside className="list-pane">
        <header className="page-header">
          <h1>Ask</h1>
          <button onClick={() => create.mutate()} disabled={create.isPending}>
            New
          </button>
        </header>
        {conversations.isPending && <Loading what="conversations" />}
        {conversations.isError && <ErrorMessage error={conversations.error} />}
        {conversations.data?.length === 0 && <Empty>No conversations yet.</Empty>}
        <ul className="items">
          {conversations.data?.map((conversation) => (
            <li
              key={conversation.id}
              className={conversation.id === conversationId ? 'selected' : ''}
              onClick={() => navigate(`/ask/${conversation.id}`)}
            >
              <div className="item-title">{conversation.title}</div>
              <span className="muted small">{formatDate(conversation.updatedAt)}</span>
            </li>
          ))}
        </ul>
      </aside>
      <div className="detail-pane chat-pane">
        {conversationId ? (
          <Chat key={conversationId} id={conversationId} onDelete={() => remove.mutate(conversationId)} />
        ) : (
          <Empty>Start a new conversation to ask the Librarian about anything in your wiki.</Empty>
        )}
      </div>
    </section>
  )
}

function Chat({ id, onDelete }: { id: string; onDelete: () => void }) {
  const queryClient = useQueryClient()
  const queryKey = ['conversations', id]
  const conversation = useQuery({ queryKey, queryFn: () => api.conversation(id) })
  // The question being answered right now; saved messages come from the conversation itself.
  const [pending, setPending] = useState<ConversationMessage | null>(null)
  const [question, setQuestion] = useState('')
  const [streaming, setStreaming] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const abort = useRef<AbortController | null>(null)
  const bottom = useRef<HTMLDivElement>(null)

  useEffect(() => () => abort.current?.abort(), [])

  useEffect(() => bottom.current?.scrollIntoView({ behavior: 'smooth' }), [conversation.data, pending, streaming])

  const send = async () => {
    const text = question.trim()
    if (!text || streaming !== null) return

    const asked: ConversationMessage = { role: 'User', text, at: new Date().toISOString() }
    setQuestion('')
    setError(null)
    setPending(asked)
    setStreaming('')
    abort.current = new AbortController()

    try {
      await ask(
        id,
        text,
        {
          onDelta: (delta) => setStreaming((current) => (current ?? '') + delta),
          onDone: (answer) =>
            queryClient.setQueryData<Conversation>(queryKey, (current) =>
              current ? { ...current, messages: [...current.messages, asked, answer] } : current,
            ),
          onError: setError,
        },
        abort.current.signal,
      )
    } catch (caught) {
      if (!(caught instanceof DOMException && caught.name === 'AbortError')) {
        setError('The connection was lost while answering.')
      }
    } finally {
      setPending(null)
      setStreaming(null)
      void queryClient.invalidateQueries({ queryKey: ['conversations'] })
    }
  }

  if (conversation.isPending) return <Loading what="conversation" />
  if (conversation.isError) return <ErrorMessage error={conversation.error} />

  const messages = pending ? [...conversation.data.messages, pending] : conversation.data.messages

  return (
    <>
      <header className="detail-header">
        <h2>{conversation.data.title}</h2>
        <button className="secondary" onClick={onDelete} disabled={streaming !== null}>
          Delete
        </button>
      </header>
      <div className="messages">
        {messages.length === 0 && streaming === null && (
          <Empty>Ask about people, decisions, commitments, or patterns across your notes.</Empty>
        )}
        {messages.map((message, index) => (
          <div key={index} className={`message ${message.role.toLowerCase()}`}>
            {message.role === 'User' ? <p>{message.text}</p> : <Markdown content={message.text} />}
          </div>
        ))}
        {streaming !== null && (
          <div className="message assistant">
            {streaming ? <Markdown content={streaming} /> : <p className="muted">Searching the wiki…</p>}
          </div>
        )}
        {error && <p className="error">{error}</p>}
        <div ref={bottom} />
      </div>
      <div className="composer">
        <textarea
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !event.shiftKey) {
              event.preventDefault()
              void send()
            }
          }}
          placeholder="Ask a question… (Enter to send, Shift+Enter for a new line)"
          disabled={streaming !== null}
        />
        <button onClick={() => void send()} disabled={!question.trim() || streaming !== null}>
          Ask
        </button>
      </div>
    </>
  )
}
