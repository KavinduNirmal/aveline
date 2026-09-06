import { apiClient } from '@/lib/api'
import type {
  CreateInvitationRequest,
  CreateInvitationResponse,
  PendingInvitationDto,
} from '@/types/invitation'

const invitationsBase = (organizationId: string) => `/api/v1/orgs/${organizationId}/invitations`

/** Creates a staff invitation and returns the one-time code plus a shareable link. */
export async function createInvitation(
  organizationId: string,
  payload: CreateInvitationRequest,
): Promise<CreateInvitationResponse> {
  const response = await apiClient.post<CreateInvitationResponse>(
    invitationsBase(organizationId),
    payload,
  )
  return response.data
}

/** Lists pending invitations for the organization. */
export async function listPendingInvitations(
  organizationId: string,
): Promise<PendingInvitationDto[]> {
  const response = await apiClient.get<PendingInvitationDto[]>(invitationsBase(organizationId))
  return response.data
}

/** Revokes a pending invitation. */
export async function revokeInvitation(
  organizationId: string,
  invitationId: string,
): Promise<void> {
  await apiClient.post(`${invitationsBase(organizationId)}/${invitationId}/revoke`)
}
