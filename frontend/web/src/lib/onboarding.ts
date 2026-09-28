import { apiClient } from '@/lib/api'

export type PlanTier = 'Seed' | 'Bloom' | 'Orchid' | 'Rose' | 'Enterprise'

export interface SaveBoutiqueDetailsRequest {
  name: string
  address: string
  phoneNumber: string
  description?: string
  logoUrl?: string
  slug?: string
}

export interface SelectPlanRequest {
  planTier: PlanTier
}

export interface SaveAiCustomizationRequest {
  brandVoice?: string
  businessRules?: string
  preferredColorsFabrics?: string
  customerPreferences?: string
}

export interface OnboardingOrganizationDto {
  id: string
  name: string
  slug: string
  address: string | null
  phoneNumber: string | null
  description: string | null
  logoUrl: string | null
  planTier: PlanTier
  brandVoice: string | null
  businessRules: string | null
  preferredColorsFabrics: string | null
  customerPreferences: string | null
  onboardingStep: number
  hasCompletedOnboarding: boolean
  /**
   * The list price the server's price book resolved for the tier (LKR), or `null` when no price row
   * was effective. A missing price is not zero: the free Seed plan is `0`, an unpriced paid tier is
   * `null`.
   */
  priceLkr?: number | null
  currency?: string
  /** The subscription lifecycle state, e.g. `Trialing` once a paid tier is selected. */
  subscriptionStatus?: string | null
  /** Always `null` in defer mode: onboarding never creates a payment intent (plan §14 Q1). */
  paymentIntentId?: string | null
  /** Always `null` in defer mode: onboarding never shows a checkout. */
  checkoutUrl?: string | null
}

export interface OnboardingStatusResponse {
  hasCompletedOnboarding: boolean
  currentStep: number
  organization: OnboardingOrganizationDto | null
}

export interface CompleteOnboardingResponse {
  organization: OnboardingOrganizationDto
  userRole: string
  organizationRole: string
  accountState: string
  blossomAllocation: number
  agentWarmedUp: boolean
}

/**
 * Retrieves the current user's onboarding progress and draft organization.
 */
export async function fetchOnboardingStatus(): Promise<OnboardingStatusResponse> {
  const response = await apiClient.get<OnboardingStatusResponse>('/api/v1/onboarding/status')
  return response.data
}

/**
 * Step 3: Saves boutique info and creates the draft organization record.
 */
export async function saveBoutiqueDetails(
  payload: SaveBoutiqueDetailsRequest,
): Promise<OnboardingOrganizationDto> {
  const response = await apiClient.post<OnboardingOrganizationDto>(
    '/api/v1/onboarding/owner',
    payload,
  )
  return response.data
}

/**
 * Step 4: selects the plan tier. The response carries the server-priced subscription the backend
 * provisioned (`priceLkr`, `currency`, `subscriptionStatus`); onboarding defers payment, so
 * `paymentIntentId` and `checkoutUrl` are always `null`.
 */
export async function selectPlan(
  planTier: PlanTier,
): Promise<OnboardingOrganizationDto> {
  const response = await apiClient.post<OnboardingOrganizationDto>(
    '/api/v1/onboarding/plan',
    { planTier },
  )
  return response.data
}

/**
 * Step 5: Sets bespoke AI context based on the chosen tier.
 */
export async function saveAiCustomization(
  payload: SaveAiCustomizationRequest,
): Promise<OnboardingOrganizationDto> {
  const response = await apiClient.post<OnboardingOrganizationDto>(
    '/api/v1/onboarding/customize',
    payload,
  )
  return response.data
}

/**
 * Step 6: Finalizes onboarding, activates the organization, provisions initial Blossoms,
 * and initializes/warms up the AI agents.
 */
export async function completeOnboarding(): Promise<CompleteOnboardingResponse> {
  const response = await apiClient.post<CompleteOnboardingResponse>(
    '/api/v1/onboarding/complete',
  )
  return response.data
}
