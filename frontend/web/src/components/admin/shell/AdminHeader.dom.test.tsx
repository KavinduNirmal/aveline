import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { SidebarProvider } from '@/components/ui/sidebar'
import { AdminHeader } from './AdminHeader'

const session = { email: 'owner@aveline.lk' as string | null }
vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => session,
}))

const userContext = { user: null as null | { email: string; username: string } }
vi.mock('@/contexts/UserContext', () => ({
  useUserContext: () => userContext,
}))

function renderHeader() {
  return render(
    <SidebarProvider>
      <MemoryRouter initialEntries={['/admin/db-1/dashboard']}>
        <Routes>
          <Route
            path="/admin/:userId/dashboard"
            element={<AdminHeader onOpenAudit={() => undefined} />}
          />
        </Routes>
      </MemoryRouter>
    </SidebarProvider>,
  )
}

/**
 * The console is not a boutique surface. The delivered header offered "Back to Boutique", which an
 * Aveline-team operator has no boutique for — it took them straight to `/forbidden`.
 */
describe('AdminHeader', () => {
  it('offers no route back to a boutique', () => {
    renderHeader()
    expect(screen.queryByRole('button', { name: /back to boutique/i })).toBeNull()
    expect(screen.queryByRole('link', { name: /back to boutique/i })).toBeNull()
  })

  it('keeps the audit drawer control', () => {
    renderHeader()
    expect(screen.getByRole('button', { name: /audit log/i })).toBeInTheDocument()
  })

  it('shows the claims email when it is present', () => {
    session.email = 'owner@aveline.lk'
    userContext.user = { email: 'db@aveline.lk', username: 'owner' }
    renderHeader()
    expect(screen.getByText('owner@aveline.lk')).toBeInTheDocument()
  })

  it('falls back to the signed-in account when the claims email is null', () => {
    // `/auth/claims` returned `email: null` for every real bearer token, and the header fell back
    // to "unknown account" even though the console already holds the account record.
    session.email = null
    userContext.user = { email: 'db@aveline.lk', username: 'owner' }
    renderHeader()
    expect(screen.getByText('db@aveline.lk')).toBeInTheDocument()
    expect(screen.queryByText(/unknown account/i)).toBeNull()
  })

  it('falls back to the username when there is no email at all', () => {
    session.email = null
    userContext.user = { email: '', username: 'owner' }
    renderHeader()
    expect(screen.getByText('@owner')).toBeInTheDocument()
  })

  it('never claims an account is unknown', () => {
    session.email = null
    userContext.user = null
    renderHeader()
    expect(screen.queryByText(/unknown account/i)).toBeNull()
    expect(screen.getByText(/signed-in account/i)).toBeInTheDocument()
  })
})
