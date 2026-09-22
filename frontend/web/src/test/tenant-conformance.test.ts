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

/**
 * The tenant dashboard tree: the dashboard shell and its panels, the catalogue panel that hangs
 * off it, the two tenant routes, and (once it exists) `src/components/shared/**`. The shared
 * directory is walked only when it exists, so the gate is green before the shared primitives are
 * promoted and covers them the moment they are — a new directory must not be born invisible to
 * the rule that governs it.
 *
 * **Deliberately not walked: the public marketing/authentication pages** (`LandingPage`,
 * `PlansPage`, `SignUpPage`, `SignInPage`, `TermsPage`, `ContactPage`, `DownloadPage`,
 * `InvitePage`, `AdminPendingPage`, `AdminSignUpPage`). Those are a separate surface with their
 * own brand palette and migration plan; pulling them into this gate would triple its scope
 * without serving the tenant dashboard. The plan's §6.7 scope is the four entries above.
 */
const tenantFiles = [
  resolve(srcRoot, 'routes', 'TenantDashboard.tsx'),
  resolve(srcRoot, 'routes', 'Dashboard.tsx'),
  ...walk(resolve(srcRoot, 'components/dashboard')),
  ...walk(resolve(srcRoot, 'components/catalog')),
  ...(exists(resolve(srcRoot, 'components/shared'))
    ? walk(resolve(srcRoot, 'components/shared'))
    : []),
].filter((file) => /\.tsx?$/.test(file) && !/\.test\./.test(file))

function source(file: string): string {
  return readFileSync(file, 'utf8')
}

/**
 * The tenant conformance rules, adopted from the admin gate (`admin-conformance.test.ts`) after
 * the tenant-dashboard strategy measured ~160 violations across 18 files (C-11, Q12: "fix
 * everything, primarily use shadcn primitives"). The admin tree is the precedent and this gate is
 * deliberately the same rule set, so a component that moves between the two trees does not change
 * its obligations.
 *
 * A genuinely bespoke component may carry an allow marker beside the exception:
 *
 *     conformance-allow: raw-button — <why no primitive covers this>
 *
 * The tenant gate ships with an **empty** allow-list, which is stricter than the admin gate.
 */
const PALETTE =
  /\b(?:bg|text|border|ring|from|to|via|fill|stroke|decoration|outline|shadow|divide|accent|caret|placeholder|ring-offset)-(?:slate|gray|grey|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}\b/g

const HEX = /#[0-9a-fA-F]{3,8}\b/g

const RAW_CONTROL = /<(select|input)\b/g

const RAW_ELEMENT = /<(button|table|hr)\b/g

const SPACE_UTILITY = /\bspace-[xy]-[0-9.]+\b/g

function allowed(file: string, rule: string): boolean {
  return new RegExp(`conformance-allow:\\s*${rule}\\b`).test(source(file))
}

function offenders(pattern: RegExp, rule: string): string[] {
  const found: string[] = []
  for (const file of tenantFiles) {
    if (allowed(file, rule)) continue
    const matches = source(file).match(pattern)
    if (matches) {
      found.push(`${file.replace(srcRoot, 'src')}: ${[...new Set(matches)].join(', ')}`)
    }
  }
  return found
}

describe('tenant conformance rules', () => {
  it('walks a non-empty tenant tree, so a broken glob cannot pass vacuously', () => {
    expect(tenantFiles.length).toBeGreaterThan(5)
    expect(tenantFiles.some((file) => file.includes('DashboardShell.tsx'))).toBe(true)
  })

  it('rule 1a: no raw palette utilities in the tenant tree', () => {
    expect(offenders(PALETTE, 'raw-palette')).toEqual([])
  })

  it('rule 1b: no bare hex colours in the tenant tree', () => {
    expect(offenders(HEX, 'raw-hex')).toEqual([])
  })

  it('rule 2: no raw <select> or <input> in the tenant tree', () => {
    expect(offenders(RAW_CONTROL, 'raw-control')).toEqual([])
  })

  it('rule 3: no raw <button>, <table> or <hr> in the tenant tree', () => {
    expect(offenders(RAW_ELEMENT, 'raw-button')).toEqual([])
  })

  it('rule 4: layout spacing uses gap, never space-x-*/space-y-*', () => {
    expect(offenders(SPACE_UTILITY, 'space-utilities')).toEqual([])
  })

  it('keeps the allow-list empty: an exception is argued in code', () => {
    const markers: string[] = []
    for (const file of tenantFiles) {
      for (const match of source(file).matchAll(/conformance-allow:\s*([a-z-]+)/g)) {
        markers.push(`${file.replace(srcRoot, 'src')}: ${match[1]}`)
      }
    }
    expect(markers).toEqual([])
  })
})
