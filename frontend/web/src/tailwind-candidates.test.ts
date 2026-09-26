import { readFileSync, readdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * A Tailwind class inside a `className` template literal must not be glued to `${`.
 *
 * This gate exists because that exact mistake shipped. `AuroraField`'s blur layer was written as
 *
 *     className={`absolute rounded-full blur-[70px]${staticOnly ? '' : ' aveline-aurora-blob'}`}
 *
 * Tailwind v4 finds candidates by scanning source text. With no separator between the utility and
 * the interpolation the candidate reads as `blur-[70px]${staticOnly`, which matches nothing, so
 * `.blur-\[70px\]` was never emitted into the stylesheet. Nothing failed: TypeScript was happy,
 * lint was happy, every test passed, and the only symptom was that six 700 px gradient blobs
 * rendered as hard-edged circles instead of a soft aurora. A sibling class in
 * `FlowerAuroraBackground` written as a plain `"blur-[110px]"` literal was emitted normally, which
 * is why only one component was affected.
 *
 * The rule is deliberately narrow so it cannot cry wolf: inside a `className` template literal,
 * every `${` must be preceded by whitespace or be the first thing in the string. `cn()` and plain
 * string literals are unaffected, because there the utility is always a complete token.
 *
 * This is the source-level half. It cannot prove a class was *emitted* — only that it could be —
 * so a utility that resolves to nothing for some other reason still needs the computed-style check
 * (`testing/performance/ab-aurora.cjs` reports `getComputedStyle(el).filter`).
 */
const SRC = fileURLToPath(new URL('.', import.meta.url))

function tsxFiles(dir: string, found: string[] = []): string[] {
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = path.join(dir, entry.name)
    if (entry.isDirectory()) tsxFiles(full, found)
    else if (entry.name.endsWith('.tsx') && !entry.name.includes('.test.')) found.push(full)
  }
  return found
}

const CLASSNAME_TEMPLATE = /className=\{`([\s\S]*?)`\}/g

function gluedCandidates(): { file: string; line: number; context: string }[] {
  const found: { file: string; line: number; context: string }[] = []
  for (const file of tsxFiles(SRC)) {
    const source = readFileSync(file, 'utf8')
    for (const match of source.matchAll(CLASSNAME_TEMPLATE)) {
      const body = match[1]
      for (const interp of body.matchAll(/\$\{/g)) {
        const index = interp.index ?? 0
        const previous = index === 0 ? '' : body[index - 1]
        if (previous && !/\s/.test(previous)) {
          found.push({
            file: path.relative(SRC, file),
            line: source.slice(0, match.index).split('\n').length,
            context: body.slice(Math.max(0, index - 52), index + 22).replace(/\n/g, ' '),
          })
        }
      }
    }
  }
  return found
}

describe('Tailwind candidates in className template literals', () => {
  it('never glues a class to an interpolation', () => {
    const offenders = gluedCandidates()
    expect(
      offenders,
      offenders
        .map(
          (o) =>
            `${o.file}:${o.line} — ...${o.context}...\n` +
            `  Tailwind cannot extract a candidate glued to \`\${}\`, so the class is silently ` +
            `never emitted. Use cn('utility', condition && 'extra') or a plain string literal.`,
        )
        .join('\n'),
    ).toEqual([])
  })

  it('still scans the files it is meant to be checking', () => {
    // Guard against a broken glob turning this gate green: the codebase uses className template
    // literals in a few places, so the scanner must be finding them.
    let templates = 0
    for (const file of tsxFiles(SRC)) {
      templates += [...readFileSync(file, 'utf8').matchAll(CLASSNAME_TEMPLATE)].length
    }
    expect(templates).toBeGreaterThan(0)
  })
})
