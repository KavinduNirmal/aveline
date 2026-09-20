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

/** The admin tree, excluding tests. `src/components/ui/**` is the primitive implementation. */
const adminFiles = [
  ...walk(resolve(srcRoot, 'routes/admin')),
  ...walk(resolve(srcRoot, 'components/admin')),
].filter((file) => /\.tsx?$/.test(file) && !/\.test\./.test(file))

function source(file: string): string {
  return readFileSync(file, 'utf8')
}

/**
 * The two blocking conformance rules of A3 (strategy C6).
 *
 * oxlint in this repository has no custom-rule plugin API, so the rules are enforced where they
 * bind just as hard: the test suite, which CI runs on every push. Rule 1 — raw palette utilities
 * and bare hex colours — is the class that breaks dark mode. Rule 2 — raw `<select>`/`<input>` —
 * is where the accessibility work lives, because the shadcn primitives carry the labels, focus
 * rings and error wiring.
 *
 * **The allow-list.** A genuinely bespoke component may carry a marker comment beside the
 * exception, as C6 requires:
 *
 *     conformance-allow: raw-button — <why no primitive covers this>
 *
 * `offenders` skips a file for a rule only when that marker is present. The only current exception
 * is the log viewer's virtualised row (see `components/admin/logs/LogRow.tsx`).
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
  for (const file of adminFiles) {
    if (allowed(file, rule)) continue
    const matches = source(file).match(pattern)
    if (matches) {
      found.push(`${file.replace(srcRoot, 'src')}: ${[...new Set(matches)].join(', ')}`)
    }
  }
  return found
}

describe('A3 blocking conformance rules', () => {
  it('rule 1a: no raw palette utilities in the admin tree', () => {
    expect(offenders(PALETTE, 'raw-palette')).toEqual([])
  })

  it('rule 1b: no bare hex colours in the admin tree', () => {
    expect(offenders(HEX, 'raw-hex')).toEqual([])
  })

  it('rule 2: no raw <select> or <input> in the admin tree', () => {
    expect(offenders(RAW_CONTROL, 'raw-control')).toEqual([])
  })

  it('rule 3: no raw <button>, <table> or <hr> in the admin tree', () => {
    expect(offenders(RAW_ELEMENT, 'raw-button')).toEqual([])
  })

  it('rule 4: layout spacing uses gap, never space-x-*/space-y-*', () => {
    expect(offenders(SPACE_UTILITY, 'space-utilities')).toEqual([])
  })

  it('keeps the allow-list honest: every exception is a documented marker', () => {
    const markers: string[] = []
    for (const file of adminFiles) {
      const text = source(file)
      for (const match of text.matchAll(/conformance-allow:\s*([a-z-]+)/g)) {
        markers.push(`${file.replace(srcRoot, 'src')}: ${match[1]}`)
      }
    }
    // Grow this list only with a reason beside the exception.
    expect(markers).toEqual(['src/components/admin/logs/LogRow.tsx: raw-button'])
  })

  it('every chart series sets connectNulls from the shared CONNECT_NULLS constant', () => {
    // A `null` must be a gap. `connectNulls: true` is the classic silent lie: the line runs
    // through zero and the chart reports a value the server never measured.
    const found: string[] = []
    for (const file of adminFiles) {
      const text = source(file)
      for (const match of text.matchAll(/<(?:Line|Area)\b[\s\S]*?\/>/g)) {
        const element = match[0]
        if (!element.includes('connectNulls={CONNECT_NULLS}')) {
          found.push(`${file.replace(srcRoot, 'src')}: ${element.slice(0, 60)}…`)
        }
      }
    }
    expect(found).toEqual([])
  })
})
