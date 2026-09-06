import { apiClient } from '@/lib/api'
import type {
  AcceptInvitationResponse,
  CreateOrganizationRequest,
  CreateOrganizationResponse,
  OrganizationMembership,
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
