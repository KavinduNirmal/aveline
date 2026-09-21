import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchSettings = vi.fn()
const updateSettings = vi.fn()
const fetchApiKeys = vi.fn()
const createApiKey = vi.fn()
const revokeApiKey = vi.fn()
const deleteApiKey = vi.fn()

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/lib/settings-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/settings-api')>('@/lib/settings-api')
  return {
    ...actual,
    fetchSettings: (...a: unknown[]) => fetchSettings(...a),
    updateSettings: (...a: unknown[]) => updateSettings(...a),
    fetchApiKeys: (...a: unknown[]) => fetchApiKeys(...a),
    createApiKey: (...a: unknown[]) => createApiKey(...a),
    revokeApiKey: (...a: unknown[]) => revokeApiKey(...a),
    deleteApiKey: (...a: unknown[]) => deleteApiKey(...a),
  }
})

import { SettingsPanel } from './SettingsPanel'

const ORG = '11111111-1111-1111-1111-111111111111'
const ORGANIZATION = { id: ORG, name: 'Test Boutique', slug: 'test-boutique', planTier: 'Bloom' } as never

describe('SettingsPanel', () => {
  beforeEach(() => {
    fetchSettings.mockReset().mockResolvedValue({
      settings: {
        id: ORG,
        name: 'Test Boutique',
        slug: 'test-boutique',
        address: null,
        phoneNumber: null,
        description: null,
        logoUrl: null,
        brandVoice: null,
        businessRules: null,
        preferredColorsFabrics: null,
        customerPreferences: null,
        billingEmail: null,
        contactEmail: 'hello@boutique.lk',
        currency: 'LKR',
        timeZone: 'Asia/Colombo',
        isActive: true,
        suspendedAt: null,
      },
      entitlements: [
        {
          key: 'blossoms.monthly',
          valueType: 'Number',
          value: 150,
          source: 'plan-default',
          effectiveFrom: '2026-09-01T00:00:00Z',
        },
      ],
    })
    updateSettings.mockReset().mockResolvedValue({})
    fetchApiKeys.mockReset().mockResolvedValue([])
  })

  it('renders the profile, the entitlements and the API keys panel for an owner', async () => {
    render(<SettingsPanel organization={ORGANIZATION} role="org:boutique_owner" />)

    expect(await screen.findByText('Boutique profile')).toBeTruthy()
    expect(screen.getByText('Plan entitlements')).toBeTruthy()
    expect(screen.getByText('API keys')).toBeTruthy()
  })

  it('sends only the fields the operator changed', async () => {
    render(<SettingsPanel organization={ORGANIZATION} role="org:boutique_owner" />)

    const name = await screen.findByLabelText('Boutique name')
    await userEvent.clear(name)
    await userEvent.type(name, 'Renamed Boutique')
    await userEvent.click(screen.getByRole('button', { name: /save changes/i }))

    await waitFor(() =>
      expect(updateSettings).toHaveBeenCalledWith(ORG, { name: 'Renamed Boutique' }),
    )
  })
})
