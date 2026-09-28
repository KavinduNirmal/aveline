import { existsSync, readFileSync, readdirSync } from 'node:fs'
import { resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

const webRoot = fileURLToPath(new URL('../..', import.meta.url))
const uiDir = resolve(webRoot, 'src/components/ui')

const ADDED_PRIMITIVES = [
  'chart',
  'sidebar',
  'empty',
  'pagination',
  'item',
  'spinner',
  'breadcrumb',
] as const

/**
 * A0's install invariants. The shadcn CLI is known to corrupt two conventions in this repo
 * (risk R-A14): it would add a second Radix copy under `@radix-ui/react-*` and it would
 * downgrade Recharts through the `chart` manifest. These assertions make that failure mode
 * mechanical rather than a review note.
 */
describe('A0 third-party and primitive invariants', () => {
  it('keeps recharts pinned to the project’s working major', () => {
    const pkg = JSON.parse(readFileSync(resolve(webRoot, 'package.json'), 'utf8'))
    expect(pkg.dependencies.recharts).toBe('^3.10.1')
  })

  it('never introduces the scoped @radix-ui/react-* packages', () => {
    const offenders = readdirSync(uiDir)
      .filter((name) => name.endsWith('.tsx'))
      .filter((name) => readFileSync(resolve(uiDir, name), 'utf8').includes('@radix-ui/react-'))
    expect(offenders).toEqual([])
  })

  it('imports cn from the "cn" package in every added primitive', () => {
    for (const primitive of ADDED_PRIMITIVES) {
      const file = resolve(uiDir, `${primitive}.tsx`)
      expect(existsSync(file), `${primitive}.tsx is missing`).toBe(true)
      expect(
        readFileSync(file, 'utf8').includes('from "cn"'),
        `${primitive}.tsx must import cn from "cn"`,
      ).toBe(true)
    }
  })

  it('ships bun.lock as the only lockfile', () => {
    expect(existsSync(resolve(webRoot, 'bun.lock'))).toBe(true)
    expect(existsSync(resolve(webRoot, 'pnpm-lock.yaml'))).toBe(false)
    expect(existsSync(resolve(webRoot, 'package-lock.json'))).toBe(false)
    expect(existsSync(resolve(webRoot, 'yarn.lock'))).toBe(false)
  })

  it('provides the mobile hook the sidebar depends on', () => {
    expect(existsSync(resolve(webRoot, 'src/hooks/use-mobile.ts'))).toBe(true)
  })
})
