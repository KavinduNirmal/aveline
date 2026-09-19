import { render, screen, waitFor } from '@testing-library/react'
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
  return render(
    <MemoryRouter initialEntries={[entry]}>
      <Routes>
        <Route path="/admin/:userId/users" element={<AdminUsersView />} />
      </Routes>
    </MemoryRouter>,
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
