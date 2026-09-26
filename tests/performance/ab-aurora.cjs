'use strict'
/** Same build, same page: does the CSS transform animation degrade `filter: blur(70px)`? */
const { spawn } = require('node:child_process')
const path = require('node:path')
const fs = require('node:fs')
const PORT = 4361, ORIGIN = `http://127.0.0.1:${PORT}`
const OUT = path.join(__dirname, 'reports-after')

async function main() {
  const { chromium } = require('playwright')
  const server = spawn(process.execPath, [path.join(__dirname, 'serve.cjs'), String(PORT)], {
    stdio: ['ignore','pipe','pipe'], env: { ...process.env, HOME: '/tmp' } })
  await new Promise((r) => { server.stdout.on('data', (d) => String(d).includes('serving') && r()); setTimeout(r, 3000) })
  const browser = await chromium.launch({ headless: true, args: ['--no-sandbox','--disable-gpu'] })
  fs.mkdirSync(OUT, { recursive: true })

  for (const reducedMotion of ['no-preference', 'reduce']) {
    const ctx = await browser.newContext({ viewport: { width: 1200, height: 760 }, reducedMotion })
    const page = await ctx.newPage()
    await page.goto(`${ORIGIN}/`, { waitUntil: 'load', timeout: 60000 })
    await page.locator('h1').first().waitFor({ state: 'visible', timeout: 60000 }).catch(() => {})
    // The alpha-preview dialog dims and blurs the page behind it; dismiss it before capturing.
    await page.getByRole('button', { name: /i understand/i }).click({ timeout: 8000 }).catch(() => {})
    await page.waitForTimeout(2500)
    const blobs = await page.evaluate(() =>
      [...document.querySelectorAll('.blur-\\[70px\\]')].slice(0, 2).map((el) => {
        const cs = getComputedStyle(el)
        return { filter: cs.filter, transform: cs.transform, background: cs.backgroundImage.slice(0, 70), w: cs.width, opacity: cs.opacity, anim: cs.animationName }
      }))
    console.log(`## ${reducedMotion}`)
    for (const b of blobs) console.log('   ', JSON.stringify(b))
    await page.screenshot({ path: path.join(OUT, `aurora-${reducedMotion}.png`) })
    await ctx.close()
  }
  await browser.close(); server.kill()
  console.log('\nwrote reports-after/aurora-{no-preference,reduce}.png')
}
main().catch((e) => { console.error(e); process.exit(1) })
