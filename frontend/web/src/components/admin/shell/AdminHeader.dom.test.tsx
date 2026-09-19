import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { SidebarProvider } from '@/components/ui/sidebar'
import { AdminHeader } from './AdminHeader'

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({ email: 'owner@aveline.lk' }),
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
})
