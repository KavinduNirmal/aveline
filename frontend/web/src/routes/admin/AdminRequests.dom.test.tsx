import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminRequestsView } from './AdminRequests'

const session = {
  status: 'ready' as const,
  email: null as string | null,
  clerkUserId: 'user_self' as string | null,
  roles: ['owner'] as string[],
  permissions: new Set<string>(),
  accountState: 'Active',
  hasCompletedOnboarding: true,
  provisional: false,
  error: null,
  can: () => true,
  refresh: async () => undefined,
}
vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => session,
}))

const listAdminRequests = vi.fn()
const approveAdminRequest = vi.fn()
const rejectAdminRequest = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  listAdminRequests: () => listAdminRequests(),
  approveAdminRequest: (...args: unknown[]) => approveAdminRequest(...args),
  rejectAdminRequest: (...args: unknown[]) => rejectAdminRequest(...args),
}))

function request(overrides: Record<string, unknown>) {
  return {
    id: 'r-1',
    clerkUserId: 'user_other',
    // The captured live fixture had `email: ""` on every row and `email: null` on /auth/claims,
    // which is exactly why the delivered guard (`email === email`) admitted self-approval.
    email: '',
    firstName: 'Ada',
    lastName: 'Lovelace',
    status: 'Pending',
    requestedAt: '2026-01-01T00:00:00Z',
    reviewedAt: null,
    reviewedByClerkUserId: null,
    ...overrides,
  }
}

function renderView() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminRequestsView />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminRequestsView', () => {
  it('disables self-approval by clerkUserId even when every email is empty', async () => {
    session.clerkUserId = 'user_self'
    listAdminRequests.mockResolvedValue([
      request({ id: 'r-self', clerkUserId: 'user_self', firstName: 'Self', email: '' }),
      request({ id: 'r-other', clerkUserId: 'user_other', firstName: 'Other', email: '' }),
    ])

    renderView()

    await waitFor(() => {
      expect(screen.getByText(/Self Lovelace/)).toBeInTheDocument()
    })

    const approveButtons = screen.getAllByRole('button', { name: /approve/i })
    expect(approveButtons).toHaveLength(2)
    expect(approveButtons[0]).toBeDisabled()
    expect(approveButtons[1]).toBeEnabled()

    await userEvent.click(approveButtons[1])
    expect(approveAdminRequest).toHaveBeenCalledWith('r-other')
  })

  it('counts pending approvals by status, not by row count', async () => {
    listAdminRequests.mockResolvedValue([
      request({ id: 'r-1', status: 'Approved' }),
      request({ id: 'r-2', status: 'Approved' }),
    ])

    renderView()

    await waitFor(() => {
      expect(screen.getByText(/0 pending/i)).toBeInTheDocument()
    })
  })

  it('reports the pending count when there are pending rows', async () => {
    listAdminRequests.mockResolvedValue([
      request({ id: 'r-1', status: 'Pending' }),
      request({ id: 'r-2', status: 'Approved' }),
    ])

    renderView()

    await waitFor(() => {
      expect(screen.getByText(/1 pending/i)).toBeInTheDocument()
    })
  })
})
