import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminUsersView } from './AdminUsers'

const searchAdminUsers = vi.fn()
const updateUserAccountState = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  searchAdminUsers: (...args: unknown[]) => searchAdminUsers(...args),
  updateUserAccountState: (...args: unknown[]) => updateUserAccountState(...args),
}))

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({ can: () => true, roles: ['owner'] }),
}))

function renderAt(entry: string) {
  // The admin tree mounts its own QueryClientProvider inside AdminLayout (C4); the page is
  // rendered directly here, so the test supplies one.
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/admin/:userId/users" element={<AdminUsersView />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/**
 * Filters and paging live in the URL, so a filtered view survives the back button and is
 * shareable. The delivered page held them in component state, which the back button discarded.
 */
describe('AdminUsersView list state', () => {
  it('reads q, accountState, page and pageSize from the URL', async () => {
    searchAdminUsers.mockReset()
    searchAdminUsers.mockResolvedValue({ items: [], page: 2, pageSize: 25, total: 0 })

    renderAt('/admin/u1/users?q=ada&accountState=Active&page=2&pageSize=25')

    await waitFor(() => {
      expect(searchAdminUsers).toHaveBeenCalledWith(
        expect.objectContaining({ q: 'ada', accountState: 'Active', page: 2, pageSize: 25 }),
      )
    })
  })

  it('defaults to page 1 with a page size the server accepts', async () => {
    searchAdminUsers.mockReset()
    searchAdminUsers.mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 })

    renderAt('/admin/u1/users?pageSize=9999')

    await waitFor(() => {
      expect(searchAdminUsers).toHaveBeenCalledWith(
        expect.objectContaining({ page: 1, pageSize: 25 }),
      )
    })
  })

  it('renders the empty state rather than a blank table', async () => {
    searchAdminUsers.mockReset()
    searchAdminUsers.mockResolvedValue({ items: [], page: 1, pageSize: 25, total: 0 })

    renderAt('/admin/u1/users')

    await waitFor(() => {
      expect(screen.getByText(/no users/i)).toBeInTheDocument()
    })
  })
})

/**
 * C4's reason for adopting TanStack Query: a page change must not blank the table. The delivered
 * page held rows in component state and cleared them on every fetch.
 */
describe('AdminUsersView keepPreviousData', () => {
  it('keeps the previous page visible while the next page is in flight', async () => {
    searchAdminUsers.mockReset()
    searchAdminUsers.mockResolvedValueOnce({
      items: [
        {
          id: 'u-1',
          clerkId: 'user_1',
          email: 'ada@aveline.lk',
          firstName: 'Ada',
          lastName: 'Lovelace',
          displayName: null,
          username: 'ada',
          phoneNumber: '',
          address: null,
          profileImageUrl: null,
          userRole: 'staff',
          organizationRole: '',
          organizationId: '',
          hasCompletedOnboarding: true,
          accountState: 'Active',
          contactPreference: 'Email',
          pushNotificationsEnabled: false,
          isActive: true,
          createdAt: '2026-01-01T00:00:00Z',
          updatedAt: '2026-01-01T00:00:00Z',
        },
      ],
      page: 1,
      pageSize: 25,
      total: 50,
    })
    // Page 2 never settles: the table must still show page 1's rows.
    searchAdminUsers.mockImplementationOnce(() => new Promise(() => undefined))

    renderAt('/admin/u1/users')

    await waitFor(() => {
      expect(screen.getByText(/Ada Lovelace/)).toBeInTheDocument()
    })

    await userEvent.click(screen.getByRole('button', { name: /next/i }))

    await waitFor(() => {
      expect(searchAdminUsers).toHaveBeenCalledWith(expect.objectContaining({ page: 2 }))
    })
    expect(screen.getByText(/Ada Lovelace/)).toBeInTheDocument()
  })
})
