import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminRouteGuard } from './AdminRouteGuard'

const session = {
  status: 'ready' as 'idle' | 'loading' | 'ready' | 'error' | 'forbidden',
  roles: ['owner'] as string[],
  userId: 'u1' as string | null,
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
        <Route path="/admin/:userId/dashboard" element={<div>OWN_CONSOLE</div>} />
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

  it('falls back to the caller’s own console for an unresolvable segment, never a dead end', () => {
    // C1 option (c): when the `{user_Id}` segment does not resolve to a user, it becomes a
    // restatement of the caller. The delivered behaviour showed a dead-end card, which made the
    // console unusable the moment the resolver could not confirm the id.
    session.status = 'ready'
    session.roles = ['owner']
    session.userId = 'u1'
    scopeState.scope = { kind: 'unknown' }
    renderGuard()
    expect(screen.queryByText('SECRET_SECTION')).toBeNull()
    expect(screen.getByText('OWN_CONSOLE')).toBeInTheDocument()
  })

  it('renders a stated refusal for an unresolvable segment when there is no session id', () => {
    session.status = 'ready'
    session.roles = ['owner']
    session.userId = null
    scopeState.scope = { kind: 'unknown' }
    renderGuard()
    expect(screen.queryByText('SECRET_SECTION')).toBeNull()
    expect(screen.getByText(/scope not available/i)).toBeInTheDocument()
    session.userId = 'u1'
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
