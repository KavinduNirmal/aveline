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

const adminTreeFiles = [
  ...walk(resolve(srcRoot, 'routes/admin')),
  ...walk(resolve(srcRoot, 'components/admin')),
].filter((file) => /\.tsx?$/.test(file) && !/\.test\./.test(file))

const allSourceFiles = walk(srcRoot).filter(
  (file) => /\.tsx?$/.test(file) && !/\.test\./.test(file),
)

/**
 * The two mechanical rules of slice A1. Both are greps on purpose: the defect they pin was a
 * hand-written chart and a hand-written identity, and neither shows up in a behaviour test.
 */
describe('A1 truthfulness rules', () => {
  it('contains no raw <svg> markup anywhere in the admin tree', () => {
    const offenders = adminTreeFiles.filter((file) => readFileSync(file, 'utf8').includes('<svg'))
    expect(offenders.map((file) => file.replace(srcRoot, 'src'))).toEqual([])
  })

  it('contains no "kaveesha" literal anywhere in src', () => {
    const offenders = allSourceFiles.filter((file) =>
      /kaveesha/i.test(readFileSync(file, 'utf8')),
    )
    expect(offenders.map((file) => file.replace(srcRoot, 'src'))).toEqual([])
  })

  it('does not ship the unimported RequireAdmin component', () => {
    const exists = adminTreeFiles.some((file) => file.endsWith('RequireAdmin.tsx'))
    expect(exists).toBe(false)
  })
})
