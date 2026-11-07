import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { ArrowLeft, BookOpen, ChevronRight, Clock, FileQuestion, FileText, Folder, FolderOpen, ListTree } from 'lucide-react'
import { api, type WikiNode } from '../api'
import { Empty, ErrorMessage, Loading, Markdown } from '../components'
import { formatDate } from '../format'

export function WikiPage() {
  const [params] = useSearchParams()
  const page = params.get('page') ?? 'index.md'
  const tree = useQuery({ queryKey: ['wiki-tree'], queryFn: api.wikiTree })
  // On a phone the page and the tree take turns; picking a page shows it again.
  const [browsing, setBrowsing] = useState(false)

  return (
    <section className={`page split${browsing ? '' : ' show-detail'}`}>
      <aside className="list-pane tree">
        <button type="button" className="back mobile-only" onClick={() => setBrowsing(false)}>
          <ArrowLeft aria-hidden />
          Back to the page
        </button>
        <header className="list-header">
          <span className="page-icon" aria-hidden>
            <BookOpen />
          </span>
          <h1>Wiki</h1>
        </header>
        {tree.isPending && <Loading what="wiki" />}
        {tree.isError && <ErrorMessage error={tree.error} />}
        {tree.data && <TreeChildren node={tree.data} selected={page} onPick={() => setBrowsing(false)} />}
      </aside>
      <div className="detail-pane">
        <div>
          <div className="wiki-toolbar">
            <Breadcrumbs path={page} />
            <button type="button" className="secondary browse" onClick={() => setBrowsing(true)}>
              <ListTree aria-hidden />
              Pages
            </button>
          </div>
          <PageView path={page} />
        </div>
      </div>
    </section>
  )
}

function Breadcrumbs({ path }: { path: string }) {
  const parts = path.replace(/\.md$/, '').split('/')
  return (
    <nav className="breadcrumbs" aria-label="Page path">
      <Link to="/wiki" aria-label="Wiki home">
        <BookOpen aria-hidden />
      </Link>
      {parts.map((part, index) => (
        <span key={index} className="crumb">
          <ChevronRight aria-hidden />
          <span>{part}</span>
        </span>
      ))}
    </nav>
  )
}

function TreeChildren({ node, selected, onPick }: { node: WikiNode; selected: string; onPick: () => void }) {
  return (
    <ul className="tree-list">
      {node.children.map((child) =>
        child.type === 'Directory' ? (
          <TreeDirectory key={child.path} node={child} selected={selected} onPick={onPick} />
        ) : (
          <li key={child.path} className={child.path === selected ? 'selected' : ''}>
            <Link to={`/wiki?page=${encodeURIComponent(child.path)}`} onClick={onPick}>
              <FileText aria-hidden />
              {child.name.replace(/\.md$/, '')}
            </Link>
          </li>
        ),
      )}
    </ul>
  )
}

function TreeDirectory({ node, selected, onPick }: { node: WikiNode; selected: string; onPick: () => void }) {
  const [open, setOpen] = useState(selected.startsWith(node.path))
  const Icon = open ? FolderOpen : Folder
  return (
    <li>
      <button type="button" className="link folder" aria-expanded={open} onClick={() => setOpen(!open)}>
        <ChevronRight className="chevron" aria-hidden />
        <Icon aria-hidden />
        {node.name}
      </button>
      {open && <TreeChildren node={node} selected={selected} onPick={onPick} />}
    </li>
  )
}

function PageView({ path }: { path: string }) {
  const page = useQuery({ queryKey: ['wiki-page', path], queryFn: () => api.wikiPage(path), retry: false })

  if (page.isPending) return <Loading what="page" />
  if (page.isError)
    return (
      <Empty icon={FileQuestion} hero>
        This page does not exist (yet).
      </Empty>
    )

  return (
    <article className="card article">
      <Markdown content={page.data.content} basePath={page.data.path} />
      <p className="meta article-meta">
        <span>
          <Clock aria-hidden />
          Updated {formatDate(page.data.updatedAt)}
        </span>
        <span>
          <code>{page.data.path}</code>
        </span>
      </p>
    </article>
  )
}
