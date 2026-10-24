import { useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { Link } from 'react-router-dom'
import { api } from '../api'
import { ErrorMessage } from '../components'

export function CapturePage() {
  const [content, setContent] = useState('')
  const capture = useMutation({
    mutationFn: api.captureNote,
    onSuccess: () => setContent(''),
  })

  const submit = () => {
    if (content.trim()) capture.mutate(content)
  }

  return (
    <section className="page narrow">
      <h1>Capture a note</h1>
      <p className="muted">
        Write in the first person, as much or as little as you like. The agents extract the facts and file them in the
        wiki.
      </p>
      <textarea
        className="capture"
        value={content}
        onChange={(event) => setContent(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === 'Enter' && (event.ctrlKey || event.metaKey)) submit()
        }}
        placeholder="What happened? Who was involved? What did you decide, learn or commit to?"
        autoFocus
      />
      <div className="actions">
        <span className="muted small">Ctrl+Enter to capture</span>
        <button onClick={submit} disabled={!content.trim() || capture.isPending}>
          {capture.isPending ? 'Capturing…' : 'Capture'}
        </button>
      </div>
      {capture.isError && <ErrorMessage error={capture.error} />}
      {capture.isSuccess && (
        <p className="success">
          Captured as <code>{capture.data.name}</code>.{' '}
          <Link to={`/notes?note=${encodeURIComponent(capture.data.name)}`}>Follow its progress</Link>
        </p>
      )}
    </section>
  )
}
