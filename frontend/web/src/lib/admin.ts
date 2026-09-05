import { apiClient } from '@/lib/api'

export type AdminRequestStatus = 'Pending' | 'Approved' | 'Rejected'

export interface AdminApprovalRequestSummary {
  id: string
  clerkUserId: string
  email: string
  firstName: string
  lastName: string
  status: AdminRequestStatus
  requestedAt: string
  reviewedAt?: string | null
  reviewedByClerkUserId?: string | null
}

/** Submits an admin access request for the signed-in user. */
export async function submitAdminRequest(): Promise<AdminApprovalRequestSummary> {
  const response = await apiClient.post<AdminApprovalRequestSummary>(
    '/api/v1/admin/requests',
  )
  return response.data
}

/** Lists pending admin access requests (reviewers only). */
export async function listAdminRequests(): Promise<AdminApprovalRequestSummary[]> {
  const response = await apiClient.get<AdminApprovalRequestSummary[]>(
    '/api/v1/admin/requests',
  )
  return response.data
}

/** Approves a request, granting the Clerk admin role. */
export async function approveAdminRequest(id: string): Promise<AdminApprovalRequestSummary> {
  const response = await apiClient.post<AdminApprovalRequestSummary>(
    `/api/v1/admin/requests/${id}/approve`,
  )
  return response.data
}

/** Rejects a pending admin access request. */
export async function rejectAdminRequest(id: string): Promise<AdminApprovalRequestSummary> {
  const response = await apiClient.post<AdminApprovalRequestSummary>(
    `/api/v1/admin/requests/${id}/reject`,
  )
  return response.data
}
