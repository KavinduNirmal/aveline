/**
 * Aveline web performance baseline harness.
 *
 * Why this exists: the repository has no Lighthouse CI, no RUM and no bundle
 * budget, so there is no recorded baseline for `frontend/web`. This script
 * produces one, reproducibly, from the real production build in
 * `frontend/web/dist`.
 *
 * It is deliberately dependency-light: it uses the `playwright` and
 * `playwright-core` packages that `frontend/web` already installs (they power
 * `bun run test:e2e`), and drives Chrome DevTools Protocol directly to apply
 * the same throttling profiles Lighthouse uses. No new network install is
 * required, which matters on a locked-down CI runner.
 *
 * Usage (from `frontend/web`, so `node_modules` resolves):
 *
 *   NODE_PATH=$PWD/node_modules node ../../tests/performance/measure.cjs
 *
 * Optional environment overrides:
 *   PERF_DIST      directory to serve           (default frontend/web/dist)
 *   PERF_OUT       report path                  (default tests/performance/reports/)
 *   PERF_SCENARIOS comma list of scenario names (default: all)
 *   PERF_LABEL     label recorded in the report (default: "local")
 *
 * Every metric reported here is captured in the browser, not modelled:
 * FCP/LCP/CLS come from PerformanceObserver, TBT is the sum of long-task
 * overrun past 50 ms, and byte weights come from `transferSize`. Speed Index
 * is NOT reported - it needs a filmstrip, which CDP screencast cannot give at
 * a fidelity worth publishing. Say "not measured" rather than guess.
 */

'use strict'

const http = require('node:http')
const fs = require('node:fs')
const path = require('node:path')
const zlib = require('node:zlib')

const REPO = path.resolve(__dirname, '..', '..')
const DIST = process.env.PERF_DIST
  ? path.resolve(process.env.PERF_DIST)
  : path.join(REPO, 'frontend', 'web', 'dist')
const OUT_DIR = process.env.PERF_OUT
  ? path.resolve(process.env.PERF_OUT)
  : path.join(__dirname, 'reports')
const LABEL = process.env.PERF_LABEL || 'local'
/**
 * Chrome writes its profile into `$HOME`, so a read-only `$HOME` (locked-down
 * CI runner, or this repository's audit sandbox) makes it die at startup with
 * "Failed to create headless user data directory container". Pointing `HOME` at
 * a writable temp dir is the whole fix; `PERF_HOME` lets a runner override it.
 */
const BROWSER_ENV = { ...process.env, HOME: process.env.PERF_HOME || '/tmp' }

const BROWSERS_DIR = path.join(
  REPO,
  'frontend',
  'web',
  'node_modules',
  '.playwright-browsers',
)

const CHROME =
  process.env.PERF_CHROME ||
  path.join(BROWSERS_DIR, 'chromium-1243', 'chrome-linux64', 'chrome')

/** Headless shell: same Blink, no browser chrome. Used as a fallback. */
const HEADLESS_SHELL = path.join(
  BROWSERS_DIR,
  'chromium_headless_shell-1243',
  'chrome-headless-shell-linux64',
  'chrome-headless-shell',
)

const { chromium } = require(path.join(REPO, 'frontend', 'web', 'node_modules', 'playwright'))

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.json': 'application/json',
  '.map': 'application/json',
  '.woff2': 'font/woff2',
  '.woff': 'font/woff',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
}

/**
 * Lighthouse's own mobile throttling profile, applied through CDP rather than
 * simulated in a lab model. Values from Lighthouse 12 `throttling.mobileSlow4G`:
 * rtt 150 ms, 1638.4 kbps down / 675 kbps up, 4x CPU slowdown.
 * (Lighthouse's simulated model uses 1474.56 kbps down; we use the applied
 * network profile, so the throughput figure below is the one CDP enforces.)
 */
const SCENARIOS = {
  'mobile-slow4g-cold': {
    width: 412,
    height: 915,
    dsf: 2.625,
    mobile: true,
    cpu: 4,
    net: { latency: 150, down: (1638.4 * 1000) / 8, up: (675 * 1000) / 8 },
    runs: 3,
    note: 'Lighthouse mobile profile: Slow 4G + 4x CPU. Cold cache, 412x915.',
  },
  'mobile-4g-cold': {
    width: 412,
    height: 915,
    dsf: 2.625,
    mobile: true,
    cpu: 4,
    net: { latency: 70, down: (9000 * 1000) / 8, up: (3000 * 1000) / 8 },
    runs: 3,
    note: 'Good 4G + 4x CPU. Cold cache. The realistic mid-tier Android case.',
  },
  'mobile-slow4g-warm': {
    width: 412,
    height: 915,
    dsf: 2.625,
    mobile: true,
    cpu: 4,
    net: { latency: 150, down: (1638.4 * 1000) / 8, up: (675 * 1000) / 8 },
    runs: 2,
    warm: true,
    note: 'Same profile, second visit with HTTP cache enabled.',
  },
  'desktop-unthrottled': {
    width: 1350,
    height: 940,
    dsf: 1,
    mobile: false,
    cpu: 1,
    net: null,
    runs: 2,
    note: 'No throttling. Desktop Chrome. The optimistic bound.',
  },
}

/**
 * Installed before any page script runs, so no entry is missed. `buffered:
 * true` replays entries that fired during HTML parsing.
 */
function probe() {
  const P = { lcp: [], cls: 0, clsEntries: [], long: [], paint: [], nav: null }
  window.__P = P
  const obs = (type, cb) => {
    try {
      new PerformanceObserver((l) => { for (const e of l.getEntries()) cb(e) })
        .observe({ type, buffered: true })
    } catch (e) {
      /* unsupported type on this Chrome; leave the array empty */
    }
  }
  obs('largest-contentful-paint', (e) =>
    P.lcp.push({
      t: e.startTime,
      size: e.size,
      el: e.element
        ? e.element.tagName +
          (typeof e.element.className === 'string' && e.element.className
            ? '.' + e.element.className.split(' ').slice(0, 3).join('.')
            : '')
        : null,
      url: e.url || null,
    }),
  )
  obs('layout-shift', (e) => {
    if (!e.hadRecentInput) {
      P.cls += e.value
      P.clsEntries.push({ t: Math.round(e.startTime), v: +e.value.toFixed(5) })
    }
  })
  obs('longtask', (e) =>
    P.long.push({
      start: Math.round(e.startTime),
      duration: Math.round(e.duration),
      attribution: e.attribution && e.attribution[0]
        ? {
            name: e.attribution[0].name,
            container: e.attribution[0].containerName || null,
            containerType: e.attribution[0].containerType || null,
          }
        : null,
    }),
  )
  obs('paint', (e) => P.paint.push({ name: e.name, start: Math.round(e.startTime) }))
  obs('navigation', (e) =>
    (P.nav = {
      responseEnd: Math.round(e.responseEnd),
      domInteractive: Math.round(e.domInteractive),
      domContentLoaded: Math.round(e.domContentLoadedEventEnd),
      load: Math.round(e.loadEventEnd),
    }),
  )
  window.__errors = []
  window.addEventListener('error', (e) => window.__errors.push(String(e.message)))
}

/** Vercel-shaped static server: brotli/gzip, immutable hashed assets, no-cache HTML, SPA rewrite. */
function createServer() {
  const log = []
  const server = http.createServer((req, res) => {
    let urlPath = decodeURIComponent(req.url.split('?')[0])
    if (urlPath === '/') urlPath = '/index.html'
    let filePath = path.join(DIST, urlPath)
    if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
      // A missing path WITH an extension is a genuine 404. A missing path
      // WITHOUT one is a client route, which vercel.json rewrites to index.html.
      if (/\.[a-z0-9]+$/i.test(urlPath)) {
        res.writeHead(404)
        res.end('not found')
        return
      }
      filePath = path.join(DIST, 'index.html')
    }
    const raw = fs.readFileSync(filePath)
    const accept = req.headers['accept-encoding'] || ''
    let body = raw
    let encoding = 'identity'
    if (/\bbr\b/.test(accept)) {
      body = zlib.brotliCompressSync(raw)
      encoding = 'br'
    } else if (/\bgzip\b/.test(accept)) {
      body = zlib.gzipSync(raw)
      encoding = 'gzip'
    }
    const hashed = /\/assets\/.*-[A-Za-z0-9_-]{8,}\.(js|css)$/.test(urlPath)
    res.writeHead(200, {
      'Content-Type': MIME[path.extname(filePath)] || 'application/octet-stream',
      'Content-Encoding': encoding,
      'Content-Length': body.length,
      'Cache-Control': hashed
        ? 'public, max-age=31536000, immutable'
        : 'public, max-age=0, must-revalidate',
    })
    res.end(body)
    log.push({ url: urlPath, rawBytes: raw.length, wireBytes: body.length, encoding })
  })
  return { server, log }
}

function summarise(runs) {
  const pick = (k) => runs.map((r) => r[k]).filter((v) => typeof v === 'number')
  const median = (k) => {
    const v = pick(k).sort((a, b) => a - b)
    if (!v.length) return null
    return v.length % 2 ? v[(v.length - 1) / 2] : Math.round((v[v.length / 2 - 1] + v[v.length / 2]) / 2)
  }
  return {
    runs: runs.length,
    fcpMs: median('fcp'),
    lcpMs: median('lcp'),
    cls: +Math.max(...pick('cls')).toFixed(4),
    tbtMs: median('tbt'),
    lastLongTaskEndMs: median('lastLongTaskEnd'),
    loadEventMs: median('load'),
    mainThreadBusyMs: median('busyMs'),
    longTaskCount: median('longTaskCount'),
    longestTaskMs: median('longestTask'),
    requestCount: median('reqs'),
    thirdPartyRequests: median('reqsThirdParty'),
    transferBytes: median('totalTransfer'),
  }
}

async function launchBrowser() {
  const args = ['--no-sandbox', '--disable-dev-shm-usage', '--disable-gpu']
  const candidates = [CHROME, HEADLESS_SHELL].filter((p) => p && fs.existsSync(p))
  const failures = []
  for (const executablePath of candidates) {
    try {
      return await chromium.launch({ executablePath, args, env: BROWSER_ENV })
    } catch (e) {
      failures.push(`${path.basename(executablePath)}: ${String(e.message).split('\n')[0]}`)
    }
  }
  throw new Error(
    'Could not launch a browser. Tried:\n  ' +
      failures.join('\n  ') +
      `\nHint: the browser needs a writable $HOME (currently resolved to ${BROWSER_ENV.HOME}).`,
  )
}

async function main() {
  if (!fs.existsSync(DIST)) {
    throw new Error(`No build at ${DIST}. Run \`bun run build\` in frontend/web first.`)
  }
  fs.mkdirSync(OUT_DIR, { recursive: true })

  const wanted = process.env.PERF_SCENARIOS
    ? process.env.PERF_SCENARIOS.split(',').map((s) => s.trim())
    : Object.keys(SCENARIOS)

  const { server, log } = createServer()
  await new Promise((r) => server.listen(0, '127.0.0.1', r))
  const origin = `http://127.0.0.1:${server.address().port}`

  const browser = await launchBrowser()

  const raw = []
  const report = { label: LABEL, dist: DIST, origin, generatedAt: new Date().toISOString(), scenarios: {} }
  let waterfall = null

  for (const name of wanted) {
    const sc = SCENARIOS[name]
    if (!sc) throw new Error(`Unknown scenario ${name}. Known: ${Object.keys(SCENARIOS).join(', ')}`)
    const runs = []
    // A warm run only measures anything if the SAME context survives across
    // runs: an HTTP cache lives in the browser context, not in the page. The
    // earlier shape created a fresh context per run, so "warm" was really
    // "cold" twice and the scenario was silently meaningless.
    let warmContext = sc.warm
      ? await browser.newContext({
          viewport: { width: sc.width, height: sc.height },
          deviceScaleFactor: sc.dsf,
          isMobile: sc.mobile,
          hasTouch: sc.mobile,
        })
      : null
    for (let i = 0; i < sc.runs; i++) {
      const context =
        warmContext ||
        (await browser.newContext({
          viewport: { width: sc.width, height: sc.height },
          deviceScaleFactor: sc.dsf,
          isMobile: sc.mobile,
          hasTouch: sc.mobile,
          userAgent: sc.mobile
            ? 'Mozilla/5.0 (Linux; Android 13; SM-A536B) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36'
            : undefined,
        }))
      const page = await context.newPage()
      const cdp = await context.newCDPSession(page)
      await cdp.send('Network.enable')
      if (sc.net) {
        await cdp.send('Network.emulateNetworkConditions', {
          offline: false,
          latency: sc.net.latency,
          downloadThroughput: sc.net.down,
          uploadThroughput: sc.net.up,
          connectionType: 'cellular4g',
        })
        await cdp.send('Network.setCacheDisabled', { cacheDisabled: !sc.warm })
      }
      if (sc.cpu > 1) await cdp.send('Emulation.setCPUThrottlingRate', { rate: sc.cpu })
      await page.addInitScript(probe)

      await page.goto(origin + '/', { waitUntil: 'load', timeout: 120000 })
      try {
        await page.waitForLoadState('networkidle', { timeout: 25000 })
      } catch (e) {
        /* a long-polling SignalR connection never goes idle; fall through */
      }
      // Observation window: LCP keeps updating until the page is scrolled or
      // input arrives, and CLS needs the paint to settle.
      await page.waitForTimeout(sc.warm || !sc.net ? 6000 : 10000)

      const data = await page.evaluate(() => ({
        P: window.__P,
        errors: window.__errors || [],
        domNodes: document.getElementsByTagName('*').length,
        resources: performance.getEntriesByType('resource').map((r) => ({
          name: r.name.startsWith(location.origin) ? r.name.slice(location.origin.length) : r.name,
          start: Math.round(r.startTime),
          duration: Math.round(r.duration),
          transferSize: r.transferSize,
          encodedBodySize: r.encodedBodySize,
          decodedBodySize: r.decodedBodySize,
          initiatorType: r.initiatorType,
        })),
      }))

      const P = data.P
      const firstParty = data.resources.filter((r) => !/^https?:/.test(r.name))
      const thirdParty = data.resources.filter((r) => /^https?:/.test(r.name))
      const row = {
        run: i + 1,
        fcp: (P.paint.find((p) => p.name === 'first-contentful-paint') || {}).start ?? null,
        lcp: P.lcp.length ? Math.round(P.lcp[P.lcp.length - 1].t) : null,
        cls: +P.cls.toFixed(4),
        tbt: Math.round(P.long.reduce((a, l) => a + Math.max(0, l.duration - 50), 0)),
        lastLongTaskEnd: P.long.length ? Math.max(...P.long.map((l) => l.start + l.duration)) : 0,
        load: P.nav ? P.nav.load : null,
        dcl: P.nav ? P.nav.domContentLoaded : null,
        busyMs: P.long.reduce((a, l) => a + l.duration, 0),
        longTaskCount: P.long.length,
        longestTask: P.long.length ? Math.max(...P.long.map((l) => l.duration)) : 0,
        reqs: data.resources.length,
        reqsThirdParty: thirdParty.length,
        firstPartyJsBytes: firstParty
          .filter((r) => r.name.endsWith('.js'))
          .reduce((a, r) => a + (r.transferSize || r.encodedBodySize || 0), 0),
        totalTransfer: data.resources.reduce((a, r) => a + (r.transferSize || 0), 0),
        domNodes: data.domNodes,
        lcpElement: P.lcp.length ? P.lcp[P.lcp.length - 1].el : null,
        errors: data.errors.slice(0, 8),
      }
      runs.push(row)

      if (i === 0 && name === 'mobile-slow4g-cold') {
        waterfall = {
          note: 'Cold, throttled first paint of the landing page. Sorted by start time.',
          lcpElement: P.lcp.length ? P.lcp[P.lcp.length - 1] : null,
          longTasks: P.long,
          layoutShifts: P.clsEntries,
          navigation: P.nav,
          resources: data.resources.sort((a, b) => a.start - b.start),
        }
      }
      raw.push({ scenario: name, ...row })
      // Keep the warm context alive across runs so run 2 hits the HTTP cache;
      // only a cold scenario tears its context down each time.
      if (!warmContext) await context.close()
    }
    if (warmContext) await warmContext.close()
    report.scenarios[name] = { ...summarise(runs), note: sc.note, profile: { cpu: sc.cpu, net: sc.net, width: sc.width, height: sc.height, dsf: sc.dsf, warm: !!sc.warm } }
    process.stderr.write(`  ${name}: LCP ${report.scenarios[name].lcpMs}ms  TBT ${report.scenarios[name].tbtMs}ms  CLS ${report.scenarios[name].cls}\n`)
  }

  await browser.close()
  server.close()

  report.waterfall = waterfall
  report.rawRuns = raw
  report.servedFiles = log.reduce((acc, e) => {
    acc[e.url] = { rawBytes: e.rawBytes, wireBytes: e.wireBytes, encoding: e.encoding }
    return acc
  }, {})

  const stamp = new Date().toISOString().replace(/[:.]/g, '-')
  const target = path.join(OUT_DIR, `perf-baseline-${LABEL}-${stamp}.json`)
  fs.writeFileSync(target, JSON.stringify(report, null, 2))
  fs.writeFileSync(path.join(OUT_DIR, 'latest.json'), JSON.stringify(report, null, 2))
  process.stderr.write(`\nreport: ${target}\n`)
}

main().catch((e) => {
  console.error(e)
  process.exit(1)
})
