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
  boutiqueRole: string
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
