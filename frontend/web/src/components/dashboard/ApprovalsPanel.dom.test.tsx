import { render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchApprovals = vi.fn()
const approveApproval = vi.fn()
const rejectApproval = vi.fn()
const reviseApproval = vi.fn()

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/contexts/NotificationsContext', () => ({
  useNotifications: () => ({ connectionState: 'Disconnected', lastNotification: null }),
}))

vi.mock('@/lib/approvals-api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/approvals-api')>('@/lib/approvals-api')
  return {
    ...actual,
    fetchApprovals: (...a: unknown[]) => fetchApprovals(...a),
    approveApproval: (...a: unknown[]) => approveApproval(...a),
    rejectApproval: (...a: unknown[]) => rejectApproval(...a),
    reviseApproval: (...a: unknown[]) => reviseApproval(...a),
  }
})

import { ApprovalsPanel } from './ApprovalsPanel'

const ORG = '11111111-1111-1111-1111-111111111111'

const ORGANIZATION = {
  id: ORG,
  name: 'Test Boutique',
  slug: 'test-boutique',
  planTier: 'Bloom',
} as never

const ENTRY = {
  id: 'a1',
  organizationId: ORG,
  orderId: 'o1',
  approvalType: 'discount',
  status: 'pending',
  thresholdExceeded: true,
  reason: 'Requested 30% discount',
  decisionComment: null,
  decidedBy: null,
  threadId: null,
  conversationId: null,
  createdAt: '2026-09-20T10:00:00Z',
  decidedAt: null,
  order: {
    id: 'o1',
    organizationId: ORG,
    customerId: 'c1',
    customerName: 'Sarah Perera',
    orderType: 'retail',
    status: 'pending_approval',
    subtotal: 50000,
    discount: 15000,
    total: 35000,
    totalCost: 20000,
    margin: 0.42,
    createdAt: '2026-09-20T10:00:00Z',
    updatedAt: null,
  },
}

describe('ApprovalsPanel decision gating (Q14)', () => {
  beforeEach(() => {
    fetchApprovals.mockReset().mockResolvedValue({
      items: [ENTRY],
      page: 1,
      pageSize: 20,
      total: 1,
    })
  })

  it('offers a staff approver only Approve — never the cancel or the money rewrite', async () => {
    render(<ApprovalsPanel organization={ORGANIZATION} role="org:boutique_staff" />)

    expect(await screen.findByText('Sarah Perera')).toBeTruthy()
    expect(screen.getByRole('button', { name: /approve/i })).toBeTruthy()
    // `reject` cancels the order and `revise` rewrites its money; Q8 denied staff both.
    expect(screen.queryByRole('button', { name: /reject/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /revise/i })).toBeNull()
  })

  it('offers an owner all three verbs', async () => {
    render(<ApprovalsPanel organization={ORGANIZATION} role="org:boutique_owner" />)

    expect(await screen.findByText('Sarah Perera')).toBeTruthy()
    expect(screen.getByRole('button', { name: /approve/i })).toBeTruthy()
    expect(screen.getByRole('button', { name: /reject/i })).toBeTruthy()
    expect(screen.getByRole('button', { name: /revise/i })).toBeTruthy()
  })
})
