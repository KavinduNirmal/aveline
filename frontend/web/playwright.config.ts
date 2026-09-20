import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end harness for the administrator console.
 *
 * The specs live in the repository-level `tests/e2e/admin-console/` tree, alongside the other
 * cross-cutting test suites, rather than inside the application package.
 *
 * It walks what can be walked without a Clerk session: the **signed-out** path, which is the path
 * the whole overhaul exists to fix. An unauthenticated visitor must reach nothing and must issue no
 * admin request.
 *
 * The authenticated walk (every route, the operator flows, the 2-second log freshness) needs a
 * Clerk test session and a running API; `E2E_BASE_URL` points the suite at a deployed origin when
 * one is available.
 *
 * Browsers are installed into `node_modules/.playwright-browsers` (already git-ignored) so the
 * download does not pollute the repository:
 *
 *     PLAYWRIGHT_BROWSERS_PATH=$PWD/node_modules/.playwright-browsers bunx playwright install chromium
 */
export default defineConfig({
  testDir: '../../tests/e2e',
  fullyParallel: true,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:5173',
    trace: 'on-first-retry',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: process.env.E2E_BASE_URL
    ? undefined
    : {
        command: 'bun run dev',
        url: 'http://localhost:5173',
        reuseExistingServer: true,
        timeout: 60_000,
      },
})
