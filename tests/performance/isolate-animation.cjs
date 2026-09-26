/**
 * Isolation experiment: how much of the landing page's main-thread cost is its
 * animation budget?
 *
 * Why it exists: after route-level code splitting the CDP harness reported the
 * landing page reaching first paint ~4.4 s earlier while *total* main-thread
 * busy over the fixed observation window roughly doubled. That is
 * counter-intuitive until you notice the window's phase shifted: before, most
 * of the window was spent downloading and parsing 2.7 MB of JavaScript; now the
 * app mounts at ~2.4 s and the window is spent running the landing page itself.
 *
 * This measures the same page, same profile, twice — once with
 * `prefers-reduced-motion: no-preference` (the default, where AuroraField
 * animates 6 blurred layers and 14 blossoms per instance x 7 instances) and once
 * with `reduce`, where those animations are switched off by the component's own
 * existing gate. Everything else is identical.
 *
 * Usage (from frontend/web):
 *   HOME=/tmp NODE_PATH=$PWD/node_modules node ../../tests/performance/isolate-animation.cjs
 *
 * The difference is the animation budget. It is a diagnostic, not a benchmark:
 * it answers "is the residual cost the split, or the page?" and not "how fast is
 * the page".
 */

'use strict'

const { spawn } = require('node:child_process')
const path = require('node:path')

const REPO = path.resolve(__dirname, '..', '..')
const PORT = 4341
const ORIGIN = `http://127.0.0.1:${PORT}`
const HOME_DIR = process.env.PERF_HOME || '/tmp'
const RUNS = Number(process.env.PERF_ISOLATE_RUNS || 3)
const OBSERVE_MS = 15_000

const PROFILE = {
  cpu: 4,
  net: { latency: 150, down: (1638.4 * 1000) / 8, up: (675 * 1000) / 8 },
}

function probe() {
  const P = { long: [], paint: [] }
  window.__P = P
  try {
    new PerformanceObserver((l) => {
      for (const e of l.getEntries()) P.long.push({ start: Math.round(e.startTime), duration: Math.round(e.duration) })
    }).observe({ type: 'longtask', buffered: true })
    new PerformanceObserver((l) => {
      for (const e of l.getEntries()) P.paint.push({ name: e.name, start: Math.round(e.startTime) })
    }).observe({ type: 'paint', buffered: true })
  } catch {
    /* unsupported */
  }
}

async function main() {
  const { chromium } = require('playwright')

  const server = spawn(process.execPath, [path.join(__dirname, 'serve.cjs'), String(PORT)], {
    stdio: ['ignore', 'pipe', 'pipe'],
    env: { ...process.env, HOME: HOME_DIR },
  })
  await new Promise((resolve) => {
    server.stdout.on('data', (d) => {
      if (String(d).includes('serving')) resolve()
    })
    setTimeout(resolve, 3000)
  })

  const browser = await chromium.launch({
    headless: true,
    args: ['--no-sandbox', '--disable-gpu'],
    env: { ...process.env, HOME: HOME_DIR },
  })

  const results = {}

  for (const reducedMotion of ['no-preference', 'reduce']) {
    const runs = []
    for (let i = 0; i < RUNS; i++) {
      const context = await browser.newContext({
        viewport: { width: 412, height: 915 },
        deviceScaleFactor: 2.625,
        isMobile: true,
        hasTouch: true,
        reducedMotion,
        userAgent:
          'Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
      })
      const page = await context.newPage()
      const cdp = await context.newCDPSession(page)
      await cdp.send('Network.enable')
      await cdp.send('Network.emulateNetworkConditions', {
        offline: false,
        latency: PROFILE.net.latency,
        downloadThroughput: PROFILE.net.down,
        uploadThroughput: PROFILE.net.up,
      })
      await cdp.send('Emulation.setCPUThrottlingRate', { rate: PROFILE.cpu })
      await page.addInitScript(probe)
      await page.goto(`${ORIGIN}/`, { waitUntil: 'commit', timeout: 60_000 }).catch(() => {})
      await page.waitForTimeout(OBSERVE_MS)

      const data = await page.evaluate(() => window.__P || { long: [], paint: [] })
      const busy = data.long.reduce((sum, t) => sum + Math.max(0, t.duration - 50), 0)
      const mounted = await page.evaluate(() => document.querySelectorAll('#root *').length)
      runs.push({
        longTaskCount: data.long.length,
        mainThreadBusyMs: busy,
        longestTaskMs: data.long.reduce((m, t) => Math.max(m, t.duration), 0),
        fcpMs: data.paint.find((p) => p.name === 'first-contentful-paint')?.start ?? null,
        mountedNodes: mounted,
      })
      await context.close()
    }
    const median = (key) => {
      const v = runs.map((r) => r[key]).filter((n) => typeof n === 'number').sort((a, b) => a - b)
      return v.length ? v[Math.floor(v.length / 2)] : null
    }
    results[reducedMotion] = {
      runs,
      median: {
        longTaskCount: median('longTaskCount'),
        mainThreadBusyMs: median('mainThreadBusyMs'),
        longestTaskMs: median('longestTaskMs'),
        fcpMs: median('fcpMs'),
        mountedNodes: median('mountedNodes'),
      },
    }
    console.log(`\n## prefers-reduced-motion: ${reducedMotion}`)
    for (const run of runs) console.log('   ', JSON.stringify(run))
  }

  await browser.close()
  server.kill()

  const a = results['no-preference'].median
  const b = results['reduce'].median
  console.log('\n## median, animated vs static')
  console.log(`   long tasks      ${a.longTaskCount}  ->  ${b.longTaskCount}`)
  console.log(`   main-thread busy ${a.mainThreadBusyMs} ms  ->  ${b.mainThreadBusyMs} ms`)
  if (a.mainThreadBusyMs) {
    console.log(`   reduction        ${(100 * (1 - b.mainThreadBusyMs / a.mainThreadBusyMs)).toFixed(1)}%`)
  }
  console.log(`   mounted nodes    ${a.mountedNodes}  ->  ${b.mountedNodes}`)

  require('node:fs').writeFileSync(
    path.join(__dirname, 'reports-after', 'isolate-animation.json'),
    JSON.stringify({ profile: PROFILE, observeMs: OBSERVE_MS, results }, null, 2),
  )
  console.log('\nwrote tests/performance/reports-after/isolate-animation.json')
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
