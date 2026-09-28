import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const listIntegrationsMock = vi.hoisted(() => vi.fn())
const listIntegrationMessagesMock = vi.hoisted(() => vi.fn())
const saveIntegrationMock = vi.hoisted(() => vi.fn())
const testIntegrationMock = vi.hoisted(() => vi.fn())
const deleteIntegrationMock = vi.hoisted(() => vi.fn())
const writeClipboardMock = vi.hoisted(() => vi.fn())

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/lib/clipboard', () => ({
  writeClipboard: (...args: unknown[]) => writeClipboardMock(...args),
}))

vi.mock('@/lib/integrations', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/integrations')>()
  return {
    ...actual,
    listIntegrations: (...args: unknown[]) => listIntegrationsMock(...args),
    listIntegrationMessages: (...args: unknown[]) => listIntegrationMessagesMock(...args),
    saveIntegration: (...args: unknown[]) => saveIntegrationMock(...args),
    testIntegration: (...args: unknown[]) => testIntegrationMock(...args),
    deleteIntegration: (...args: unknown[]) => deleteIntegrationMock(...args),
  }
})

import { apiBaseUrl } from '@/lib/env'
import type { OrganizationProfileDto } from '@/types/organization'
import { IntegrationsPanel } from './IntegrationsPanel'

const ORG = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Aveline Colombo 07',
  slug: 'aveline-colombo-07',
  clerkOrgId: null,
  ownerUserId: 'u-1',
  address: null,
  phoneNumber: null,
  description: null,
  logoUrl: null,
  planTier: 'Bloom',
} as never as OrganizationProfileDto

const WEBHOOK_URL = `${apiBaseUrl}/api/v1/webhooks/whatsapp/${ORG.id}`

function whatsappCard(): HTMLElement {
  const card = screen.getByText('WhatsApp Business').closest('[data-slot="card"]')
  if (!(card instanceof HTMLElement)) {
    throw new Error('WhatsApp card not found')
  }
  return card
}

/** Opens the WhatsApp credentials dialog, which is where the webhook is set up. */
async function openWhatsAppDialog() {
  const user = userEvent.setup()
  render(
    <MemoryRouter>
      <IntegrationsPanel organization={ORG} />
    </MemoryRouter>,
  )
  await user.click(within(whatsappCard()).getByRole('button', { name: /connect/i }))
  return user
}

beforeEach(() => {
  vi.clearAllMocks()
  listIntegrationsMock.mockResolvedValue([])
  listIntegrationMessagesMock.mockResolvedValue([])
})

describe('IntegrationsPanel webhook setup', () => {
  it('offers the URL Meta has to call for this boutique', async () => {
    await openWhatsAppDialog()

    expect(await screen.findByLabelText('Webhook URL')).toHaveValue(WEBHOOK_URL)
  })

  it('copies the webhook URL verbatim', async () => {
    const user = await openWhatsAppDialog()

    await user.click(screen.getByRole('button', { name: /copy webhook url/i }))

    await waitFor(() => expect(writeClipboardMock).toHaveBeenCalledWith(WEBHOOK_URL))
  })

  it('suggests a URL-safe verify token instead of leaving it to be invented', async () => {
    const user = await openWhatsAppDialog()
    const token = screen.getByLabelText('Webhook Verify Token') as HTMLInputElement
    expect(token.value).toBe('')

    await user.click(screen.getByRole('button', { name: /generate/i }))

    // 24 random bytes, base64url: unpadded, and safe to paste into a form or a query string.
    expect(token.value).toMatch(/^[A-Za-z0-9_-]{32}$/)
  })

  it('lets the generated token be copied for Meta', async () => {
    const user = await openWhatsAppDialog()

    await user.click(screen.getByRole('button', { name: /generate/i }))
    const token = (screen.getByLabelText('Webhook Verify Token') as HTMLInputElement).value
    await user.click(screen.getByRole('button', { name: /copy webhook verify token/i }))

    await waitFor(() => expect(writeClipboardMock).toHaveBeenCalledWith(token))
  })

  it('keeps the verify token out of the other credential fields', async () => {
    await openWhatsAppDialog()

    // Every other WhatsApp credential stays masked; only the token has to be readable to copy.
    expect(screen.getByLabelText('Access Token')).toHaveAttribute('type', 'password')
    expect(screen.getByLabelText('App Secret')).toHaveAttribute('type', 'password')
    expect(screen.getByLabelText('Webhook Verify Token')).toHaveAttribute('type', 'text')
  })
})
