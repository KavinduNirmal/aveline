import { expect, test } from '@playwright/test'

/**
 * The tenant dashboard's signed-out path — slice T7.
 *
 * The plan's own framing is that the tenant surface was "measured by nothing": it had no coverage
 * gate, no conformance gate and no end-to-end walk. The coverage and conformance gates landed in
 * T0b; this spec is the end-to-end half.
 *
 * It mirrors `tests/e2e/admin-console/console-access.spec.ts`: the one walk buildable **without** a
 * Clerk test session, and the one that guards the defect the whole slice exists to remove — a
 * signed-out visitor reaching a dashboard and causing it to fetch tenant data. Every route under
 * `/app/b/:slug` sits inside `ProtectedRoute` (`frontend/web/src/App.tsx`), so a visitor must land
 * on `/sign-in`, see no dashboard chrome, and issue **zero** `/api/v1/orgs/` requests.
 *
 * The authenticated walk (the four-role matrix, the reduced takings card) needs a Clerk test
 * session and a running API. `E2E_BASE_URL` points the suite at a deployed origin when one exists;
 * `docs/frontend/tenant-dashboard.md` records that the authenticated walk is not delivered and
 * states which tests pin the role matrix instead.
 */

/** A slug-shaped value. The route is protected, so the slug is never resolved while signed out. */
const TENANT_SLUG = 'aveline-demo'

/** The two sections that carry the tenant money surface, plus the shell's landing section. */
const TENANT_URLS = [
  `/app/b/${TENANT_SLUG}`,
  `/app/b/${TENANT_SLUG}/overview`,
  `/app/b/${TENANT_SLUG}/income`,
  `/app/b/${TENANT_SLUG}/billing`,
]

/**
 * The dashboard chrome a signed-out visitor must never see.
 *
 * `Switch boutique` and `Reporting window` are the shell's two stable aria-labels, and `Top up` is
 * its only primary action. Asserting on the chrome rather than on a heading is deliberate: the
 * shell's own labels are what a regression would render, and a heading can be added without the
 * shell mounting.
 */
async function expectNoDashboardChrome(page: import('@playwright/test').Page) {
  await expect(page.locator('[aria-label="Switch boutique"]')).toHaveCount(0)
  await expect(page.locator('[aria-label="Reporting window"]')).toHaveCount(0)
  await expect(page.getByRole('button', { name: 'Top up' })).toHaveCount(0)
}

for (const url of TENANT_URLS) {
  test(`signed out, ${url} is unreachable and issues no organisation request`, async ({ page }) => {
    const orgRequests: string[] = []
    page.on('request', (request) => {
      const target = request.url()
      if (target.includes('/api/v1/orgs/')) orgRequests.push(target)
    })

    await page.goto(url)

    // The route tree sits inside ProtectedRoute, so the visitor lands on sign-in.
    await expect(page).toHaveURL(/\/sign-in/)
    await expectNoDashboardChrome(page)

    // The whole point of the slice: no tenant data is requested at all, signed out.
    expect(orgRequests).toEqual([])
  })
}

test('signed out, the bare /app entry (the org switcher) is also refused', async ({ page }) => {
  const orgRequests: string[] = []
  page.on('request', (request) => {
    const target = request.url()
    if (target.includes('/api/v1/orgs/')) orgRequests.push(target)
  })

  await page.goto('/app')

  await expect(page).toHaveURL(/\/sign-in/)
  await expectNoDashboardChrome(page)
  expect(orgRequests).toEqual([])
})

test('signed out, the tenant routes never render the retired "Demo mode" claim', async ({ page }) => {
  // "Demo mode" was a claim about the product, not a state of the data; T4 deleted it. This walk
  // keeps it deleted on the one page a signed-out visitor can reach.
  await page.goto(`/app/b/${TENANT_SLUG}/overview`)
  await expect(page).toHaveURL(/\/sign-in/)
  await expect(page.getByText('Demo mode')).toHaveCount(0)
})
