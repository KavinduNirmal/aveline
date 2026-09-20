import { readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

import { ADMIN_ROUTES } from './routes'

/**
 * Registry ⇄ router correspondence.
 *
 * `routes.ts` is the single source of truth for the panel, the breadcrumb and the registry
 * invariants, but it does **not** mount anything: `src/App.tsx` carries a hand-written
 * `<Route>` block, and until this test existed nothing checked the two agreed. A registered
 * entry with no `<Route>` falls through to the `path="*"` catch-all and silently redirects to
 * the dashboard, which is worse than a 404 because nothing looks broken.
 *
 * The test reads `App.tsx` from source rather than rendering the router: the console's routing
 * sits behind an auth guard and a session provider, so rendering it to observe a path table
 * would test far more than this invariant.
 */
const appSource = readFileSync(fileURLToPath(new URL('../../App.tsx', import.meta.url)), 'utf8')

/** Every file `App.tsx` imports for an admin page, e.g. `AdminBlossomsView`. */
function adminViewImports(): string[] {
  return [...appSource.matchAll(/import \{ (Admin\w+View) \}/g)].map((match) => match[1])
}

/**
 * The `path` of every `<Route>` whose element is one of those admin views.
 *
 * Scoped by element rather than by position, so a tenant route can never satisfy an admin
 * entry and the block's boundaries do not need to be guessed.
 */
function mountedAdminPaths(): Map<string, string> {
  const views = new Set(adminViewImports())
  const mounted = new Map<string, string>()

  for (const match of appSource.matchAll(/<Route\s+path="([^"]+)"\s+element=\{<(\w+)/g)) {
    const [, path, element] = match
    if (views.has(element)) mounted.set(path, element)
  }
  return mounted
}

describe('the admin registry is mounted by App.tsx', () => {
  it('parses a sane route block first, so a parser bug cannot masquerade as parity', () => {
    const mounted = mountedAdminPaths()
    // Large enough to be the real block, and containing known entries.
    expect(mounted.size).toBeGreaterThanOrEqual(10)
    expect(mounted.has('dashboard')).toBe(true)
    expect(mounted.has('blossoms')).toBe(true)
    // Tenant routes must not leak into the admin set.
    for (const path of mounted.keys()) {
      expect(path.startsWith('/'), `unexpected absolute tenant path ${path}`).toBe(false)
    }
  })

  it('mounts every enabled registry entry', () => {
    const mounted = mountedAdminPaths()
    for (const route of ADMIN_ROUTES) {
      if (!route.enabled) continue
      expect(
        mounted.has(route.subPath),
        `no <Route path="${route.subPath}"> for ${route.id}`,
      ).toBe(true)
    }
  })

  it('does not mount a route for an entry that is registered but not enabled', () => {
    // `enabled: false` means "the plan specifies this page and no slice has built it". It is
    // registered so the navigation's shape and the invariants are settled first, and it is
    // never linked — so mounting it would ship a page the panel deliberately hides.
    const mounted = mountedAdminPaths()
    for (const route of ADMIN_ROUTES) {
      if (route.enabled) continue
      expect(
        mounted.has(route.subPath),
        `${route.id} is not enabled but is mounted at "${route.subPath}"`,
      ).toBe(false)
    }
  })

  it('mounts exactly one route per registry sub-path', () => {
    const paths = [...mountedAdminPaths().keys()]
    expect(new Set(paths).size).toBe(paths.length)
  })

  it('imports a view for every mounted admin route, and mounts every imported one', () => {
    const mounted = new Set(mountedAdminPaths().values())
    for (const view of adminViewImports()) {
      expect(mounted.has(view), `${view} is imported but mounted nowhere`).toBe(true)
    }
    const imported = new Set(adminViewImports())
    for (const view of mounted.values()) {
      expect(imported.has(view), `${view} is mounted but not imported`).toBe(true)
    }
  })
})
