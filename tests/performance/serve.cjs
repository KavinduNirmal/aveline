/**
 * Static server for the performance specs.
 *
 * Mirrors what Vercel does for this project, so a budget measured here is a
 * budget measured against realistic delivery:
 *   - brotli, falling back to gzip
 *   - content-hashed /assets/* get `immutable`; everything else gets
 *     `must-revalidate` (index.html must never be cached long, or a stale
 *     shell points at deleted hashed chunks and the page goes white)
 *   - extension-less paths fall back to index.html, matching the catch-all
 *     rewrite in frontend/web/vercel.json
 *
 * Usage: node serve.cjs [port]   (default 4319; override dist with $PERF_DIST)
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
const PORT = Number(process.argv[2] || 4319)

const MIME = {
  '.html': 'text/html; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.json': 'application/json',
  '.map': 'application/json',
  '.woff2': 'font/woff2',
  '.png': 'image/png',
  '.ico': 'image/x-icon',
}

if (!fs.existsSync(DIST)) {
  console.error(`No build at ${DIST}. Run \`bun run build\` in frontend/web first.`)
  process.exit(1)
}

http
  .createServer((req, res) => {
    let urlPath = decodeURIComponent(req.url.split('?')[0])
    if (urlPath === '/') urlPath = '/index.html'
    let file = path.join(DIST, urlPath)
    if (!fs.existsSync(file) || fs.statSync(file).isDirectory()) {
      if (/\.[a-z0-9]+$/i.test(urlPath)) {
        res.writeHead(404)
        res.end('not found')
        return
      }
      file = path.join(DIST, 'index.html')
    }
    const raw = fs.readFileSync(file)
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
      'Content-Type': MIME[path.extname(file)] || 'application/octet-stream',
      'Content-Encoding': encoding,
      'Content-Length': body.length,
      'Cache-Control': hashed
        ? 'public, max-age=31536000, immutable'
        : 'public, max-age=0, must-revalidate',
    })
    res.end(body)
  })
  .listen(PORT, '127.0.0.1', () => console.log(`serving ${DIST} on http://127.0.0.1:${PORT}`))
