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
 * The remaining two rules (raw `<button>`/`<table>`/`<hr>`, and `space-x-*`/`space-y-*`) land
 * before A8.
 */
const PALETTE =
  /\b(?:bg|text|border|ring|from|to|via|fill|stroke|decoration|outline|shadow|divide|accent|caret|placeholder|ring-offset)-(?:slate|gray|grey|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\d{2,3}\b/g

const HEX = /#[0-9a-fA-F]{3,8}\b/g

const RAW_CONTROL = /<(select|input)\b/g

function offenders(pattern: RegExp): string[] {
  const found: string[] = []
  for (const file of adminFiles) {
    const matches = source(file).match(pattern)
    if (matches) {
      found.push(`${file.replace(srcRoot, 'src')}: ${[...new Set(matches)].join(', ')}`)
    }
  }
  return found
}

describe('A3 blocking conformance rules', () => {
  it('rule 1a: no raw palette utilities in the admin tree', () => {
    expect(offenders(PALETTE)).toEqual([])
  })

  it('rule 1b: no bare hex colours in the admin tree', () => {
    expect(offenders(HEX)).toEqual([])
  })

  it('rule 2: no raw <select> or <input> in the admin tree', () => {
    expect(offenders(RAW_CONTROL)).toEqual([])
  })
})
