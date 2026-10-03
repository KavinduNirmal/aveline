import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { RequireAccountState } from './RequireAccountState'

const userContext = {
  user: null as { userRole: string } | null,
  accountState: 'OnboardingPending' as 'OnboardingPending' | 'Active' | 'Suspended' | null,
  isLoading: false,
}

const clerk = {
  user: null as { unsafeMetadata?: Record<string, unknown> | null } | null,
  isLoaded: true,
}

vi.mock('@/contexts/UserContext', () => ({
  useUserContext: () => userContext,
}))

vi.mock('@clerk/react', () => ({
  useUser: () => clerk,
}))

function renderGuard() {
  return render(
    <MemoryRouter initialEntries={['/app']}>
      <Routes>
        <Route element={<RequireAccountState />}>
          <Route path="/app" element={<div>BUSINESS_TREE</div>} />
        </Route>
        <Route path="/onboarding" element={<div>ONBOARDING_WIZARD</div>} />
        <Route path="/admin/pending" element={<div>PENDING_SCREEN</div>} />
        <Route path="/suspended" element={<div>SUSPENDED_PAGE</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe('RequireAccountState', () => {
  beforeEach(() => {
    userContext.user = null
    userContext.accountState = 'OnboardingPending'
    userContext.isLoading = false
    clerk.user = null
    clerk.isLoaded = true
  })

  it('admits an owner whose account is still OnboardingPending', () => {
    // Regression: granted the console role but never activated, which is the *normal* shape of
    // an approved administrator (`Roles.OnboardingExemptRoles`). Bouncing this to
    // `/admin/pending` made that screen forward to `/admin`, which bounced straight back —
    // an infinite loop that re-fetched /auth/claims on every lap.
    userContext.user = { userRole: 'owner' }
    userContext.accountState = 'OnboardingPending'
    clerk.user = { unsafeMetadata: { accountType: 'admin' } }

    renderGuard()

    expect(screen.getByText('BUSINESS_TREE')).toBeInTheDocument()
    expect(screen.queryByText('PENDING_SCREEN')).toBeNull()
  })

  it('admits an admin and a moderator whose accounts are still OnboardingPending', () => {
    userContext.accountState = 'OnboardingPending'
    clerk.user = { unsafeMetadata: { accountType: 'admin' } }

    for (const userRole of ['admin', 'moderator', 'Admin']) {
      userContext.user = { userRole }
      const view = renderGuard()
      expect(screen.getByText('BUSINESS_TREE')).toBeInTheDocument()
      expect(screen.queryByText('PENDING_SCREEN')).toBeNull()
      view.unmount()
    }
  })

  it('still parks an administrator sign-up holding no console role', () => {
    userContext.user = { userRole: 'user' }
    userContext.accountState = 'OnboardingPending'
    clerk.user = { unsafeMetadata: { accountType: 'admin' } }

    renderGuard()

    expect(screen.getByText('PENDING_SCREEN')).toBeInTheDocument()
    expect(screen.queryByText('BUSINESS_TREE')).toBeNull()
  })

  it('still sends an ordinary pending account to the onboarding wizard', () => {
    userContext.user = { userRole: 'staff' }
    userContext.accountState = 'OnboardingPending'
    clerk.user = { unsafeMetadata: {} }

    renderGuard()

    expect(screen.getByText('ONBOARDING_WIZARD')).toBeInTheDocument()
    expect(screen.queryByText('BUSINESS_TREE')).toBeNull()
  })

  it('suspension outranks the onboarding exemption', () => {
    // A suspended owner keeps no access: the suspended branch is evaluated first, so the
    // exemption can never resurrect a suspended account.
    userContext.user = { userRole: 'owner' }
    userContext.accountState = 'Suspended'

    renderGuard()

    expect(screen.getByText('SUSPENDED_PAGE')).toBeInTheDocument()
    expect(screen.queryByText('BUSINESS_TREE')).toBeNull()
  })

  it('admits an active ordinary account', () => {
    userContext.user = { userRole: 'staff' }
    userContext.accountState = 'Active'
    clerk.user = { unsafeMetadata: {} }

    renderGuard()

    expect(screen.getByText('BUSINESS_TREE')).toBeInTheDocument()
  })

  it('does not exempt when the profile has not loaded a role', () => {
    // `/users/me` is the role source here. No profile row means no evidence of a console role,
    // so the pending branch still applies rather than admitting an unproven account.
    userContext.user = null
    userContext.accountState = 'OnboardingPending'
    clerk.user = { unsafeMetadata: { accountType: 'admin' } }

    renderGuard()

    expect(screen.getByText('PENDING_SCREEN')).toBeInTheDocument()
    expect(screen.queryByText('BUSINESS_TREE')).toBeNull()
  })

  it('waits for the profile before deciding', () => {
    userContext.user = null
    userContext.accountState = null
    userContext.isLoading = true
    clerk.user = null

    renderGuard()

    expect(screen.queryByText('BUSINESS_TREE')).toBeNull()
    expect(screen.queryByText('PENDING_SCREEN')).toBeNull()
    expect(screen.queryByText('ONBOARDING_WIZARD')).toBeNull()
  })
})
