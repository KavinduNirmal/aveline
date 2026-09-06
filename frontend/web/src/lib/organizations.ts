import { apiClient } from '@/lib/api'
import type {
  AcceptInvitationResponse,
  CreateOrganizationRequest,
  CreateOrganizationResponse,
  OrganizationMembership,
  OrganizationProfileWithMembershipDto,
  OrganizationUsageSummary,
} from '@/types/organization'

/**
 * Owner flow: creates the boutique organization and activates the owner account.
 * Requires an authenticated session; see POST /api/v1/orgs.
 */
export async function createOrganization(
  payload: CreateOrganizationRequest,
): Promise<CreateOrganizationResponse> {
  const response = await apiClient.post<CreateOrganizationResponse>(
    '/api/v1/orgs',
    payload,
  )
  return response.data
}

/**
 * Returns the current user's organization memberships. See GET /api/v1/orgs/my.
 */
export async function fetchMyOrganizations(): Promise<OrganizationMembership[]> {
  const response = await apiClient.get<OrganizationMembership[]>(
    '/api/v1/orgs/my',
  )
  return response.data
}

/**
 * Staff flow: joins a boutique by redeeming an invitation code.
 * See POST /api/v1/invitations/accept.
 */
export async function acceptInvitation(
  code: string,
): Promise<AcceptInvitationResponse> {
  const response = await apiClient.post<AcceptInvitationResponse>(
    '/api/v1/invitations/accept',
    { code },
  )
  return response.data
}

/**
 * Resolves a boutique by slug and returns its profile plus the caller's membership
 * (null when the caller is not a member). See GET /api/v1/orgs/by-slug/{slug}.
 */
export async function fetchOrganizationBySlug(
  slug: string,
): Promise<OrganizationProfileWithMembershipDto> {
  const response = await apiClient.get<OrganizationProfileWithMembershipDto>(
    `/api/v1/orgs/by-slug/${encodeURIComponent(slug)}`,
  )
  return response.data
}

/**
 * Returns the current billing period's Blossom usage for a boutique.
 * See GET /api/v1/orgs/{organizationId}/usage.
 */
export async function fetchOrganizationUsage(
  organizationId: string,
): Promise<OrganizationUsageSummary> {
  const response = await apiClient.get<OrganizationUsageSummary>(
    `/api/v1/orgs/${organizationId}/usage`,
  )
  return response.data
}
