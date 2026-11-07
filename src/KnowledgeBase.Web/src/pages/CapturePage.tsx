import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { ArrowRight, CircleCheck, LoaderCircle, Send, Sparkles, Users, Waypoints } from 'lucide-react'
import { api } from '../api'
import { ErrorMessage } from '../components'

export function CapturePage({ name }: { name?: string }) {
  const [content, setContent] = useState('')
  const capture = useMutation({
    mutationFn: api.captureNote,
    onSuccess: () => setContent(''),
  })

  const submit = () => {
    if (content.trim()) capture.mutate(content)
  }

  const words = content.trim() ? content.trim().split(/\s+/).length : 0

  return (
    <section className="page narrow">
      <div>
        <p className="greeting">{greeting(name)}</p>
        <h1 className="capture-title">What’s on your mind?</h1>
        <p className="page-description">
          Write in the first person, as much or as little as you like. The agents extract the facts and file them in the
          wiki.
        </p>

        <div className="capture-box">
          <textarea
            aria-label="Note"
            value={content}
            onChange={(event) => setContent(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) submit()
            }}
            placeholder="What happened? Who was involved? What did you decide, learn or commit to?"
            autoFocus
          />
          <div className="capture-toolbar">
            <span className="muted">
              {words > 0 ? `${words} ${words === 1 ? 'word' : 'words'}` : ''}
              <span className="keyboard-hint">
                {words > 0 ? ' · ' : ''}
                <kbd>Ctrl</kbd> + <kbd>Enter</kbd> to capture
              </span>
            </span>
            <button onClick={submit} disabled={!content.trim() || capture.isPending}>
              {capture.isPending ? <LoaderCircle className="spin" aria-hidden /> : <Send aria-hidden />}
              {capture.isPending ? 'Capturing…' : 'Capture'}
            </button>
          </div>
        </div>

        <div className="feedback">
          {capture.isError && <ErrorMessage error={capture.error} />}
          {capture.isSuccess && (
            <p className="success" role="status">
              <CircleCheck aria-hidden />
              <span>
                Captured as <code>{capture.data.name}</code>.
              </span>
              <Link to={`/notes?note=${encodeURIComponent(capture.data.name)}`}>
                Follow its progress <ArrowRight size={16} aria-hidden />
              </Link>
            </p>
          )}
        </div>

        <ul className="tips">
          <li>
            <Users aria-hidden />
            <span>
              <strong>Name the people</strong>
              Who was there and what they said or asked for.
            </span>
          </li>
          <li>
            <Sparkles aria-hidden />
            <span>
              <strong>Say what changed</strong>
              Decisions, commitments and anything you learnt.
            </span>
          </li>
          <li>
            <Waypoints aria-hidden />
            <span>
              <strong>Let the agents link it</strong>
              Facts are woven into the right wiki pages.
            </span>
          </li>
        </ul>
      </div>
    </section>
  )
}

function greeting(name: string | undefined): string {
  const hour = new Date().getHours()
  const part = hour < 12 ? 'Good morning' : hour < 18 ? 'Good afternoon' : 'Good evening'
  const first = name?.trim().split(/\s+/)[0]
  return first ? `${part}, ${first}` : part
}
