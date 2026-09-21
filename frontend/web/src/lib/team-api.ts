import { apiClient } from '@/lib/api'

/**
 * The tenant team surface: the Members directory (docs/api/README.md C.5).
 *
 * The invitation routes live in `lib/invitations.ts`; this module owns the member lifecycle, which
 * is a different thing: an invitation mints a code, a membership is a person who already holds one.
 *
 * Every path is org-scoped, and the write verbs take `team:manage` on the server — a manager may
 * manage staff without reaching Integrations (TD5.5).
 */

export interface OrganizationMember {
  userId: string
  email: string
  firstName: string
  lastName: string
  displayName: string | null
  profileImageUrl: string | null
  boutiqueRole: string
  status: string
  joinedAt: string
}

export interface PagedOrganizationMembers {
  items: OrganizationMember[]
  page: number
  pageSize: number
  total: number
}

export interface MemberFilters {
  status?: string
  role?: string
  q?: string
  page?: number
  pageSize?: number
}

const membersBase = (organizationId: string) => `/api/v1/orgs/${organizationId}/members`

export async function fetchMembers(
  organizationId: string,
  filters: MemberFilters = {},
  signal?: AbortSignal,
): Promise<PagedOrganizationMembers> {
  const response = await apiClient.get<PagedOrganizationMembers>(membersBase(organizationId), {
    params: {
      status: filters.status || undefined,
      role: filters.role || undefined,
      q: filters.q || undefined,
      page: filters.page ?? 1,
      pageSize: filters.pageSize ?? 20,
    },
    signal,
  })
  return response.data
}

export async function changeMemberRole(
  organizationId: string,
  userId: string,
  boutiqueRole: string,
  signal?: AbortSignal,
): Promise<OrganizationMember> {
  const response = await apiClient.patch<OrganizationMember>(
    `${membersBase(organizationId)}/${userId}`,
    { boutiqueRole },
    { signal },
  )
  return response.data
}

export async function suspendMember(
  organizationId: string,
  userId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.post(`${membersBase(organizationId)}/${userId}/suspend`, undefined, { signal })
}

export async function activateMember(
  organizationId: string,
  userId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.post(`${membersBase(organizationId)}/${userId}/activate`, undefined, { signal })
}

export async function removeMember(
  organizationId: string,
  userId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.delete(`${membersBase(organizationId)}/${userId}`, { signal })
}

/**
 * Why the caller may not act on a member row, or `null` when they may.
 *
 * The server enforces both rules — a self role change is `409`, and an owner membership cannot be
 * suspended or removed — but a UI that offers an action it knows will fail is a UI that teaches the
 * operator to distrust the buttons. Returning the **reason** rather than a bare boolean is what lets
 * the row say *why* it is disabled instead of leaving a dead control unexplained.
 */
export function memberActionBlockedReason(
  member: OrganizationMember,
  currentUserId: string,
): string | null {
  if (member.userId === currentUserId) {
    return 'You cannot change your own role or remove yourself.'
  }

  if (member.boutiqueRole === 'org:boutique_owner') {
    return 'An owner membership cannot be suspended or removed.'
  }

  return null
}
