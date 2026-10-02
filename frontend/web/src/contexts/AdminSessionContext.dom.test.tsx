import { render, screen, waitFor } from '@testing-library/react'
import { useEffect } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { AdminSessionProvider, useAdminSession } from './AdminSessionContext'

const authState = { isLoaded: true, isSignedIn: false }
vi.mock('@clerk/react', () => ({
  useAuth: () => authState,
  useUser: () => ({ user: null }),
}))

const fetchAuthClaims = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchAuthClaims: (...args: unknown[]) => fetchAuthClaims(...args),
}))

const api = vi.hoisted(() => ({
  forbiddenHandler: null as ((error: unknown) => void) | null,
}))
vi.mock('@/lib/api', () => ({
  registerForbiddenHandler: (handler: ((error: unknown) => void) | null) => {
    api.forbiddenHandler = handler
  },
}))

function StatusProbe(): React.JSX.Element {
  const { status, userId, roles } = useAdminSession()
  useEffect(() => undefined, [])
  return (
    <div>
      <span data-testid="status">{status}</span>
      <span data-testid="userId">{userId ?? 'none'}</span>
      <span data-testid="roles">{roles.join(',') || 'none'}</span>
    </div>
  )
}

/** Reads the context's own `can()`, so the public permission check is exercised, not the Set. */
function PermissionProbe(): React.JSX.Element {
  const { can } = useAdminSession()
  return <span data-testid="can-catalog">{can('catalog:view') ? 'yes' : 'no'}</span>
}

describe('AdminSessionProvider, signed out', () => {
  it('never issues an admin claims request', async () => {
    fetchAuthClaims.mockReset()
    authState.isSignedIn = false

    render(
      <AdminSessionProvider>
        <StatusProbe />
      </AdminSessionProvider>,
    )

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('idle')
    })
    expect(fetchAuthClaims).not.toHaveBeenCalled()
  })

  it('never synthesises an identity', async () => {
    fetchAuthClaims.mockReset()
    authState.isSignedIn = false

    render(
      <AdminSessionProvider>
        <StatusProbe />
      </AdminSessionProvider>,
    )

    // The delivered provider fabricated `userId: "kaveesha"` and `roles: ["Admin"]` here,
    // which is what made the route guard admit a signed-out visitor.
    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('idle')
    })
    expect(screen.getByTestId('userId')).toHaveTextContent('none')
    expect(screen.getByTestId('roles')).toHaveTextContent('none')
  })

  it('loads real claims when signed in', async () => {
    fetchAuthClaims.mockReset()
    fetchAuthClaims.mockResolvedValue({
      userId: 'user-1',
      email: 'owner@aveline.lk',
      roles: ['owner'],
      account: { accountState: 'Active', hasCompletedOnboarding: true },
    })
    authState.isSignedIn = true

    render(
      <AdminSessionProvider>
        <StatusProbe />
      </AdminSessionProvider>,
    )

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('ready')
    })
    expect(screen.getByTestId('userId')).toHaveTextContent('user-1')
    expect(screen.getByTestId('roles')).toHaveTextContent('owner')
    expect(fetchAuthClaims).toHaveBeenCalledTimes(1)
    authState.isSignedIn = false
  })

  it('re-resolves claims only while the session is unsettled, so a 403 cannot loop', async () => {
    fetchAuthClaims.mockReset()
    fetchAuthClaims.mockResolvedValue({
      userId: 'user-1',
      email: 'owner@aveline.lk',
      roles: ['owner'],
      account: { accountState: 'Active', hasCompletedOnboarding: true },
    })
    authState.isSignedIn = true

    render(
      <AdminSessionProvider>
        <StatusProbe />
      </AdminSessionProvider>,
    )

    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('ready')
    })
    expect(fetchAuthClaims).toHaveBeenCalledTimes(1)

    // Once the session is settled a 403 is a genuine denial, not a stale permission set.
    // Refreshing here loops forever (refresh -> new context -> refetch -> 403 -> refresh).
    api.forbiddenHandler?.({ status: 403 })
    api.forbiddenHandler?.({ status: 403 })
    api.forbiddenHandler?.({ status: 403 })
    await waitFor(() => undefined)

    expect(fetchAuthClaims).toHaveBeenCalledTimes(1)
    authState.isSignedIn = false
  })

  it('refreshes claims when a 403 arrives before the session has settled', async () => {
    fetchAuthClaims.mockReset()
    // Hold the first request open, so the session is still "loading" when the 403 arrives and
    // the guard's unsettled branch is the one under test. The settled branch is the test above;
    // together they pin both halves of the guard.
    let releaseFirst: (() => void) | undefined
    fetchAuthClaims.mockImplementationOnce(
      () =>
        new Promise((resolve) => {
          releaseFirst = () =>
            resolve({
              userId: 'user-1',
              email: 'owner@aveline.lk',
              roles: ['owner'],
              account: { accountState: 'Active', hasCompletedOnboarding: true },
            })
        }),
    )
    fetchAuthClaims.mockResolvedValue({
      userId: 'user-1',
      email: 'owner@aveline.lk',
      roles: ['owner'],
      account: { accountState: 'Active', hasCompletedOnboarding: true },
    })
    authState.isSignedIn = true

    render(
      <AdminSessionProvider>
        <StatusProbe />
      </AdminSessionProvider>,
    )

    await waitFor(() => {
      expect(fetchAuthClaims).toHaveBeenCalledTimes(1)
    })

    // Unsettled: the permission set a 403 is complaining about may still be stale, so the
    // claims are re-resolved once.
    api.forbiddenHandler?.({ status: 403 })
    await waitFor(() => {
      expect(fetchAuthClaims).toHaveBeenCalledTimes(2)
    })

    releaseFirst?.()
    await waitFor(() => {
      expect(screen.getByTestId('status')).toHaveTextContent('ready')
    })
    authState.isSignedIn = false
  })

  it('exposes can() over the resolved permissions', async () => {
    fetchAuthClaims.mockReset()
    fetchAuthClaims.mockResolvedValue({
      userId: 'user-1',
      email: 'owner@aveline.lk',
      roles: ['owner'],
      account: { accountState: 'Active', hasCompletedOnboarding: true },
    })
    authState.isSignedIn = true

    render(
      <AdminSessionProvider>
        <PermissionProbe />
      </AdminSessionProvider>,
    )

    await waitFor(() => {
      expect(screen.getByTestId('can-catalog')).toHaveTextContent('yes')
    })
    authState.isSignedIn = false
  })
})
