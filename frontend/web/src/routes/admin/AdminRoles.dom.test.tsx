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
    // 28 permissions since the revenue family joined the catalogue (Revenue Ledger R0).
    expect(within(table).getAllByRole('row')).toHaveLength(1 + 28)
    expect(within(table).getAllByRole('columnheader')).toHaveLength(1 + 9)
  })

  it('grants analytics:business:read to moderator, admin and owner only', () => {
    render(<AdminRolesView />)
    const row = within(screen.getByRole('table'))
      .getByText('analytics:business:read')
      .closest('tr')
    expect(row).not.toBeNull()
    const cells = within(row as HTMLElement)
    expect(cells.getByLabelText('moderator holds analytics:business:read')).toBeInTheDocument()
    expect(cells.getByLabelText('admin holds analytics:business:read')).toBeInTheDocument()
    expect(cells.getByLabelText('owner holds analytics:business:read')).toBeInTheDocument()
    expect(
      cells.getByLabelText('org:boutique_owner does not hold analytics:business:read'),
    ).toBeInTheDocument()
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

  /**
   * R0. The revenue family's read/write split is the one place a moderator's grant and denial
   * sit on adjacent rows, so the roles page is where a reader can see the split at a glance.
   */
  it('shows a moderator holding revenue:read and denied the money-moving permissions', () => {
    render(<AdminRolesView />)
    const table = within(screen.getByRole('table'))

    const readRow = table.getByText('revenue:read').closest('tr') as HTMLElement
    expect(within(readRow).getByLabelText('moderator holds revenue:read')).toBeInTheDocument()

    const manageRow = table.getByText('revenue:manage').closest('tr') as HTMLElement
    expect(
      within(manageRow).getByLabelText('moderator does not hold revenue:manage'),
    ).toBeInTheDocument()

    // And the one denial that distinguishes an admin from an owner.
    const refundRow = table.getByText('revenue:refund').closest('tr') as HTMLElement
    expect(within(refundRow).getByLabelText('owner holds revenue:refund')).toBeInTheDocument()
    expect(
      within(refundRow).getByLabelText('admin does not hold revenue:refund'),
    ).toBeInTheDocument()
  })

  it('says the server is authoritative and names every role-guarded policy', () => {
    render(<AdminRolesView />)
    expect(screen.getByText(/server is authoritative/i)).toBeInTheDocument()
    for (const policy of [
      'AdminReview',
      'StatsSystem',
      'AuditView',
      'PricingAdminRead',
      'MoneyRead',
      'MoneyOperations',
    ]) {
      expect(screen.getByText(policy)).toBeInTheDocument()
    }
  })
})
