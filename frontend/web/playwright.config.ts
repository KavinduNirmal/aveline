import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end harness for the two authenticated UI trees.
 *
 * The specs live in the repository-level `tests/e2e/` tree, alongside the other cross-cutting test
 * suites, rather than inside the application package: `admin-console/` for the Aveline console and
 * `tenant-dashboard/` for the boutique dashboard (`/app/b/:slug/:section`).
 *
 * Both walks cover what can be walked without a Clerk session: the **signed-out** path. An
 * unauthenticated visitor must reach nothing and must issue no request to that tree's own API — no
 * `/api/v1/admin/` request for the console, and no `/api/v1/orgs/` request for the tenant dashboard.
 *
 * The authenticated walks (the console's routes and 2-second log freshness; the tenant role matrix
 * and the reduced takings card) need a Clerk test session and a running API; `E2E_BASE_URL` points
 * the suite at a deployed origin when one is available.
 *
 * Run it through the script, not by invoking `playwright test` directly. The script carries two
 * settings this repository needs:
 *
 *     bun run test:e2e:install   # browsers into node_modules/.playwright-browsers (git-ignored)
 *     bun run test:e2e           # NODE_PATH so a spec outside this package can resolve @playwright/test
 *
 * `NODE_PATH` is not a preference: the specs sit above `frontend/web/`, and Node resolves an import
 * from the importing file's directory, so without it `import { test } from '@playwright/test'` in
 * `tests/e2e/**` fails before a single test is collected.
 */
export default defineConfig({
  testDir: '../../tests/e2e',
  fullyParallel: true,
  // One worker, on purpose. Every spec runs against the same vite dev server, and the first load of
  // each route pays Vite's on-demand module graph plus Clerk's own script; with fifteen workers the
  // shared server starves and `toHaveURL(/sign-in/)` times out on a real redirect rather than a real
  // failure. Serial takes ~60 s for the whole tree, which is the right trade for a signed-out walk.
  workers: 1,
  forbidOnly: Boolean(process.env.CI),
  retries: process.env.CI ? 1 : 0,
  reporter: [['list']],
  // Above Playwright's 5 s default: a cold `bun run dev` can take longer than that to hand back the
  // first document, and a liveness bound that fails a correct redirect is worse than a slow run.
  expect: { timeout: 15_000 },
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
