import { expect, test } from '@playwright/test'

/**
 * The tenant top-up walk, end to end, against the **mock** payment provider.
 *
 * This spec buys a real pack through the real stack: the dashboard's own Top up dialog, the real
 * `POST …/blossoms/top-ups/checkout`, the mock provider's Development-only hosted page, its signed
 * webhook, and the settlement that grants the Blossoms. Nothing is stubbed: the terminal state
 * asserted here is the one `GET …/payment-intents/{id}` returned.
 *
 * **It runs against the mock provider only.** The mock is deterministic and offline (no card
 * network, no provider account), which is what makes the walk reproducible; `Payments:Provider`
 * must therefore be `mock` and the API must be in Development.
 *
 * ## Why it is skipped by default
 *
 * The tenant dashboard is reached through a Clerk session, and this repository has no Clerk test
 * session in CI (`@clerk/testing` is not a dependency). Rather than ship a spec that fails for the
 * wrong reason, the walk is skipped until a signed-in tenant storage state is supplied. Everything
 * it needs is below.
 *
 * ## How to run it
 *
 * ```bash
 * # 1. API, Development, mock provider
 * ASPNETCORE_ENVIRONMENT=Development \
 * Payments__Provider=mock \
 * Payments__Mock__Enabled=true \
 * Payments__Mock__WebhookSigningSecret=whsec_e2e_local_secret \
 * dotnet run --project Aveline.Api
 *
 * # 2. seed a pack (needs a team-admin token)
 * AVELINE_ADMIN_TOKEN=<jwt> scripts/seed-price-book.sh
 *
 * # 3. web dev server
 * cd frontend/web && bun run dev
 *
 * # 4. a signed-in boutique-owner storage state (sign in once by hand and export it, or mint one
 * #    with Clerk's test helpers), then:
 * cd frontend/web
 * E2E_TENANT_STORAGE_STATE=/absolute/path/tenant-owner.json \
 * E2E_TENANT_SLUG=my-boutique \
 * E2E_TENANT_PACK=blossom_pack_500 \
 * bun run test:e2e -- tests/e2e/payments/top-up.spec.ts
 * ```
 */

const storageState = process.env.E2E_TENANT_STORAGE_STATE
const slug = process.env.E2E_TENANT_SLUG
const packLabel = process.env.E2E_TENANT_PACK_LABEL ?? '500 Blossoms'

test.describe('tenant top-up through the mock provider', () => {
  test.skip(
    !storageState || !slug,
    'needs E2E_TENANT_STORAGE_STATE (a signed-in boutique-owner session) and E2E_TENANT_SLUG; '
      + 'see the header of this file for the full command.',
  )

  test.use({ storageState })

  test('buys a pack, settles it at the mock, and the balance rises', async ({ page }) => {
    await page.goto(`/app/b/${slug}/overview`)

    // The chip carries the balance the server measured; the delta below is asserted against it.
    const chip = page.getByText(/Blossoms$/).first()
    await expect(chip).toBeVisible()
    const before = await readBalance(chip)

    await page.getByRole('button', { name: 'Top up' }).click()

    const dialog = page.getByRole('dialog')
    await expect(dialog).toBeVisible()
    // The packs are the server's own price book, so a rendered pack is a purchasable one.
    await dialog.getByRole('button', { name: new RegExp(packLabel, 'i') }).click()
    await dialog.getByRole('button', { name: /continue to payment/i }).click()

    // The hosted page opens in a new tab. Its forms post a documented test credential to the
    // settle endpoint, which applies it to the charge and delivers the mock's signed webhook.
    const popup = await page.waitForEvent('popup')
    await popup.waitForLoadState()
    await popup.getByRole('button', { name: 'tok_aveline_succeed' }).click()
    await popup.close()

    // The redirect is not the proof: the dialog polls the intent and renders the server's state.
    await expect(dialog.getByText('Top-up complete')).toBeVisible({ timeout: 30_000 })

    await dialog.getByRole('button', { name: /buy another pack/i }).click()
    await page.keyboard.press('Escape')

    await expect
      .poll(async () => readBalance(page.getByText(/Blossoms$/).first()), { timeout: 30_000 })
      .toBeGreaterThan(before)
  })
})

async function readBalance(chip: import('@playwright/test').Locator): Promise<number> {
  const text = (await chip.textContent()) ?? ''
  const digits = text.replace(/[^0-9.]/g, '')
  return Number.parseFloat(digits)
}
