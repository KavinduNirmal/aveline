import { existsSync, readFileSync } from 'node:fs'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * The route table's code-splitting invariant.
 *
 * `src/App.tsx` used to statically import all 41 route components, which put the entire
 * application — the admin console with Recharts, the docs engine with Mermaid and highlight.js,
 * the tenant dashboard — on the critical path of an anonymous visit to `/`. Vite only splits on
 * dynamic `import()`, so the fix is a property of this one file, and this file is where the
 * regression would reappear: one added static import silently puts 100+ KB gzip back on every
 * first visit and no functional test would notice.
 *
 * So the invariant is asserted at the source level, next to the existing registry/parity check
 * in `src/lib/admin/routes.router.test.ts`, and at the byte level by the deterministic budget
 * assertions in `tests/performance/budgets.spec.ts`.
 */
const appSource = readFileSync(fileURLToPath(new URL('./App.tsx', import.meta.url)), 'utf8')

/**
 * Route modules that are deliberately eager.
 *
 * `DashboardRedirect` resolves a session and then navigates; deferring it would turn one
 * navigation into two round trips, and it is a few hundred bytes of logic over dependencies that
 * are already in the entry chunk. It is the only exception, and adding to this set should be a
 * deliberate act with a comment in `App.tsx` explaining it.
 */
const EAGER_ROUTE_IMPORTS = new Set(['./routes/Dashboard'])

/** Static `import ... from './routes/...'` specifiers in `App.tsx`. */
function staticRouteImports(): string[] {
  return [...appSource.matchAll(/import \{[^}]*\} from '(\.[^']*)'/g)]
    .map((match) => match[1])
    .filter((specifier) => specifier.includes('/routes/'))
}

/** Every `import('./routes/...')` specifier reachable from a `lazyRoute(...)` binding. */
function lazyRouteImports(): string[] {
  return [...appSource.matchAll(/import\('(\.[^']*)'\)/g)].map((match) => match[1])
}

describe('App.tsx code-splits the route table', () => {
  it('statically imports no route module except the documented eager exception', () => {
    const offenders = staticRouteImports().filter(
      (specifier) => !EAGER_ROUTE_IMPORTS.has(specifier),
    )
    expect(
      offenders,
      `${offenders.join(', ')} are statically imported by App.tsx. Every route element must be ` +
        `bound with lazyRoute(() => import(...)) so Vite emits it as its own chunk; a static ` +
        `import puts the whole route on the critical path of every visit.`,
    ).toEqual([])
  })

  it('binds a large number of routes lazily, so a parser bug cannot masquerade as a pass', () => {
    // 41 routes are mounted today. The bound is deliberately well below that: it exists to fail
    // loudly if this parser stops matching, not to pin the exact count.
    expect(lazyRouteImports().length).toBeGreaterThanOrEqual(30)
  })

  it('every lazy route import resolves to a file that exists', () => {
    const missing = lazyRouteImports().filter(
      (specifier) => !existsSync(fileURLToPath(new URL(specifier, import.meta.url)) + '.tsx')
        && !existsSync(fileURLToPath(new URL(specifier, import.meta.url)) + '.ts'),
    )
    expect(missing, `unresolvable dynamic imports: ${missing.join(', ')}`).toEqual([])
  })

  it('every lazyRoute binding names an export that its module actually exports', () => {
    // A dynamic import that resolves but destructures the wrong name yields `undefined` at
    // render time, which React reports as "Element type is invalid" at whatever route the user
    // happens to open. Cheap to catch here.
    const bindings = [...appSource.matchAll(/lazyRoute\(\s*\(\)\s*=>\s*import\('(\.[^']*)'\),\s*'(\w+)'/g)]
    expect(bindings.length).toBeGreaterThanOrEqual(30)
    for (const [, specifier, exportName] of bindings) {
      const source = readFileSync(
        fileURLToPath(new URL(specifier, import.meta.url)) + '.tsx',
        'utf8',
      )
      expect(
        new RegExp(`export (function|const|class) ${exportName}\\b`).test(source),
        `${specifier} does not export \`${exportName}\``,
      ).toBe(true)
    }
  })
})
