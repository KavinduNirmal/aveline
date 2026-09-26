import { readFileSync, readdirSync } from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { describe, expect, it } from 'vitest'

/**
 * Every `<img>` in the application carries a loading hint.
 *
 * At audit time all 25 were bare: no `loading`, no `decoding`, no `srcset`, no intrinsic
 * dimensions. Every photograph in the product — catalog thumbnails, chat attachments, the
 * landing page's hero slideshow — was fetched eagerly at full source resolution, and the three
 * hero slides asked Pexels for `w=1400` into a 672 px box.
 *
 * This is a source-level gate rather than a browser assertion on purpose: the Playwright budget
 * suite in `tests/performance/` needs a headless Chrome and third-party network access, and it
 * is not wired into `test-web` yet. This one runs wherever `bun run test` runs, which is where CI
 * actually looks, and it cannot pass vacuously — it counts the images it checked.
 *
 * `loading="eager"` plus `fetchpriority="high"` is a legitimate answer for an image that must be
 * requested immediately; what is not allowed is saying nothing and leaving the browser to guess.
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

/** Comments are stripped first: `<img>` appears inside prose in `blocks.tsx`, and a tag scan that
 *  starts there would run forward to the next real `/>` and swallow a genuine element. */
function stripComments(source: string): string {
  return source.replace(/\/\*[\s\S]*?\*\//g, '').replace(/^[ \t]*\/\/.*$/gm, '')
}

/** `<img ...>` / `<img ... />` blocks, including multi-line tags. */
function imgTags(): { file: string; tag: string }[] {
  const found: { file: string; tag: string }[] = []
  for (const file of tsxFiles(SRC)) {
    const source = stripComments(readFileSync(file, 'utf8'))
    for (const match of source.matchAll(/<img\b[\s\S]*?\/>/g)) {
      found.push({ file: path.relative(SRC, file), tag: match[0] })
    }
  }
  return found
}

describe('every image declares a loading hint', () => {
  it('finds the images it is meant to be checking', () => {
    // Guards against the regex silently matching nothing, which is the failure mode that made an
    // earlier version of the Playwright gate pass while detecting nothing.
    const tags = imgTags()
    expect(tags.length).toBeGreaterThanOrEqual(20)
  })

  it('gives every <img> a loading or fetchpriority attribute', () => {
    const offenders = imgTags()
      .filter(({ tag }) => !/\bloading="/.test(tag) && !/\bfetchPriority=/i.test(tag))
      .map(({ file, tag }) => `${file}: ${tag.split('\n')[0].trim()}`)
    expect(
      offenders,
      `${offenders.length} image(s) load eagerly with no declared priority:\n${offenders.join('\n')}`,
    ).toEqual([])
  })

  it('decodes every <img> off the main thread', () => {
    const offenders = imgTags()
      .filter(({ tag }) => !/\bdecoding="/.test(tag))
      .map(({ file }) => file)
    expect(offenders, `missing decoding="async": ${offenders.join(', ')}`).toEqual([])
  })

  it('asks the hero slideshow for a size-appropriate image, not the original', () => {
    const source = readFileSync(
      fileURLToPath(new URL('./components/site/HeroSlideshow.tsx', import.meta.url)),
      'utf8',
    )
    // The slides render into `max-w-2xl` (672px) but requested `w=1400` from Pexels — 3.6x the
    // pixels any layout could use, per slide, on the landing page. Matched inside the URL rather
    // than as a bare string so the explanatory comments can still name the old value.
    expect(source).not.toMatch(/images\.pexels\.com\/[^'"`\s]*w=1400/)
    expect(source).toContain('srcSet=')
    expect(source).toContain('sizes=')
  })
})
