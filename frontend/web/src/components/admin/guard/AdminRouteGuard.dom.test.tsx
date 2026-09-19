import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminRouteGuard } from './AdminRouteGuard'

const session = {
  status: 'ready' as 'idle' | 'loading' | 'ready' | 'error' | 'forbidden',
  roles: ['owner'] as string[],
  error: null as string | null,
  can: () => true,
  refresh: vi.fn(),
}
const scopeState = { status: 'settled' as 'resolving' | 'settled', scope: { kind: 'self', userId: 'u1' } as unknown }

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => session,
}))
vi.mock('@/hooks/useAdminScope', () => ({
  useAdminScope: () => scopeState,
}))
vi.mock('@clerk/react', () => ({
  useUser: () => ({ user: null }),
}))

function renderGuard() {
  return render(
    <MemoryRouter initialEntries={['/']}>
      <Routes>
        <Route element={<AdminRouteGuard />}>
          <Route path="/" element={<div>SECRET_SECTION</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

describe('AdminRouteGuard', () => {
  it('admits an owner', () => {
    session.status = 'ready'
    session.roles = ['owner']
    scopeState.scope = { kind: 'self', userId: 'u1' }
    renderGuard()
    expect(screen.getByText('SECRET_SECTION')).toBeInTheDocument()
  })

  it('refuses a moderator at the door with a stated reason, and issues no section', () => {
    session.status = 'ready'
    session.roles = ['moderator']
    renderGuard()

    expect(screen.queryByText('SECRET_SECTION')).toBeNull()
    // The console is the Aveline team's internal tool (C2); a moderator holds no admin
    // surface of its own, so it is refused rather than shown a panel of four 403s.
    expect(
      screen.getByText(/limited to the .*owner.* and .*admin.* roles/i),
    ).toBeInTheDocument()
  })

  it('refuses a boutique role', () => {
    session.status = 'ready'
    session.roles = ['org:boutique_owner']
    renderGuard()
    expect(screen.queryByText('SECRET_SECTION')).toBeNull()
  })

  it('does not render the section for an out-of-scope segment', () => {
    session.status = 'ready'
    session.roles = ['owner']
    scopeState.scope = { kind: 'unknown' }
    renderGuard()
    expect(screen.queryByText('SECRET_SECTION')).toBeNull()
  })

  it('refuses an out-of-scope caller', () => {
    session.status = 'ready'
    session.roles = ['owner']
    scopeState.scope = { kind: 'forbidden' }
    renderGuard()
    expect(screen.queryByText('SECRET_SECTION')).toBeNull()
  })

  it('renders the session error with a retry when the session failed', () => {
    session.status = 'error'
    session.error = 'boom'
    renderGuard()
    expect(screen.getByText(/boom/)).toBeInTheDocument()
  })
})
