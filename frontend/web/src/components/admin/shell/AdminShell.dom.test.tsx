import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminShell } from './AdminShell'

vi.mock('./AdminSidePanel', () => ({ AdminSidePanel: () => <nav data-testid="panel" /> }))
vi.mock('./AdminHeader', () => ({
  AdminHeader: ({ onOpenAudit }: { onOpenAudit: () => void }) => (
    <header data-testid="header">
      <button type="button" onClick={onOpenAudit}>
        Audit Log
      </button>
    </header>
  ),
}))
vi.mock('@/components/admin/AuditTrailPanel', () => ({
  AuditTrailPanel: () => <aside data-testid="audit-drawer" />,
}))

function renderShell() {
  return render(
    <MemoryRouter initialEntries={['/admin/u1/dashboard']}>
      <Routes>
        <Route path="/admin/:userId" element={<AdminShell />}>
          <Route path="dashboard" element={<div>PAGE_CONTENT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  )
}

/**
 * The geometry invariant. jsdom cannot measure layout, so this pins the *structure*: one
 * container element wraps the header and the content together, which is what makes the two
 * left edges identical. The pixel measurement is A8's Playwright walk.
 */
describe('AdminShell', () => {
  it('applies the geometry container exactly once, around header and content', () => {
    const { container } = renderShell()
    const geometry = container.querySelectorAll('.admin-container')
    expect(geometry).toHaveLength(1)
    expect(geometry[0].querySelector('[data-testid="header"]')).not.toBeNull()
    expect(geometry[0].querySelector('#admin-main')).not.toBeNull()
  })

  it('provides a skip link that targets the main region', () => {
    renderShell()
    const skip = screen.getByRole('link', { name: /skip to main content/i })
    expect(skip).toHaveAttribute('href', '#admin-main')
    expect(document.querySelector('#admin-main')).not.toBeNull()
  })

  it('renders the routed page inside the main region', () => {
    renderShell()
    expect(screen.getByText('PAGE_CONTENT')).toBeInTheDocument()
    expect(document.querySelector('#admin-main')?.textContent).toContain('PAGE_CONTENT')
  })

  it('renders the navigation panel and the audit drawer', () => {
    renderShell()
    expect(screen.getByTestId('panel')).toBeInTheDocument()
    expect(screen.getByTestId('audit-drawer')).toBeInTheDocument()
  })
})
