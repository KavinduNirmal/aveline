import { render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { Permission } from '@/lib/admin/permissions'
import { Gate } from './Gate'

const session = {
  roles: ['admin'] as string[],
  can: (_permission: Permission) => false,
  status: 'ready' as const,
}
vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => session,
}))

/**
 * A denied section must be **absent from the DOM**, not hidden by CSS. The delivered console
 * rendered sections a caller could only get a `403` from; an operator cannot tell a greyed-out
 * control from a broken one.
 */
describe('Gate', () => {
  it('omits children entirely when the permission is not granted', () => {
    session.roles = ['admin']
    session.can = () => false

    render(
      <Gate gate={{ kind: 'permission', permission: 'pricing:backdate' }}>
        <button type="button">Recompute</button>
      </Gate>,
    )

    expect(screen.queryByText('Recompute')).toBeNull()
    expect(screen.queryByRole('button')).toBeNull()
  })

  it('renders children when the permission is granted', () => {
    session.roles = ['owner']
    session.can = (permission) => permission === 'pricing:backdate'

    render(
      <Gate gate={{ kind: 'permission', permission: 'pricing:backdate' }}>
        <button type="button">Recompute</button>
      </Gate>,
    )

    expect(screen.getByRole('button', { name: 'Recompute' })).toBeInTheDocument()
  })

  it('omits children when the role gate refuses, even if the permission is held', () => {
    session.roles = ['moderator']
    session.can = () => true

    render(
      <Gate gate={{ kind: 'role', anyOf: ['owner', 'admin'] }}>
        <span>Audit explorer</span>
      </Gate>,
    )

    expect(screen.queryByText('Audit explorer')).toBeNull()
  })

  it('renders a caller-supplied fallback instead of nothing when asked', () => {
    session.roles = ['admin']
    session.can = () => false

    render(
      <Gate
        gate={{ kind: 'permission', permission: 'pricing:backdate' }}
        fallback={<span>requires the pricing:backdate grant</span>}
      >
        <button type="button">Recompute</button>
      </Gate>,
    )

    expect(screen.getByText('requires the pricing:backdate grant')).toBeInTheDocument()
    expect(screen.queryByRole('button')).toBeNull()
  })
})
