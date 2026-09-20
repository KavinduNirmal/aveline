import { expect, test } from '@playwright/test'

/**
 * The signed-out path — the defect slice A1 exists to remove.
 *
 * The delivered console returned `200` for `/admin/kaveesha/dashboard` while signed out, rendered
 * ten sections of fabricated data, and issued six `401`s. A visitor must now reach nothing.
 */

const ADMIN_URL = '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/dashboard'

/** The two business-KPI routes added by the Business KPIs plan (P5/P6). */
const BUSINESS_URL = '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/business'
const BUSINESS_USAGE_URL = '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/business/usage'

test('signed out, the console is unreachable and issues no admin request', async ({ page }) => {
  const adminRequests: string[] = []
  page.on('request', (request) => {
    const url = request.url()
    if (url.includes('/api/v1/admin/')) adminRequests.push(url)
  })

  await page.goto(ADMIN_URL)

  // The route tree sits inside ProtectedRoute, so the visitor lands on sign-in.
  await expect(page).toHaveURL(/\/sign-in/)
  await expect(page.getByText('Aveline Console')).toHaveCount(0)

  expect(adminRequests).toEqual([])
})

for (const url of [BUSINESS_URL, BUSINESS_USAGE_URL]) {
  test(`signed out, ${url} is unreachable and issues no business-KPI request`, async ({ page }) => {
    const businessRequests: string[] = []
    page.on('request', (request) => {
      const target = request.url()
      if (target.includes('/api/v1/admin/statistics/business/')) businessRequests.push(target)
    })

    await page.goto(url)

    await expect(page).toHaveURL(/\/sign-in/)
    await expect(page.getByText('Aveline Console')).toHaveCount(0)
    expect(businessRequests).toEqual([])
  })
}

/**
 * The four `money` routes (Revenue Ledger R0–R6).
 *
 * `admin:read`-shaped data is the most sensitive the console holds, so the signed-out guarantee is
 * asserted per route rather than once for the tree: a route added outside `ProtectedRoute` would
 * still be caught here even if the shared assertion passed.
 */
const MONEY_URLS = [
  '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/revenue',
  '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/revenue/ledger',
  '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/revenue/statistics',
  '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/blossoms',
]

for (const url of MONEY_URLS) {
  test(`signed out, ${url} is unreachable and issues no revenue request`, async ({ page }) => {
    const revenueRequests: string[] = []
    page.on('request', (request) => {
      const target = request.url()
      if (
        target.includes('/api/v1/admin/revenue') ||
        target.includes('/api/v1/admin/statistics/revenue') ||
        target.includes('/api/v1/admin/statistics/billing') ||
        target.includes('/blossoms/statement')
      ) {
        revenueRequests.push(target)
      }
    })

    await page.goto(url)

    await expect(page).toHaveURL(/\/sign-in/)
    await expect(page.getByText('Aveline Console')).toHaveCount(0)
    expect(revenueRequests).toEqual([])
  })
}

test('signed out, the bare /admin entry is also refused', async ({ page }) => {
  await page.goto('/admin')
  await expect(page).toHaveURL(/\/sign-in/)
  await expect(page.getByText('Aveline Console')).toHaveCount(0)
})

test('the sign-in page the visitor lands on does not overflow at any supported width', async ({
  page,
}) => {
  // The six widths A3's structural test pins in jsdom (where layout cannot be measured) are
  // measured here, in a real browser, on the one page a signed-out visitor can reach.
  for (const width of [1920, 1440, 1280, 1024, 768, 390]) {
    await page.setViewportSize({ width, height: 900 })
    await page.goto('/sign-in')
    const overflow = await page.evaluate(() => {
      const root = document.documentElement
      return root.scrollWidth - root.clientWidth
    })
    expect(overflow, `horizontal overflow at ${width}px`).toBeLessThanOrEqual(1)
  }
})
