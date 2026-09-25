import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, describe, expect, it, vi } from 'vitest'

import type { OnboardingOrganizationDto } from '@/lib/onboarding'

import { PlanSelectionStep } from './PlanSelectionStep'
import { OwnerOnboardingWizardProvider } from '../wizard-context'

/**
 * Plan §9.1 F1 acceptance (4): the plan step renders the **server's** price once the backend has
 * priced the tier. The hardcoded `PLANS` constant survives only as the not-yet-priced copy, and
 * defer mode means the demo banner and the null payment fields stay put.
 */

const { fetchOnboardingStatus, selectPlan } = vi.hoisted(() => ({
  fetchOnboardingStatus: vi.fn(),
  selectPlan: vi.fn(),
}))

vi.mock('@/lib/onboarding', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/onboarding')>()
  return { ...actual, fetchOnboardingStatus, selectPlan }
})

vi.mock('@/contexts/UserContext', () => ({
  useUserContext: () => ({ refreshUser: vi.fn() }),
}))

function pricedOrg(priceLkr: number | null): OnboardingOrganizationDto {
  return {
    id: 'org-1',
    name: 'The Silk Pavilion',
    slug: 'the-silk-pavilion',
    address: '123 Galle Road, Colombo',
    phoneNumber: '+94 77 123 4567',
    description: null,
    logoUrl: null,
    planTier: 'Bloom',
    brandVoice: null,
    businessRules: null,
    preferredColorsFabrics: null,
    customerPreferences: null,
    onboardingStep: 4,
    hasCompletedOnboarding: false,
    priceLkr,
    currency: 'LKR',
    subscriptionStatus: 'Trialing',
    paymentIntentId: null,
    checkoutUrl: null,
  }
}

function renderStep() {
  return render(
    <MemoryRouter>
      <OwnerOnboardingWizardProvider>
        <PlanSelectionStep />
      </OwnerOnboardingWizardProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  vi.clearAllMocks()
})

describe('PlanSelectionStep', () => {
  it('renders the server price returned for the selected tier', async () => {
    fetchOnboardingStatus.mockRejectedValue(new Error('fresh session'))
    selectPlan.mockResolvedValue(pricedOrg(2900))

    renderStep()

    // Before the post the card carries the marketing copy.
    expect(await screen.findByText('LKR 3,500/mo')).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /continue to ai customization/i }))

    // The server said 2,900; the step must render that value, not a second opinion.
    expect(await screen.findByText('LKR 2,900/mo')).toBeInTheDocument()
    expect(screen.queryByText('LKR 3,500/mo')).not.toBeInTheDocument()
  })

  it('falls back to the collection copy when the server reports no price', async () => {
    fetchOnboardingStatus.mockRejectedValue(new Error('fresh session'))
    selectPlan.mockResolvedValue(pricedOrg(null))

    renderStep()
    await screen.findByText('LKR 3,500/mo')

    await userEvent.click(screen.getByRole('button', { name: /continue to ai customization/i }))

    expect(await screen.findByText('LKR 3,500/mo')).toBeInTheDocument()
  })

  it('keeps the demo banner and shows no checkout, because defer mode collects nothing', async () => {
    fetchOnboardingStatus.mockRejectedValue(new Error('fresh session'))
    selectPlan.mockResolvedValue(pricedOrg(3500))

    renderStep()

    expect(await screen.findByText(/demo mode active/i)).toBeInTheDocument()
    expect(screen.getByText(/no payment or credit card is collected today/i)).toBeInTheDocument()
    expect(screen.queryByText(/checkout/i)).not.toBeInTheDocument()
  })
})
