import { expect, test } from '@playwright/test'

/**
 * The signed-out path — the defect slice A1 exists to remove.
 *
 * The delivered console returned `200` for `/admin/kaveesha/dashboard` while signed out, rendered
 * ten sections of fabricated data, and issued six `401`s. A visitor must now reach nothing.
 */

const ADMIN_URL = '/admin/01a0ba0b-485f-780e-b465-ae11670539e9/dashboard'

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
