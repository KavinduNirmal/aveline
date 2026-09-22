import { apiClient } from '@/lib/api'

/**
 * The tenant approvals surface (docs/api/README.md B.19): the queue and the four decision verbs.
 *
 * Q14 lives in this module's `availableDecisions`. `approvals:approve` is held by staff after Q8,
 * but `reject` **cancels** the order and `revise` **rewrites** its discount, total and margin, so
 * the two verbs need `orders:manage` as well. The server enforces that by verb (on `/decision`'s
 * body as well as on the route); the client asks before rendering a button, because a hidden verb is
 * honest and a button that answers `403` is not.
 */

export interface ApprovalOrder {
  id: string
  organizationId: string
  customerId: string
  customerName: string
  orderType: string
  status: string
  subtotal: number
  discount: number
  total: number
  totalCost: number
  margin: number
  createdAt: string
  updatedAt: string | null
}

export interface ApprovalQueueEntry {
  id: string
  organizationId: string
  orderId: string
  approvalType: string
  status: string
  thresholdExceeded: boolean
  reason: string
  decisionComment: string | null
  decidedBy: string | null
  threadId: string | null
  conversationId: string | null
  createdAt: string
  decidedAt: string | null
  order: ApprovalOrder | null
}

export interface PagedApprovals {
  items: ApprovalQueueEntry[]
  page: number
  pageSize: number
  total: number
}

export type ApprovalDecision = 'approve' | 'reject' | 'revise'

export interface DecisionPayload {
  reason?: string
  revisedDiscount?: number
}

const approvalsBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/approvals`

export async function fetchApprovals(
  organizationId: string,
  filters: { status?: string; page?: number; pageSize?: number } = {},
  signal?: AbortSignal,
): Promise<PagedApprovals> {
  const response = await apiClient.get<PagedApprovals>(approvalsBase(organizationId), {
    params: {
      status: filters.status || undefined,
      page: filters.page ?? 1,
      pageSize: filters.pageSize ?? 20,
    },
    signal,
  })
  return response.data
}

async function decide(
  organizationId: string,
  approvalId: string,
  verb: ApprovalDecision,
  payload: DecisionPayload,
  signal?: AbortSignal,
): Promise<ApprovalQueueEntry> {
  const response = await apiClient.post<ApprovalQueueEntry>(
    `${approvalsBase(organizationId)}/${approvalId}/${verb}`,
    payload,
    { signal },
  )
  return response.data
}

export const approveApproval = (
  organizationId: string,
  approvalId: string,
  payload: DecisionPayload = {},
  signal?: AbortSignal,
) => decide(organizationId, approvalId, 'approve', payload, signal)

export const rejectApproval = (
  organizationId: string,
  approvalId: string,
  payload: DecisionPayload = {},
  signal?: AbortSignal,
) => decide(organizationId, approvalId, 'reject', payload, signal)

export const reviseApproval = (
  organizationId: string,
  approvalId: string,
  payload: DecisionPayload = {},
  signal?: AbortSignal,
) => decide(organizationId, approvalId, 'revise', payload, signal)

/**
 * The decision verbs the caller may actually use.
 *
 * `approve` needs `approvals:approve`; `reject` and `revise` additionally need `orders:manage`, and
 * `orders:manage` alone does **not** grant `approve` — the two permissions are independent, which is
 * why this returns a list rather than a single boolean.
 */
export function availableDecisions(
  hasApprovalsApprove: boolean,
  hasOrdersManage: boolean,
): ApprovalDecision[] {
  const decisions: ApprovalDecision[] = []
  if (hasApprovalsApprove) {
    decisions.push('approve')
  }
  if (hasApprovalsApprove && hasOrdersManage) {
    decisions.push('reject', 'revise')
  }
  return decisions
}
