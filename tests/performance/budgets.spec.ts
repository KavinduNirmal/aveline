/**
 * Performance budget gates for the built `frontend/web` bundle.
 *
 * Why these exist: before this suite the `test-web` CI job built the bundle,
 * uploaded it as an artifact, and asserted nothing about it
 * (`.github/workflows/ci.yml:162-218`). The bundle could double overnight and
 * every check would stay green. These are the smallest set of assertions that
 * would have caught the regression this audit is about.
 *
 * They are RATCHETS, not aspirations. The budgets below are set slightly above
 * the value measured at audit time (revision c2ec531) so they pass today and
 * fail only when something gets worse. When you make the bundle smaller, lower
 * the number in the same commit — that is the whole mechanism.
 *
 * Run:
 *   cd frontend/web
 *   HOME=/tmp NODE_PATH=$PWD/node_modules \
 *     node_modules/.bin/playwright test -c ../../tests/performance/playwright.perf.config.ts
 *
 * `HOME` must be writable: Chrome needs it for its profile directory.
 */

import { expect, test } from '@playwright/test'
import fs from 'node:fs'
import path from 'node:path'
import zlib from 'node:zlib'

const REPO = path.resolve(__dirname, '..', '..')
const DIST = path.join(REPO, 'frontend', 'web', 'dist')

/**
 * Budgets measured at audit time. Each is a ceiling, not a target.
 *
 * `eagerGzipBytes` is the payload the browser is explicitly told to fetch
 * before the app can run: the entry script plus every `modulepreload` plus the
 * stylesheet, all brotli/gzip-compressed. Today this is dominated by one
 * unsplit 597 KB-gzip entry chunk; after route-level code splitting it should
 * fall by roughly 73 % and this number must be lowered to lock that in.
 */
const BUDGET = {
  // 925,563 measured before route splitting; 197,886 after. The budget sits just above the
  // post-split value: it is a ratchet on the split, so a static route import back into
  // `src/App.tsx` fails here rather than shipping.
  eagerGzipBytes: 240_000,
  // 2,731,826 before; 513,869 after. The remainder is react-dom, Clerk, the router, axios,
  // signalr, sonner and the app shell — the floor the audit identified as genuinely shared.
  entryChunkBytes: 560_000,
  // 16 before (entry + 14 modulepreload + stylesheet), 6 after (entry + 4 shared + stylesheet).
  eagerFileCount: 10,
  // The image defect is fixed: all 25 `<img>` in src carry `loading` + `decoding`, and the hero
  // slides use srcset. This assertion is now a real gate rather than an inverted one.
  maxImagesWithoutLazyLoading: 0,
  // LCP is text (`h1.mt-6.max-w-xl.font-serif`), so no image may be prioritised above it. The
  // landing page's hero photograph is decoration and must not compete for a Slow 4G pipe.
  maxHighPriorityImagesOnLanding: 0,
}

/**
 * A phrase that only exists in the landing page's hero copy. If it is in the entry chunk, the
 * landing route was not split out — which is the whole regression this gate exists to catch, and
 * a byte-count assertion alone cannot distinguish "smaller because split" from "smaller because
 * something was deleted".
 */
const LANDING_ONLY_MARKER = 'so your staff don'

/** Everything `index.html` tells the browser to fetch before running the app. */
function eagerAssets() {
  const html = fs.readFileSync(path.join(DIST, 'index.html'), 'utf8')
  const collect = (re) => [...html.matchAll(re)].map((m) => m[1])
  const entry = collect(/<script[^>]*src="(\/assets\/[^"]+)"/g)
  const preload = collect(/rel="modulepreload"[^>]*href="(\/assets\/[^"]+)"/g)
  const css = collect(/rel="stylesheet"[^>]*href="(\/assets\/[^"]+)"/g)
  return { entry, preload, css, all: [...new Set([...entry, ...preload, ...css])] }
}

const read = (rel) => fs.readFileSync(path.join(DIST, rel))

test.describe('bundle budgets', () => {
  test('the build exists', () => {
    expect(fs.existsSync(path.join(DIST, 'index.html')), `${DIST} has no index.html — run \`bun run build\``).toBe(true)
  })

  test('eager payload is within the gzip budget', () => {
    const { all } = eagerAssets()
    const gzip = all.reduce((sum, f) => sum + zlib.gzipSync(read(f)).length, 0)
    expect(
      gzip,
      `Eager JS+CSS is ${(gzip / 1024).toFixed(0)} KB gzip across ${all.length} files ` +
        `(budget ${(BUDGET.eagerGzipBytes / 1024).toFixed(0)} KB). ` +
        `If this grew, something new is on the critical path — check for a static ` +
        `import of a heavy module in src/App.tsx or src/components/docs/Mermaid.tsx.`,
    ).toBeLessThan(BUDGET.eagerGzipBytes)
  })

  test('the entry chunk is within budget', () => {
    const { entry } = eagerAssets()
    expect(entry, 'index.html should reference exactly one entry script').toHaveLength(1)
    const bytes = read(entry[0]).length
    expect(
      bytes,
      `Entry chunk is ${(bytes / 1024).toFixed(0)} KB (budget ${(BUDGET.entryChunkBytes / 1024).toFixed(0)} KB). ` +
        `This is the single file every route pays for.`,
    ).toBeLessThan(BUDGET.entryChunkBytes)
  })

  test('the number of eagerly-preloaded files is bounded', () => {
    const { all } = eagerAssets()
    expect(
      all.length,
      `${all.length} files are eagerly fetched. A rising count means more of the ` +
        `app is reachable from the static import graph of src/App.tsx.`,
    ).toBeLessThan(BUDGET.eagerFileCount)
  })

  test('the documentation engine is not eagerly preloaded', () => {
    // Promoted from `test.fail(true)`. `src/components/docs/Mermaid.tsx` no longer imports
    // mermaid statically — the import is dynamic inside its render effect, and the docs route is
    // itself a lazy chunk — so mermaid's own chunk graph has left the HTML `modulepreload` set.
    const { preload } = eagerAssets()
    const mermaidish = preload.filter((f) => /rough\.esm|architectureDiagram|c4Diagram|^\/assets\/chunk-/.test(f))
    expect(mermaidish, `Mermaid-family chunks are eagerly preloaded: ${mermaidish.join(', ')}`).toHaveLength(0)
  })

  test('the landing route is not in the entry chunk', () => {
    // The byte budget alone could be satisfied by deleting code. This asserts the mechanism:
    // the landing page's hero copy must live in a lazily-loaded chunk, not in the entry.
    const { entry } = eagerAssets()
    const entrySource = read(entry[0]).toString('utf8')
    expect(
      entrySource.includes(LANDING_ONLY_MARKER),
      `The entry chunk contains landing-page copy ("${LANDING_ONLY_MARKER}"), so the landing route ` +
        `is still statically reachable from src/App.tsx.`,
    ).toBe(false)
  })

  test('the admin console is not in the entry chunk', () => {
    // Recharts (265 KB of generated bytes) is the console's single largest dependency and used to
    // be billed to every anonymous visitor. TanStack Query travelled with it.
    const { entry } = eagerAssets()
    const entrySource = read(entry[0]).toString('utf8')
    for (const marker of ['recharts', 'mermaid']) {
      expect(
        entrySource.includes(marker),
        `The entry chunk references "${marker}" — an admin/docs-only dependency is back on the ` +
          `critical path of every visit.`,
      ).toBe(false)
    }
  })
})

test.describe('landing page on a mobile viewport', () => {
  // The suite default is 30 s, which is shorter than the render wait below.
  // Without this, a slow cold start fails on the overall test timeout and the
  // error blames the wrong thing.
  test.describe.configure({ timeout: 120_000 })

  test.use({
    viewport: { width: 390, height: 844 },
    deviceScaleFactor: 3,
    isMobile: true,
    hasTouch: true,
    // An explicit mobile UA is required, not cosmetic. With `isMobile: true`
    // but a desktop UA the app did not mount at all in this environment,
    // which silently turned these assertions into vacuous passes. Using the
    // Desktop Chrome device default while emulating mobile is the trap.
    userAgent:
      'Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
  })

  /** The landing page's hero heading. Waiting on it proves React actually mounted. */
  const HERO = 'h1'

  /**
   * These three assertions need the SPA to have actually mounted, which needs
   * the whole 2.7 MB entry chunk parsed, Clerk's third-party script fetched
   * from `*.clerk.accounts.dev`, and a real paint. In the audit sandbox that
   * succeeded only intermittently — headless Chrome sometimes left `#root`
   * empty and emitted no paint-timing entries at all (the same flakiness that
   * made the A/B harness's FCP/LCP arm unusable).
   *
   * So: attempt the render, and if the app does not mount, SKIP with a message
   * that names the environment rather than asserting something that was never
   * observed. A skipped test here is an honest "not measured"; a passing one
   * would be a lie. On real CI hardware with network access to Clerk these
   * should run.
   */
  async function mountOrSkip(page: import('@playwright/test').Page) {
    await page.goto('/')
    const mounted = await page
      .locator(HERO)
      .first()
      .waitFor({ state: 'visible', timeout: 60_000 })
      .then(() => true)
      .catch(() => false)
    test.skip(
      !mounted,
      'The landing page did not mount in this environment within 60 s (headless Chrome ' +
        'left #root empty, or no paint occurred). This is an environment limitation — ' +
        'the app needs ~2.7 MB of JS and a third-party Clerk script before it renders. ' +
        'Re-run on a machine with working outbound access to *.clerk.accounts.dev.',
    )
  }

  test('renders and does not overflow horizontally', async ({ page }) => {
    await mountOrSkip(page)
    const overflow = await page.evaluate(
      () => document.documentElement.scrollWidth - document.documentElement.clientWidth,
    )
    expect(overflow, `Landing page overflows horizontally by ${overflow}px at 390px wide`).toBeLessThanOrEqual(1)
  })

  test('the LCP element is text, not an image', async ({ page }) => {
    // Recorded deliberately. The LCP element at audit time was
    // `h1.mt-6.max-w-xl.font-serif` — text, gated on JavaScript executing and
    // on the Playfair Display webfont. This means image optimisation is NOT an
    // LCP strategy for this app. If this ever fails because LCP became an
    // image, the optimisation priorities change with it.
    await mountOrSkip(page)
    const lcpTag = await page.evaluate(
      () =>
        new Promise<string | null>((resolve) => {
          let last: string | null = null
          try {
            new PerformanceObserver((list) => {
              for (const e of list.getEntries()) {
                const el = (e as PerformanceEntry & { element?: Element }).element
                last = el ? el.tagName : null
              }
            }).observe({ type: 'largest-contentful-paint', buffered: true })
          } catch {
            resolve(null)
          }
          setTimeout(() => resolve(last), 3000)
        }),
    )
    test.skip(lcpTag === null, 'no LCP entry was observed in this environment')
    expect(['H1', 'H2', 'P', 'SPAN', 'DIV', 'A']).toContain(lcpTag)
  })

  test('images carry a loading hint', async ({ page }) => {
    // Promoted from `test.fail(true)`. All 25 `<img>` in src now carry `loading` + `decoding`,
    // and the hero slides use `srcset`/`sizes` instead of requesting Pexels at w=1400. The
    // source-level half of this gate lives in `frontend/web/src/image-hints.test.ts`, which runs
    // in `test-web` without a browser; this half verifies the attributes survive into the DOM.
    await mountOrSkip(page)
    const result = await page.evaluate(() => {
      const imgs = [...document.images]
      return {
        total: imgs.length,
        // Use getAttribute, NOT `img.loading` / `img.decoding`. Those IDL
        // properties default to "eager" / "auto" — non-empty strings, and
        // therefore truthy — so a truthiness check silently matches nothing
        // and the test passes without testing anything. This was a real bug in
        // the first revision of this file.
        offenders: imgs
          .filter(
            (img) =>
              !img.getAttribute('loading') &&
              !img.getAttribute('decoding') &&
              !img.getAttribute('width'),
          )
          .map((img) => img.currentSrc || img.src),
        // The LCP element on this page is text, so nothing here should be competing with it.
        highPriority: imgs
          .filter((img) => (img.getAttribute('fetchpriority') ?? '').toLowerCase() === 'high')
          .map((img) => img.currentSrc || img.src),
      }
    })
    // Guard against a vacuous pass: with no images this test would otherwise
    // succeed without checking anything.
    expect(result.total, 'no images found on the landing page — the assertion would be vacuous').toBeGreaterThan(0)
    expect(
      result.offenders.length,
      `${result.offenders.length} of ${result.total} landing-page image(s) have no ` +
        `loading/decoding/width hint:\n` +
        result.offenders.slice(0, 5).join('\n'),
    ).toBeLessThanOrEqual(BUDGET.maxImagesWithoutLazyLoading)
    expect(
      result.highPriority.length,
      `${result.highPriority.length} landing-page image(s) are marked fetchpriority=high while ` +
        `the LCP element is text:\n${result.highPriority.slice(0, 5).join('\n')}`,
    ).toBeLessThanOrEqual(BUDGET.maxHighPriorityImagesOnLanding)
  })
})
