import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const webRoot = fileURLToPath(new URL('../..', import.meta.url))
const srcRoot = resolve(webRoot, 'src')

function read(relative: string): string {
  return readFileSync(resolve(srcRoot, relative), 'utf8')
}

/** Comments are documentation; a rule about shipped copy must not fire on prose about it. */
function code(source: string): string {
  return source
    .split('\n')
    .filter((line) => {
      const trimmed = line.trimStart()
      return !trimmed.startsWith('//') && !trimmed.startsWith('*') && !trimmed.startsWith('/*')
    })
    .join('\n')
}

const shell = read('components/dashboard/DashboardShell.tsx')
const app = read('App.tsx')

/**
 * Q6 made the dashboard section part of the URL: `/app/b/:slug/:section`, with a redirect from
 * the bare slug route. These assertions are the invariant that keeps the two halves in step — the
 * nav table (`SECTIONS`) and the router — because a section added to one and not the other is a
 * link that 404s or a route that renders no panel.
 */
describe('tenant dashboard section routing (Q6)', () => {
  it('declares the section route and redirects the bare slug route to overview', () => {
    expect(app).toMatch(/path="\/app\/b\/:slug\/:section"/)
    expect(app).toMatch(
      /path="\/app\/b\/:slug"[\s\S]{0,120}<Navigate to="overview" replace \/>/,
    )
  })

  it('routes every nav section through the URL rather than local state', () => {
    // The shell must not keep a `useState<SectionId>` for the section any more.
    expect(shell).not.toMatch(/useState<SectionId>/)
    expect(shell).toMatch(/useParams<\{ section\?: string \}>/)
    expect(shell).toMatch(/navigate\(`\/app\/b\/\$\{organization\.slug\}\/\$\{next\}`\)/)
  })

  it('declares a unique, non-empty section id for every nav entry', () => {
    const ids = [...shell.matchAll(/\{ id: '([a-z-]+)', label:/g)].map((m) => m[1])
    expect(ids.length).toBeGreaterThanOrEqual(10)
    expect(new Set(ids).size).toBe(ids.length)
    expect(ids).toContain('overview')
    expect(ids).toContain('billing')
    expect(ids).toContain('team')
  })

  it('gates Team on team:manage and leaves Integrations on settings:manage', () => {
    const sections = [...shell.matchAll(/\{ id: '([a-z-]+)',[^}]*?permission: '([^']+)'/g)].map(
      (m) => ({ id: m[1], permission: m[2] }),
    )
    const byId = Object.fromEntries(sections.map((s) => [s.id, s.permission]))
    expect(byId.team).toBe('team:manage')
    expect(byId.integrations).toBe('settings:manage')
    expect(byId.approvals).toBe('approvals:approve')
  })

  it('claims no invoice feature in the billing placeholder copy', () => {
    // TD8: invoices are deferred, and the placeholder promised them.
    expect(code(shell)).not.toMatch(/invoices/i)
    expect(shell).toMatch(/'Plan, statement & payment methods'/)
  })
})
