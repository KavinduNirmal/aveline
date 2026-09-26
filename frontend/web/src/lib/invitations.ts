import { apiClient } from '@/lib/api'
import type {
  BulkCreateInvitationRequest,
  BulkCreateInvitationResponse,
  CreateInvitationRequest,
  CreateInvitationResponse,
  PendingInvitationDto,
} from '@/types/invitation'

const invitationsBase = (organizationId: string) => `/api/v1/orgs/${organizationId}/invitations`

/**
 * A fresh idempotency key for one creation attempt. Both invitation routes require the header
 * (T6), because a retry that mints a second staff code is a real cost. A new key per attempt is
 * what lets a genuinely repeated invite succeed while a double-click replays the first response.
 */
function newIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `invite-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

/** Creates a staff invitation and returns the one-time code plus a shareable link. */
export async function createInvitation(
  organizationId: string,
  payload: CreateInvitationRequest,
): Promise<CreateInvitationResponse> {
  const response = await apiClient.post<CreateInvitationResponse>(
    invitationsBase(organizationId),
    payload,
    { headers: { 'Idempotency-Key': newIdempotencyKey() } },
  )
  return response.data
}

/**
 * Creates multiple staff invitations in one call (E-10). The response carries the requested and
 * created counts side by side, so a server-side clamp is visible rather than silent.
 */
export async function createBulkInvitations(
  organizationId: string,
  payload: BulkCreateInvitationRequest,
): Promise<BulkCreateInvitationResponse> {
  const response = await apiClient.post<BulkCreateInvitationResponse>(
    `${invitationsBase(organizationId)}/bulk`,
    payload,
    { headers: { 'Idempotency-Key': newIdempotencyKey() } },
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

