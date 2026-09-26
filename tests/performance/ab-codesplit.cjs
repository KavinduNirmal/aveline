/**
 * A/B experiment: does route-level code splitting actually make the landing
 * page load faster, or does it only shrink a number in a build log?
 *
 * Method
 * ------
 * 1. Produce a SECOND production build in a scratch directory from a copy of
 *    `frontend/web/src` in which every `./routes/*` import in `App.tsx` is
 *    converted to `React.lazy` + `Suspense`. The real source tree is never
 *    touched: only a copy under `$TMPDIR` is edited.
 * 2. Serve BOTH builds (baseline `frontend/web/dist` and the split build) from
 *    a Vercel-shaped static server: brotli, immutable hashed assets, no-cache
 *    HTML.
 * 3. Measure them INTERLEAVED (A, B, A, B, ...) under the Lighthouse mobile
 *    profile, so machine drift and sandbox network variance hit both arms
 *    equally rather than one of them.
 *
 * Usage (from `frontend/web`):
 *   NODE_PATH=$PWD/node_modules HOME=/tmp node ../../tests/performance/ab-codesplit.cjs
 *
 * What it answers: the delta in FCP/LCP/TBT and in eager bytes between
 * "everything statically imported" and "routes lazily imported". It does NOT
 * model what else you would ship alongside (font subsetting, image sizing) -
 * those are separate arms.
 */

'use strict'

const http = require('node:http')
const fs = require('node:fs')
const path = require('node:path')
const os = require('node:os')
const zlib = require('node:zlib')
const { execFileSync } = require('node:child_process')

const REPO = path.resolve(__dirname, '..', '..')
const WEB = path.join(REPO, 'frontend', 'web')
const BASE_DIST = path.join(WEB, 'dist')
const OUT_DIR = path.join(__dirname, 'reports')
const SCRATCH = fs.mkdtempSync(path.join(os.tmpdir(), 'aveline-ab-'))
const SPLIT_DIST = path.join(SCRATCH, 'dist')

const { chromium } = require(path.join(WEB, 'node_modules', 'playwright'))
const CHROME = path.join(
  WEB,
  'node_modules',
  '.playwright-browsers',
  'chromium-1243',
  'chrome-linux64',
  'chrome',
)

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.woff2': 'font/woff2',
}

// ---------------------------------------------------------------- prototype

/**
 * Rewrite `App.tsx` so that every `import { A, B } from './routes/...'` becomes
 * a `React.lazy` call, and wrap the route table in `<Suspense>`.
 *
 * Only `./routes/*` is rewritten. `src/contexts/*` and the Sonner `<Toaster>`
 * are mounted by `App` itself and must stay eager - they are not route-scoped.
 */
function makeSplitPrototype() {
  const srcCopy = path.join(SCRATCH, 'src')
  fs.cpSync(path.join(WEB, 'src'), srcCopy, { recursive: true })
  fs.copyFileSync(path.join(WEB, 'index.html'), path.join(SCRATCH, 'index.html'))
  fs.symlinkSync(path.join(WEB, 'node_modules'), path.join(SCRATCH, 'node_modules'), 'dir')
  fs.writeFileSync(
    path.join(SCRATCH, 'vite.config.ts'),
    `import { fileURLToPath, URL } from 'node:url'
import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) } },
})
`,
  )

  const appPath = path.join(srcCopy, 'App.tsx')
  let app = fs.readFileSync(appPath, 'utf8')

  const importRe = /^import \{ ([A-Za-z0-9_, ]+) \} from '(\.\/routes\/[^']+)'$/gm
  let converted = 0
  app = app.replace(importRe, (_m, names, mod) => {
    const list = names.split(',').map((s) => s.trim()).filter(Boolean)
    converted += list.length
    return list
      .map(
        (n) => `const ${n} = lazy(() => import('${mod}').then((m) => ({ default: m.${n} })))`,
      )
      .join('\n')
  })
  app = app.replace(
    "import { AuthenticateWithRedirectCallback } from '@clerk/react'",
    "import { Suspense, lazy } from 'react'\nimport { AuthenticateWithRedirectCallback } from '@clerk/react'",
  )
  app = app.replace(
    '        <Routes>',
    "        <Suspense fallback={<div style={{ minHeight: '100vh' }} />}>\n        <Routes>",
  )
  app = app.replace('        </Routes>', '        </Routes>\n        </Suspense>')
  fs.writeFileSync(appPath, app)

  execFileSync(
    path.join(WEB, 'node_modules', '.bin', 'vite'),
    ['build', '--outDir', SPLIT_DIST, '--emptyOutDir'],
    { cwd: SCRATCH, stdio: 'pipe' },
  )
  return converted
}

// -------------------------------------------------------------- measurement

function createServer(distDir) {
  const server = http.createServer((req, res) => {
    let p = decodeURIComponent(req.url.split('?')[0])
    if (p === '/') p = '/index.html'
    let file = path.join(distDir, p)
    if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) {
      if (/\.[a-z0-9]+$/i.test(p)) {
        res.writeHead(404)
        res.end('nf')
        return
      }
      file = path.join(distDir, 'index.html')
    }
    const raw = fs.readFileSync(file)
    const ae = req.headers['accept-encoding'] || ''
    let body = raw
    let enc = 'identity'
    if (/br/.test(ae)) {
      body = zlib.brotliCompressSync(raw)
      enc = 'br'
    } else if (/gzip/.test(ae)) {
      body = zlib.gzipSync(raw)
      enc = 'gzip'
    }
    const hashed = /\/assets\/.*-[A-Za-z0-9_-]{8,}\.(js|css)$/.test(p)
    res.writeHead(200, {
      'Content-Type': MIME[path.extname(file)] || 'application/octet-stream',
      'Content-Encoding': enc,
      'Content-Length': body.length,
      'Cache-Control': hashed
        ? 'public, max-age=31536000, immutable'
        : 'public, max-age=0, must-revalidate',
    })
    res.end(body)
  })
  return server
}

/** Eager bytes: the entry script + modulepreloads + stylesheets in index.html. */
function eagerBytes(distDir) {
  const html = fs.readFileSync(path.join(distDir, 'index.html'), 'utf8')
  const refs = [
    ...new Set([
      ...[...html.matchAll(/<script[^>]*src="(\/assets\/[^"]+)"/g)].map((m) => m[1]),
      ...[...html.matchAll(/rel="modulepreload"[^>]*href="(\/assets\/[^"]+)"/g)].map((m) => m[1]),
      ...[...html.matchAll(/rel="stylesheet"[^>]*href="(\/assets\/[^"]+)"/g)].map((m) => m[1]),
    ]),
  ]
  const read = (f) => fs.readFileSync(path.join(distDir, f))
  return {
    files: refs.length,
    raw: refs.reduce((a, f) => a + read(f).length, 0),
    gzip: refs.reduce((a, f) => a + zlib.gzipSync(read(f)).length, 0),
    brotli: refs.reduce((a, f) => a + zlib.brotliCompressSync(read(f)).length, 0),
    list: refs,
  }
}

const PROBE = () => {
  const P = { lcp: [], cls: 0, long: [], paint: [], nav: null }
  window.__P = P
  const o = (t, cb) => {
    try {
      new PerformanceObserver((l) => {
        for (const e of l.getEntries()) cb(e)
      }).observe({ type: t, buffered: true })
    } catch (e) {}
  }
  o('largest-contentful-paint', (e) => P.lcp.push({ t: e.startTime, size: e.size }))
  o('layout-shift', (e) => {
    if (!e.hadRecentInput) P.cls += e.value
  })
  o('longtask', (e) => P.long.push({ s: e.startTime, d: e.duration }))
  o('paint', (e) => P.paint.push({ name: e.name, start: Math.round(e.startTime) }))
  o('navigation', (e) =>
    (P.nav = {
      dcl: Math.round(e.domContentLoadedEventEnd),
      load: Math.round(e.loadEventEnd),
      responseEnd: Math.round(e.responseEnd),
    }),
  )
}

async function measure(browser, origin, runs) {
  const rows = []
  for (let i = 0; i < runs; i++) {
    const ctx = await browser.newContext({
      viewport: { width: 412, height: 915 },
      deviceScaleFactor: 2.625,
      isMobile: true,
      hasTouch: true,
      userAgent:
        'Mozilla/5.0 (Linux; Android 13; SM-A536B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
    })
    const page = await ctx.newPage()
    const cdp = await ctx.newCDPSession(page)
    await cdp.send('Network.enable')
    await cdp.send('Network.emulateNetworkConditions', {
      offline: false,
      latency: 150,
      downloadThroughput: (1638.4 * 1000) / 8,
      uploadThroughput: (675 * 1000) / 8,
      connectionType: 'cellular4g',
    })
    await cdp.send('Emulation.setCPUThrottlingRate', { rate: 4 })
    await page.addInitScript(PROBE)
    await page.goto(origin + '/', { waitUntil: 'load', timeout: 120000 }).catch(() => {})
    // Observation window. LCP keeps updating until input arrives, and the
    // baseline arm's main-thread work runs well past DOMContentLoaded, so this
    // has to outlast both or the arms are not compared fairly.
    await page.waitForTimeout(15000)
    const d = await page.evaluate(() => ({
      P: window.__P,
      reqs: performance.getEntriesByType('resource').length,
      js: performance
        .getEntriesByType('resource')
        .filter((r) => r.name.endsWith('.js'))
        .reduce((a, r) => a + (r.transferSize || r.encodedBodySize || 0), 0),
      total: performance
        .getEntriesByType('resource')
        .reduce((a, r) => a + (r.transferSize || 0), 0),
    }))
    const P = d.P
    rows.push({
      fcp: (P.paint.find((p) => p.name === 'first-contentful-paint') || {}).start ?? null,
      lcp: P.lcp.length ? Math.round(P.lcp[P.lcp.length - 1].t) : null,
      tbt: Math.round(P.long.reduce((a, l) => a + Math.max(0, l.d - 50), 0)),
      busy: Math.round(P.long.reduce((a, l) => a + l.d, 0)),
      dcl: P.nav ? P.nav.dcl : null,
      jsBytes: d.js,
      totalBytes: d.total,
      reqs: d.reqs,
    })
    await ctx.close()
  }
  return rows
}

const median = (rows, k) => {
  const v = rows.map((r) => r[k]).filter((x) => typeof x === 'number').sort((a, b) => a - b)
  return v.length ? v[Math.floor((v.length - 1) / 2)] : null
}

async function main() {
  if (!fs.existsSync(BASE_DIST)) throw new Error(`No baseline build at ${BASE_DIST}`)
  const converted = makeSplitPrototype()

  const srvA = createServer(BASE_DIST)
  const srvB = createServer(SPLIT_DIST)
  await new Promise((r) => srvA.listen(0, '127.0.0.1', r))
  await new Promise((r) => srvB.listen(0, '127.0.0.1', r))
  const originA = `http://127.0.0.1:${srvA.address().port}`
  const originB = `http://127.0.0.1:${srvB.address().port}`

  const bytesA = eagerBytes(BASE_DIST)
  const bytesB = eagerBytes(SPLIT_DIST)

  const browser = await chromium.launch({
    executablePath: CHROME,
    args: ['--no-sandbox', '--disable-gpu', '--disable-dev-shm-usage'],
    env: { ...process.env, HOME: process.env.PERF_HOME || '/tmp' },
  })

  const RUNS = Number(process.env.PERF_AB_RUNS || 3)
  const A = []
  const B = []
  // Interleaved: alternate arms so drift cannot favour one of them.
  for (let i = 0; i < RUNS; i++) {
    A.push(...(await measure(browser, originA, 1)))
    B.push(...(await measure(browser, originB, 1)))
  }

  await browser.close()
  srvA.close()
  srvB.close()

  const summarise = (rows) => ({
    runs: rows.length,
    fcpMs: median(rows, 'fcp') && Math.round(median(rows, 'fcp')),
    lcpMs: median(rows, 'lcp'),
    tbtMs: median(rows, 'tbt'),
    mainThreadBusyMs: median(rows, 'busy'),
    domContentLoadedMs: median(rows, 'dcl'),
    jsTransferBytes: median(rows, 'jsBytes'),
    totalTransferBytes: median(rows, 'totalBytes'),
    requests: median(rows, 'reqs'),
  })

  const report = {
    generatedAt: new Date().toISOString(),
    profile: 'mobile Slow 4G (Lighthouse) + 4x CPU, cold cache, 412x915',
    note: 'A = shipped build. B = same source with App.tsx routes converted to React.lazy + Suspense, built in a scratch copy.',
    routeComponentsLazyLoaded: converted,
    eagerBytes: {
      baseline: { files: bytesA.files, raw: bytesA.raw, gzip: bytesA.gzip, brotli: bytesA.brotli },
      codesplit: { files: bytesB.files, raw: bytesB.raw, gzip: bytesB.gzip, brotli: bytesB.brotli },
    },
    runtime: { baseline: summarise(A), codesplit: summarise(B) },
    rawRuns: { baseline: A, codesplit: B },
  }

  fs.mkdirSync(OUT_DIR, { recursive: true })
  const out = path.join(OUT_DIR, 'ab-codesplit.json')
  fs.writeFileSync(out, JSON.stringify(report, null, 2))

  const pct = (a, b) => (a && b ? (((a - b) / a) * 100).toFixed(1) + '%' : 'n/a')
  const line = (label, a, b, unit = '') =>
    `${label.padEnd(26)}${String(a).padStart(9)}${unit}${String(b).padStart(12)}${unit}  ${pct(a, b).padStart(8)}`

  console.log('A/B: route-level code splitting')
  console.log(`lazy-loaded route components: ${converted}`)
  console.log(`scratch build: ${SCRATCH}`)
  console.log('')
  console.log('eager payload              baseline    code-split   reduction')
  console.log(line('  files', bytesA.files, bytesB.files))
  console.log(line('  raw bytes', bytesA.raw, bytesB.raw))
  console.log(line('  gzip bytes', bytesA.gzip, bytesB.gzip))
  console.log(line('  brotli bytes', bytesA.brotli, bytesB.brotli))
  console.log('')
  console.log(`runtime (median of ${RUNS} interleaved runs)`)
  console.log('                           baseline    code-split   reduction')
  for (const [label, key] of [
    ['FCP (ms)', 'fcpMs'],
    ['LCP (ms)', 'lcpMs'],
    ['TBT (ms)', 'tbtMs'],
    ['main-thread busy (ms)', 'mainThreadBusyMs'],
    ['DOMContentLoaded (ms)', 'domContentLoadedMs'],
    ['JS transfer (bytes)', 'jsTransferBytes'],
    ['total transfer (bytes)', 'totalTransferBytes'],
    ['requests', 'requests'],
  ]) {
    console.log(line('  ' + label, report.runtime.baseline[key], report.runtime.codesplit[key]))
  }
  console.log('')
  console.log(`report: ${out}`)
}

main().catch((e) => {
  console.error(e)
  process.exit(1)
})
