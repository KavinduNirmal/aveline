import { defineConfig, devices } from '@playwright/test'

/**
 * Playwright config for the performance budget gates.
 *
 * It is separate from `frontend/web/playwright.config.ts` on purpose:
 *
 *   - that config targets the **dev server** (`bun run dev`, port 5173) and the
 *     `tests/e2e/**` tree, because it verifies authentication routing. A
 *     performance budget is meaningless against a dev server, whose module
 *     graph is served unbundled on demand and whose bundle does not exist.
 *   - this config targets the **production build** in `frontend/web/dist`,
 *     served with realistic compression and cache headers by `serve.cjs`.
 *   - that config declares exactly one project, `Desktop Chrome`
 *     (`frontend/web/playwright.config.ts:46`) — the repository has no mobile
 *     device emulation anywhere, which is why the mobile block below exists.
 *
 * Run from `frontend/web` so that `@playwright/test` resolves:
 *
 *   cd frontend/web
 *   HOME=/tmp NODE_PATH=$PWD/node_modules \
 *     node_modules/.bin/playwright test -c ../../tests/performance/playwright.perf.config.ts
 *
 * `HOME` must point somewhere writable — Chrome creates its profile there and
 * fails at startup otherwise.
 */

export default defineConfig({
  testDir: __dirname,
  testMatch: ['**/*.spec.ts'],
  fullyParallel: false,
  // One worker: every assertion is a timing- or byte-measurement against the
  // same server, and parallel workers make the timings noisy for no gain.
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  expect: { timeout: 15_000 },
  use: {
    baseURL: 'http://127.0.0.1:4319',
    trace: 'on-first-retry',
  },
  projects: [
    {
      // One project. The budget spec sets its own mobile context via
      // `test.use` inside the describe block that needs it, so a second
      // project would only re-run the same assertions.
      name: 'budgets',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
  webServer: {
    command: 'node serve.cjs 4319',
    url: 'http://127.0.0.1:4319',
    reuseExistingServer: true,
    timeout: 30_000,
  },
})
