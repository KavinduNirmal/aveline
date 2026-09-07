import { describe, expect, it } from 'vitest'
import type {
  CompleteOnboardingResponse,
  OnboardingOrganizationDto,
  OnboardingStatusResponse,
  PlanTier,
  SaveAiCustomizationRequest,
  SaveBoutiqueDetailsRequest,
  SelectPlanRequest,
} from './onboarding'

describe('Onboarding Module Contracts', () => {
  it('validates SaveBoutiqueDetailsRequest construction', () => {
    const req: SaveBoutiqueDetailsRequest = {
      name: 'The Grand Atelier',
      address: '42 Galle Road, Colombo 03',
      phoneNumber: '+94 77 123 4567',
      description: 'Luxury bridal and evening wear',
      logoUrl: 'https://example.com/logo.png',
    }

    expect(req.name).toBe('The Grand Atelier')
    expect(req.phoneNumber).toContain('+94')
  })

  it('validates SelectPlanRequest plan tier typing', () => {
    const tiers: PlanTier[] = ['Seed', 'Bloom', 'Orchid', 'Rose', 'Enterprise']
    tiers.forEach((tier) => {
      const planReq: SelectPlanRequest = { planTier: tier }
      expect(planReq.planTier).toBe(tier)
    })
  })

  it('validates SaveAiCustomizationRequest fields', () => {
    const aiReq: SaveAiCustomizationRequest = {
      brandVoice: 'Poised and formal',
      businessRules: 'Max discount 15%',
      preferredColorsFabrics: 'Silk and Cashmere',
      customerPreferences: 'Greet with Ceylon tea',
    }

    expect(aiReq.brandVoice).toBe('Poised and formal')
    expect(aiReq.preferredColorsFabrics).toBe('Silk and Cashmere')
  })

  it('validates OnboardingStatusResponse data mapping', () => {
    const org: OnboardingOrganizationDto = {
      id: 'org-123',
      name: 'House of Fashions',
      slug: 'house-of-fashions',
      address: 'Colombo',
      phoneNumber: '+94 77 000 0000',
      description: null,
      logoUrl: null,
      planTier: 'Bloom',
      brandVoice: 'Warm',
      businessRules: null,
      preferredColorsFabrics: null,
      customerPreferences: null,
      onboardingStep: 4,
      hasCompletedOnboarding: false,
    }

    const status: OnboardingStatusResponse = {
      hasCompletedOnboarding: false,
      currentStep: 4,
      organization: org,
    }

    expect(status.currentStep).toBe(4)
    expect(status.organization?.planTier).toBe('Bloom')
  })

  it('validates CompleteOnboardingResponse payload structure', () => {
    const complete: CompleteOnboardingResponse = {
      organization: {
        id: 'org-456',
        name: 'Atelier Colombo',
        slug: 'atelier-colombo',
        address: 'Colombo 07',
        phoneNumber: '+94 11 111 2222',
        description: null,
        logoUrl: null,
        planTier: 'Bloom',
        brandVoice: 'Sophisticated',
        businessRules: 'Max 10%',
        preferredColorsFabrics: 'Silk',
        customerPreferences: 'VIP care',
        onboardingStep: 6,
        hasCompletedOnboarding: true,
      },
      userRole: 'owner',
      organizationRole: 'org:principal',
      accountState: 'Active',
      blossomAllocation: 750,
      agentWarmedUp: true,
    }

    expect(complete.userRole).toBe('owner')
    expect(complete.organizationRole).toBe('org:principal')
    expect(complete.accountState).toBe('Active')
    expect(complete.blossomAllocation).toBe(750)
    expect(complete.agentWarmedUp).toBe(true)
  })
})
