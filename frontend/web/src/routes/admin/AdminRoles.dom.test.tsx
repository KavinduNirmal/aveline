import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { AdminRolesView } from './AdminRoles'

/**
 * The roles page is display-only (Q5: a read-only Roles page does not substitute for wiring up the
 * users and approvals surfaces, which A5 does). Its job is to render the **drift-tested** mirrors
 * faithfully and to say what they cannot express.
 */
describe('AdminRolesView', () => {
  it('renders a row per permission and a column per canonical role', () => {
    render(<AdminRolesView />)
    const table = screen.getByRole('table')
    expect(within(table).getAllByRole('row')).toHaveLength(1 + 24)
    expect(within(table).getAllByRole('columnheader')).toHaveLength(1 + 9)
  })

  it('marks owner as holding every permission, including pricing:backdate', () => {
    render(<AdminRolesView />)
    const row = within(screen.getByRole('table')).getByText('pricing:backdate').closest('tr')
    expect(row).not.toBeNull()
    expect(within(row as HTMLElement).getByLabelText('owner holds pricing:backdate')).toBeInTheDocument()
  })

  it('marks admin as denied pricing:backdate', () => {
    render(<AdminRolesView />)
    const row = within(screen.getByRole('table')).getByText('pricing:backdate').closest('tr')
    expect(
      within(row as HTMLElement).getByLabelText('admin does not hold pricing:backdate'),
    ).toBeInTheDocument()
  })

  it('marks moderator as denied audit:view, refuting the audit’s premise', () => {
    render(<AdminRolesView />)
    const row = within(screen.getByRole('table')).getByText('audit:view').closest('tr')
    expect(
      within(row as HTMLElement).getByLabelText('moderator does not hold audit:view'),
    ).toBeInTheDocument()
  })

  it('says the server is authoritative and names the four role-guarded policies', () => {
    render(<AdminRolesView />)
    expect(screen.getByText(/server is authoritative/i)).toBeInTheDocument()
    for (const policy of ['AdminReview', 'StatsSystem', 'AuditView', 'PricingAdminRead']) {
      expect(screen.getByText(policy)).toBeInTheDocument()
    }
  })
})
