import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { DashboardRedirect } from './Dashboard'

const userContext = {
  user: null as null | { id: string; userRole: string },
  isLoading: false,
}
vi.mock('@/contexts/UserContext', () => ({
  useUserContext: () => userContext,
}))

const fetchMyOrganizations = vi.fn()
vi.mock('@/lib/organizations', () => ({
  fetchMyOrganizations: () => fetchMyOrganizations(),
}))

function renderAt() {
  return render(
    <MemoryRouter initialEntries={['/app']}>
      <Routes>
        <Route path="/app" element={<DashboardRedirect />} />
        <Route path="/app/b/:slug" element={<div>TENANT_DASHBOARD</div>} />
        <Route path="/admin/:userId/dashboard" element={<div>ADMIN_CONSOLE</div>} />
        <Route path="/forbidden" element={<div>FORBIDDEN</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

/**
 * `/app` is what every "dashboard" link in the product resolves to. An Aveline-team operator has no
 * boutique, so the tenant resolution could only ever send them to `/forbidden` — which is exactly
 * what it did.
 */
describe('DashboardRedirect', () => {
  it('sends a console role to the administrator console', async () => {
    userContext.user = { id: 'db-1', userRole: 'owner' }
    fetchMyOrganizations.mockReset()

    renderAt()

    await waitFor(() => {
      expect(screen.getByText('ADMIN_CONSOLE')).toBeInTheDocument()
    })
    // It must not bother resolving boutique memberships for an operator.
    expect(fetchMyOrganizations).not.toHaveBeenCalled()
  })

  it('admits an admin role too', async () => {
    userContext.user = { id: 'db-2', userRole: 'Admin' }
    fetchMyOrganizations.mockReset()

    renderAt()

    await waitFor(() => {
      expect(screen.getByText('ADMIN_CONSOLE')).toBeInTheDocument()
    })
  })

  it('still resolves the boutique dashboard for a tenant role', async () => {
    userContext.user = { id: 'db-3', userRole: 'staff' }
    fetchMyOrganizations.mockReset()
    fetchMyOrganizations.mockResolvedValue([{ status: 'Active', slug: 'aveline-boutique' }])

    renderAt()

    await waitFor(() => {
      expect(screen.getByText('TENANT_DASHBOARD')).toBeInTheDocument()
    })
  })

  it('sends a tenant with no active boutique to forbidden', async () => {
    userContext.user = { id: 'db-4', userRole: 'staff' }
    fetchMyOrganizations.mockReset()
    fetchMyOrganizations.mockResolvedValue([])

    renderAt()

    await waitFor(() => {
      expect(screen.getByText('FORBIDDEN')).toBeInTheDocument()
    })
  })

  it('refuses a moderator, who holds no console role', async () => {
    userContext.user = { id: 'db-5', userRole: 'moderator' }
    fetchMyOrganizations.mockReset()
    fetchMyOrganizations.mockResolvedValue([])

    renderAt()

    await waitFor(() => {
      expect(screen.getByText('FORBIDDEN')).toBeInTheDocument()
    })
  })
})
