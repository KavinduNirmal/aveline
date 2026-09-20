import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { SidebarProvider } from '@/components/ui/sidebar'

import { AdminSidePanel } from './AdminSidePanel'

/**
 * The panel is a **pure filter** over the registry: a denied entry is absent from the DOM, not
 * rendered disabled. That is what makes the two business entries invisible to a caller without
 * `analytics:business:read` with no extra code.
 */
const session = {
  status: 'ready',
  email: 'owner@aveline.lk',
  userId: 'user-1',
  roles: ['owner'] as string[],
  permissions: new Set<string>(),
  accountState: 'Active',
  hasCompletedOnboarding: true,
  error: null,
  can: (_permission: string) => true,
  refresh: async () => undefined,
}

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => session,
}))

// The scope chip needs the app's UserProvider; this test is about the registry filter.
vi.mock('./AdminScopeChip', () => ({ AdminScopeChip: () => null }))

function renderPanel() {
  return render(
    <MemoryRouter initialEntries={['/admin/u1']}>
      <SidebarProvider>
        <Routes>
          <Route path="/admin/:userId" element={<AdminSidePanel />} />
        </Routes>
      </SidebarProvider>
    </MemoryRouter>,
  )
}

describe('AdminSidePanel and the business domain', () => {
  it('renders both business entries when the caller holds the permission', () => {
    session.can = () => true
    renderPanel()

    expect(screen.getByText('Growth')).toBeInTheDocument()
    expect(screen.getByText('Usage & Engagement')).toBeInTheDocument()
    expect(screen.getByText('Business')).toBeInTheDocument()
  })

  it('omits both business entries, rather than disabling them, without the permission', () => {
    session.can = (permission: string) => permission !== 'analytics:business:read'
    renderPanel()

    expect(screen.queryByText('Growth')).not.toBeInTheDocument()
    expect(screen.queryByText('Usage & Engagement')).not.toBeInTheDocument()
    // The domain heading disappears with its last entry.
    expect(screen.queryByText('Business')).not.toBeInTheDocument()
  })
})
