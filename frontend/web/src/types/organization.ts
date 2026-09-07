import type { AccountState } from '@/types/user'

/** Organization created by an owner during onboarding. */
export interface Organization {
  id: string
  name: string
  slug: string
  clerkOrgId: string | null
  ownerUserId: string
  createdAt: string
}

/** A user's canonical membership within an organization. */
export interface OrganizationMembership {
  organizationId: string
  userId: string
  organizationName: string | null
  slug: string | null
  boutiqueRole: string
  status: string
}

/** Plan tier label returned by the backend (mirrors Billing.PlanTier). */
export type OrganizationPlanTier = 'Seed' | 'Bloom' | 'Orchid' | 'Rose' | 'Enterprise'

/** Public boutique profile used to render the tenant dashboard identity/header. */
export interface OrganizationProfileDto {
  id: string
  name: string
  slug: string
  clerkOrgId: string | null
  ownerUserId: string
  address: string | null
  phoneNumber: string | null
  description: string | null
  logoUrl: string | null
  planTier: OrganizationPlanTier
  hasCompletedOnboarding: boolean
  createdAt: string
}

/** A caller's membership within a boutique (from the by-slug lookup). */
export interface OrganizationMembershipView {
  organizationId: string
  organizationName: string
  slug: string
  boutiqueRole: string
  status: string
}

/** A boutique profile paired with the requesting user's membership (null if none). */
export interface OrganizationProfileWithMembershipDto {
  organization: OrganizationProfileDto
  membership: OrganizationMembershipView | null
}

/** Blossom usage for the current billing period (mirrors backend UsageSummary). */
export interface OrganizationUsageSummary {
  organizationId: string
  periodStart: string
  periodEnd: string
  monthlyBlossomLimit: number
  blossomUsed: number
  blossomRemaining: number
  status: string
}

export interface CreateOrganizationRequest {
  name: string
  slug?: string
  clerkOrgId?: string
}

export interface CreateOrganizationResponse {
  organization: Organization
  accountState: AccountState
}

export interface AcceptInvitationRequest {
  code: string
}

export interface AcceptInvitationResponse {
  organizationId: string
  userId: string
  boutiqueRole: string
  clerkOrgId: string | null
  accountState: AccountState
}
