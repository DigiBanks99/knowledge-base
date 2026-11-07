import { NavLink, Outlet } from 'react-router-dom'
import { ShieldBan, Tags, Telescope } from 'lucide-react'
import { PageHeader } from '../components'

/**
 * The research section: what is researched (topics, each with its own page of runs) and what never leaves the machine
 * (the deny-list).
 */
export function ResearchPage() {
  return (
    <section className="page narrow">
      <div>
        <PageHeader icon={Telescope} title="Research" />
        <nav className="tabs">
          <NavLink to="/research/topics">
            <Tags aria-hidden />
            Topics
          </NavLink>
          <NavLink to="/research/deny-list">
            <ShieldBan aria-hidden />
            Deny-list
          </NavLink>
        </nav>
        <Outlet />
      </div>
    </section>
  )
}
