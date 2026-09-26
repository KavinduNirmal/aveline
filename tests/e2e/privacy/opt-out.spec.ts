import { expect, test } from '@playwright/test'

/**
 * The customer-facing opt-out walk, in a real browser (privacy plan §12.3, Phase 7 item 7.5).
 *
 * `/privacy/opt-out` is the only surface in the product a customer - who has no Aveline account -
 * uses directly, and it carries a legal obligation, so it earns a rendered-flow test rather than
 * only a component test. This spec drives the shipped page: it parses the signed link, collects the
 * number and the scope, requests a code, verifies it, and lands on the revoked state.
 *
 * ## What is real and what is intercepted
 *
 * The page, the router and the whole state machine are real. The two anonymous API calls are
 * intercepted with `page.route`, for two reasons:
 *
 * 1. a code is delivered over **WhatsApp**, so a hermetic run cannot read it; and
 * 2. the point of this spec is the browser flow, not the OTP service - that service has its own
 *    integration tests (`PrivacyOptOutStartTests`, `PrivacyOptOutVerifyTests`).
 *
 * The stubbed bodies are the shipped contract: `start` answers the constant accepted body **with the
 * handle it minted**, and `verify` answers `revoked` or the single `otp-invalid` refusal.
 *
 * ## How to run it
 *
 * ```bash
 * cd frontend/web
 * bun run test:e2e:install   # once, chromium into node_modules/.playwright-browsers
 * bun run test:e2e -- tests/e2e/privacy/opt-out.spec.ts
 * ```
 *
 * The suite starts `bun run dev` itself (`playwright.config.ts`), so no API is required.
 */

const ORG = '9f1c1111-1111-4111-8111-111111111111'
const SIGNED_LINK = `/privacy/opt-out?o=${ORG}&v=1&s=base64url-hmac`

const START_RESPONSE = {
  status: 'accepted',
  handle: 'opaque-handle-e2e',
  expiresInSeconds: 300,
}

const REVOKED_RESPONSE = {
  status: 'revoked',
  scope: 'org',
  effectiveAtUtc: '2026-09-25T10:15:00Z',
}

test.describe('the /privacy/opt-out customer walk', () => {
  test('refuses a page opened without the signed link', async ({ page }) => {
    await page.goto('/privacy/opt-out')

    await expect(
      page.getByRole('heading', { name: /link is not valid/i }),
    ).toBeVisible()
    await expect(page.getByLabel(/whatsapp number/i)).toHaveCount(0)
  })

  test('starts the opt-out from the signed link and lands on the revoked state', async ({ page }) => {
    let startBody: Record<string, unknown> | null = null
    let verifyBody: Record<string, unknown> | null = null

    await page.route('**/api/v1/privacy/opt-out/start', async (route) => {
      startBody = route.request().postDataJSON() as Record<string, unknown>
      await route.fulfill({ status: 202, json: START_RESPONSE })
    })
    await page.route('**/api/v1/privacy/opt-out/verify', async (route) => {
      verifyBody = route.request().postDataJSON() as Record<string, unknown>
      await route.fulfill({ status: 200, json: REVOKED_RESPONSE })
    })

    await page.goto(SIGNED_LINK)

    await page.getByLabel(/whatsapp number/i).fill('0771234567')
    await page.getByRole('radio', { name: /this boutique only/i }).click()
    await page.getByRole('button', { name: /send me a code/i }).click()

    // Anti-enumeration in the UI: one neutral sentence whether or not the number is known.
    await expect(page.getByText(/if that number is registered/i)).toBeVisible()
    await expect(page.getByText(/expires in 5 minutes/i)).toBeVisible()

    await page.getByLabel(/six-digit code/i).fill('123456')
    await page.getByRole('button', { name: /confirm opt-out/i }).click()

    await expect(page.getByRole('heading', { name: /you have opted out/i })).toBeVisible()
    await expect(page.getByText(/this boutique will not process/i)).toBeVisible()

    expect(startBody).toMatchObject({
      organizationId: ORG,
      phoneNumber: '0771234567',
      scope: 'org',
      version: '1',
      signature: 'base64url-hmac',
    })
    expect(verifyBody).toMatchObject({
      organizationId: ORG,
      handle: 'opaque-handle-e2e',
      otp: '123456',
      scope: 'org',
    })
  })

  test('keeps the customer on the code step for an invalid or expired code', async ({ page }) => {
    await page.route('**/api/v1/privacy/opt-out/start', (route) =>
      route.fulfill({ status: 202, json: START_RESPONSE }),
    )
    await page.route('**/api/v1/privacy/opt-out/verify', (route) =>
      route.fulfill({
        status: 400,
        json: { code: 'otp-invalid', message: 'The code is invalid, expired or was already used.' },
      }),
    )

    await page.goto(SIGNED_LINK)
    await page.getByLabel(/whatsapp number/i).fill('0771234567')
    await page.getByRole('button', { name: /send me a code/i }).click()

    await page.getByLabel(/six-digit code/i).fill('000000')
    await page.getByRole('button', { name: /confirm opt-out/i }).click()

    await expect(page.getByRole('alert')).toContainText(
      /not valid, has expired, or was already used/i,
    )
    await expect(page.getByLabel(/six-digit code/i)).toBeVisible()
    await expect(page.getByRole('heading', { name: /you have opted out/i })).toHaveCount(0)
  })

  test('the global scope reaches every boutique in the confirmation', async ({ page }) => {
    await page.route('**/api/v1/privacy/opt-out/start', (route) =>
      route.fulfill({ status: 202, json: START_RESPONSE }),
    )
    await page.route('**/api/v1/privacy/opt-out/verify', (route) =>
      route.fulfill({ status: 200, json: { ...REVOKED_RESPONSE, scope: 'all' } }),
    )

    await page.goto(SIGNED_LINK)
    await page.getByLabel(/whatsapp number/i).fill('0771234567')
    await page.getByRole('radio', { name: /every aveline boutique/i }).click()
    await page.getByRole('button', { name: /send me a code/i }).click()

    await page.getByLabel(/six-digit code/i).fill('123456')
    await page.getByRole('button', { name: /confirm opt-out/i }).click()

    await expect(
      page.getByText(/every aveline boutique holding your number/i),
    ).toBeVisible()
  })
})
