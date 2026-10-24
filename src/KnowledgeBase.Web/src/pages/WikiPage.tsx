import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { api, type WikiNode } from '../api'
import { Empty, ErrorMessage, Loading, Markdown, formatDate } from '../components'

export function WikiPage() {
  const [params] = useSearchParams()
  const page = params.get('page') ?? 'index.md'
  const tree = useQuery({ queryKey: ['wiki-tree'], queryFn: api.wikiTree })

  return (
    <section className="page split">
      <aside className="list-pane tree">
        <h1>Wiki</h1>
        {tree.isPending && <Loading what="wiki" />}
        {tree.isError && <ErrorMessage error={tree.error} />}
        {tree.data && <TreeChildren node={tree.data} selected={page} />}
      </aside>
      <div className="detail-pane">
        <PageView path={page} />
      </div>
    </section>
  )
}

function TreeChildren({ node, selected }: { node: WikiNode; selected: string }) {
  return (
    <ul className="tree-list">
      {node.children.map((child) =>
        child.type === 'Directory' ? (
          <TreeDirectory key={child.path} node={child} selected={selected} />
        ) : (
          <li key={child.path} className={child.path === selected ? 'selected' : ''}>
            <Link to={`/wiki?page=${encodeURIComponent(child.path)}`}>{child.name.replace(/\.md$/, '')}</Link>
          </li>
        ),
      )}
    </ul>
  )
}

function TreeDirectory({ node, selected }: { node: WikiNode; selected: string }) {
  const [open, setOpen] = useState(selected.startsWith(node.path))
  return (
    <li>
      <button className="link folder" onClick={() => setOpen(!open)}>
        {open ? '▾' : '▸'} {node.name}
      </button>
      {open && <TreeChildren node={node} selected={selected} />}
    </li>
  )
}

function PageView({ path }: { path: string }) {
  const page = useQuery({ queryKey: ['wiki-page', path], queryFn: () => api.wikiPage(path), retry: false })

  if (page.isPending) return <Loading what="page" />
  if (page.isError) return <Empty>This page does not exist (yet).</Empty>

  return (
    <>
      <p className="muted small">
        <code>{page.data.path}</code> · updated {formatDate(page.data.updatedAt)}
      </p>
      <div className="card">
        <Markdown content={page.data.content} basePath={page.data.path} />
      </div>
    </>
  )
}
