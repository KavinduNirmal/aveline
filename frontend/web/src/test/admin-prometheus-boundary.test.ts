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

const adminFiles = [
  ...walk(resolve(srcRoot, 'routes/admin')),
  ...walk(resolve(srcRoot, 'components/admin')),
  ...walk(resolve(srcRoot, 'lib/admin')),
].filter((file) => /\.tsx?$/.test(file))

/**
 * The mechanical form of Q11 / §13.5.
 *
 * No console file may render a Prometheus series name. If a number comes from Prometheus it
 * arrives through the sibling workstream's proxy contract, whose label comes from its
 * `ExportedMetric` two-name contract — never from a second derivation here. This is what stops
 * the console becoming a third copy of the Grafana surface.
 */
describe('the Grafana boundary is mechanical', () => {
  it('contains no string matching ^aveline_ or ^pg_ in the admin tree', () => {
    const offenders: string[] = []
    for (const file of adminFiles) {
      const text = readFileSync(file, 'utf8')
      for (const match of text.matchAll(/\b(aveline_|pg_)[a-z0-9_]+/g)) {
        offenders.push(`${file.replace(srcRoot, 'src')}: ${match[0]}`)
      }
    }
    expect(offenders).toEqual([])
  })
})
