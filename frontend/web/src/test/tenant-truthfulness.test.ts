import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const webRoot = fileURLToPath(new URL('../..', import.meta.url))
const srcRoot = resolve(webRoot, 'src')

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry)
    return statSync(full).isDirectory() ? walk(full) : [full]
  })
}

function exists(dir: string): boolean {
  try {
    return statSync(dir).isDirectory()
  } catch {
    return false
  }
}

const tenantFiles = [
  resolve(srcRoot, 'routes', 'TenantDashboard.tsx'),
  resolve(srcRoot, 'routes', 'Dashboard.tsx'),
  ...walk(resolve(srcRoot, 'components/dashboard')),
  // The catalogue panel hangs off the dashboard shell and is where a metric fallback or an
  // invented tenant id does real damage (F-9), so it is inside the gate.
  ...walk(resolve(srcRoot, 'components/catalog')),
  ...(exists(resolve(srcRoot, 'components/shared'))
    ? walk(resolve(srcRoot, 'components/shared'))
    : []),
].filter((file) => /\.tsx?$/.test(file) && !/\.test\./.test(file))

function source(file: string): string {
  return readFileSync(file, 'utf8')
}

/**
 * Comments are documentation, not claims on a screen, so a rule must not fire on prose that
 * *describes* the thing it forbids (the "Demo mode" comment explains why it was deleted).
 */
function code(file: string): string {
  return source(file)
    .split('\n')
    .filter((line) => {
      const trimmed = line.trimStart()
      return !trimmed.startsWith('//') && !trimmed.startsWith('*') && !trimmed.startsWith('/*')
    })
    .join('\n')
}

function offenders(pattern: RegExp): string[] {
  const found: string[] = []
  for (const file of tenantFiles) {
    const matches = code(file).match(pattern)
    if (matches) {
      found.push(`${file.replace(srcRoot, 'src')}: ${[...new Set(matches)].join(', ')}`)
    }
  }
  return found
}

/**
 * The tenant truthfulness gate. The admin console has the same shape of gate
 * (`admin-truthfulness.test.ts`) and it is the reason the console cannot fabricate a number.
 *
 * Every rule here is a **literal, mechanically checkable** pattern. The plan's §6.8 prose rules
 * ("every chart has a null state", "every metric is object-accessed through the dataQuality
 * DTO") are not expressible as source scans, and a gate that cannot fail is not a gate; that
 * intent lives in the DOM tests instead.
 */
describe('tenant truthfulness rules', () => {
  it('walks a non-empty tenant tree, so a broken glob cannot pass vacuously', () => {
    expect(tenantFiles.length).toBeGreaterThan(5)
  })

  it('rule 1: never tells the user the product is a demo', () => {
    // The shell carried a literal "Demo mode" chip whenever the balance was missing, which
    // turned "the API did not measure this" into a claim about the whole product.
    expect(offenders(/['"`][^'"`]*\bdemo mode\b[^'"`]*['"`]/gi)).toEqual([])
  })

  it('rule 2: never defaults a Blossom measure to zero', () => {
    // `UsagePanel` computed `usage?.blossomUsed ?? 0` and rendered "0 used" for a period the API
    // had not measured. On a dashboard, an invented zero reads as a real measurement — the exact
    // failure the admin console's `KpiTile` ("not measured") exists to prevent.
    expect(offenders(/\b(?:blossom|allowance|usage)\w*\s*(?:\?\.\w+)?\s*\?\?\s*0\b/gi)).toEqual([])
  })

  it('rule 3: no tenant metric is coerced from null to zero with a bare ?? 0', () => {
    expect(offenders(/\b(?:remaining|collected|revenue|margin|total|subtotal|discount)\w*\s*\?\?\s*0\b/gi)).toEqual([])
  })

  it('rule 4: recharts is imported only by the chart wrapper', () => {
    // A raw recharts import bypasses `components/ui/chart.tsx`, which is where the shared
    // `CONNECT_NULLS` decision lives: `connectNulls: true` draws a line straight through a gap
    // and reports a value the server never measured.
    expect(offenders(/from\s+['"]recharts['"]/g)).toEqual([])
  })

  it('rule 5: never invents a tenant id', () => {
    // F-9. The catalogue panel fell back to `'00000000-0000-0000-0000-000000000001'` when the
    // organisation was missing, so a save or an image upload addressed **a real organisation that
    // is not the caller's**. A wrong tenant id is worse than an error.
    expect(offenders(/00000000-0000-0000-0000-000000000001/g)).toEqual([])
  })

  it('rule 6: a failed save does not render a draft the server rejected', () => {
    // The catalogue panel used to log the failure, show a toast and then fall through to
    // `setInventory(...)`, so a rejected piece appeared in the list as though it had persisted.
    expect(offenders(/Retaining local draft/gi)).toEqual([])
  })

  it('rule 7: never renders the word invoice, because the product has no invoice (D8)', () => {
    // Q2 deferred invoices: there is no invoice entity, no numbering rule, no payment provider and
    // no currency column. The section is a statement of account, and a reader must not be shown a
    // word that promises a document the repository cannot produce. Rule 7 is what makes the T5
    // gate's "the word does not appear in the tree" mechanically true.
    expect(offenders(/\binvoices?\b/gi)).toEqual([])
  })
})
