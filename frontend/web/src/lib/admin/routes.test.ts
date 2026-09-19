import { describe, expect, it } from 'vitest'

import { ALL_PERMISSIONS } from './permissions'
import {
  ADMIN_DOMAINS,
  ADMIN_ROUTES,
  declaredPermissions,
  findRouteById,
  navigableRoutes,
  routesForDomain,
} from './routes'

/**
 * Registry invariants. The navigation and the router read the same list, so these are the
 * properties that keep the panel honest: nothing links to a page the caller cannot use, and
 * nothing declares a permission the server does not define.
 */
describe('ADMIN_ROUTES invariants', () => {
  it('has unique ids and unique subPaths', () => {
    const ids = ADMIN_ROUTES.map((route) => route.id)
    const subPaths = ADMIN_ROUTES.map((route) => route.subPath)
    expect(new Set(ids).size).toBe(ids.length)
    expect(new Set(subPaths).size).toBe(subPaths.length)
  })

  it('declares a gate on every non-Overview entry', () => {
    for (const route of ADMIN_ROUTES) {
      if (route.domain === 'overview') continue
      expect(route.gate, `${route.id} must declare a gate`).not.toBeNull()
    }
  })

  it('declares only permissions that exist in the catalogue', () => {
    const catalogue = new Set<string>(ALL_PERMISSIONS)
    for (const permission of declaredPermissions()) {
      expect(catalogue.has(permission), `${permission} is not a real permission`).toBe(true)
    }
  })

  it('gives every domain at least two entries', () => {
    for (const domain of ADMIN_DOMAINS) {
      expect(routesForDomain(domain).length, `domain ${domain}`).toBeGreaterThanOrEqual(2)
    }
  })

  it('uses every declared domain, and declares every used domain', () => {
    const used = new Set(ADMIN_ROUTES.map((route) => route.domain))
    expect([...used].sort()).toEqual([...ADMIN_DOMAINS].sort())
  })

  it('never links a route that is not enabled', () => {
    for (const route of navigableRoutes()) {
      expect(route.enabled, `${route.id} is linked but not enabled`).toBe(true)
    }
  })

  it('resolves its own entries by id', () => {
    expect(findRouteById('dashboard')?.subPath).toBe('dashboard')
    expect(findRouteById('does-not-exist')).toBeUndefined()
  })
})
