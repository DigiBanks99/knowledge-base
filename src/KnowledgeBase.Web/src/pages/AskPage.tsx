import { useEffect, useRef, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useNavigate, useParams } from 'react-router-dom'
import { ArrowUp, LoaderCircle, MessageSquarePlus, MessagesSquare, Plus, Trash2 } from 'lucide-react'
import { api, ask, type Conversation, type ConversationMessage } from '../api'
import { AgentIcon, BackLink, Empty, ErrorMessage, Loading, Markdown } from '../components'
import { formatDate } from '../format'

/** Starting points for an empty conversation; picking one fills the composer rather than sending it. */
const suggestions = [
  'What did I commit to this week?',
  'Who have I met with most recently?',
  'Which decisions are still open?',
]

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
    <section className={`page split${conversationId ? ' show-detail' : ''}`}>
      <aside className="list-pane">
        <header className="list-header">
          <span className="page-icon" aria-hidden>
            <MessagesSquare />
          </span>
          <h1>Ask</h1>
          <button onClick={() => create.mutate()} disabled={create.isPending}>
            <Plus aria-hidden />
            New
          </button>
        </header>
        {conversations.isPending && <Loading what="conversations" />}
        {conversations.isError && <ErrorMessage error={conversations.error} />}
        {conversations.data?.length === 0 && (
          <Empty icon={MessageSquarePlus}>No conversations yet. Start one to ask the Librarian.</Empty>
        )}
        <ul className="items">
          {conversations.data?.map((conversation) => (
            <li key={conversation.id} className={conversation.id === conversationId ? 'selected' : ''}>
              <button type="button" className="row" onClick={() => navigate(`/ask/${conversation.id}`)}>
                <span className="row-title">{conversation.title}</span>
                <span className="row-meta">{formatDate(conversation.updatedAt)}</span>
              </button>
            </li>
          ))}
        </ul>
      </aside>
      <div className="detail-pane chat-pane">
        {conversationId ? (
          <Chat key={conversationId} id={conversationId} onDelete={() => remove.mutate(conversationId)} />
        ) : (
          <Empty icon={MessagesSquare} hero>
            Start a new conversation to ask the Librarian about anything in your wiki.
          </Empty>
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
  const input = useRef<HTMLTextAreaElement>(null)

  useEffect(() => () => abort.current?.abort(), [])

  // A block body: newer browsers return a Promise from scrollIntoView, and React would take it for a cleanup function.
  useEffect(() => {
    bottom.current?.scrollIntoView({ behavior: 'smooth' })
  }, [conversation.data, pending, streaming])

  // The composer grows with the question, up to the height its style allows.
  useEffect(() => {
    const element = input.current
    if (!element) return
    element.style.height = 'auto'
    element.style.height = `${element.scrollHeight}px`
  }, [question])

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

  const back = <BackLink to="/ask">Conversations</BackLink>
  if (conversation.isPending)
    return (
      <>
        {back}
        <Loading what="conversation" />
      </>
    )
  if (conversation.isError)
    return (
      <>
        {back}
        <ErrorMessage error={conversation.error} />
      </>
    )

  const messages = pending ? [...conversation.data.messages, pending] : conversation.data.messages

  return (
    <>
      {back}
      <header className="detail-header">
        <h2>{conversation.data.title}</h2>
        <button
          className="ghost icon"
          onClick={onDelete}
          disabled={streaming !== null}
          title="Delete this conversation"
          aria-label="Delete this conversation"
        >
          <Trash2 aria-hidden />
        </button>
      </header>
      <div className="messages">
        {messages.length === 0 && streaming === null && (
          <Empty icon={MessagesSquare} hero>
            Ask about people, decisions, commitments, or patterns across your notes.
            <div className="suggestions">
              {suggestions.map((suggestion) => (
                <button
                  key={suggestion}
                  type="button"
                  className="secondary"
                  onClick={() => {
                    setQuestion(suggestion)
                    input.current?.focus()
                  }}
                >
                  {suggestion}
                </button>
              ))}
            </div>
          </Empty>
        )}
        {messages.map((message, index) => (
          <div key={index} className={`message ${message.role.toLowerCase()}`}>
            {message.role !== 'User' && <AgentIcon agent="Librarian" />}
            <div className="bubble">
              {message.role === 'User' ? <p>{message.text}</p> : <Markdown content={message.text} />}
            </div>
          </div>
        ))}
        {streaming !== null && (
          <div className="message assistant">
            <AgentIcon agent="Librarian" />
            <div className="bubble">
              {streaming ? (
                <Markdown content={streaming} />
              ) : (
                <p className="thinking">
                  <LoaderCircle className="spin" aria-hidden />
                  Searching the wiki…
                </p>
              )}
            </div>
          </div>
        )}
        {error && <ErrorMessage error={new Error(error)} />}
        <div ref={bottom} />
      </div>
      <div className="composer">
        <textarea
          ref={input}
          rows={1}
          aria-label="Question"
          value={question}
          onChange={(event) => setQuestion(event.target.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !event.shiftKey) {
              event.preventDefault()
              void send()
            }
          }}
          placeholder="Ask the Librarian…"
          disabled={streaming !== null}
        />
        <button
          onClick={() => void send()}
          disabled={!question.trim() || streaming !== null}
          title="Ask (Enter); Shift+Enter for a new line"
          aria-label="Ask"
        >
          <ArrowUp aria-hidden />
        </button>
      </div>
    </>
  )
}
