import { NavLink, Outlet } from 'react-router-dom'

/**
 * The research section: what is researched (topics), how the Pleb curated their queries (curation) and what never
 * leaves the machine (the deny-list).
 */
export function ResearchPage() {
  return (
    <section className="page narrow">
      <h1>Research</h1>
      <nav className="tabs">
        <NavLink to="/research/topics">Topics</NavLink>
        <NavLink to="/research/curation">Curation</NavLink>
        <NavLink to="/research/deny-list">Deny-list</NavLink>
      </nav>
      <Outlet />
    </section>
  )
}
