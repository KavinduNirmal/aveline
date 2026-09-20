import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AdminOrganizationDto, BlossomStatement } from '@/types/admin'
import { AdminBlossomsView } from './AdminBlossoms'

const fetchBlossomStatement = vi.fn()
const fetchBlossomReconciliation = vi.fn()
const creditBlossoms = vi.fn()
const debitBlossoms = vi.fn()
const revokeBlossoms = vi.fn()
const searchAdminOrganizations = vi.fn()

vi.mock('@/lib/admin/api', () => ({
  fetchBlossomStatement: (...args: unknown[]) => fetchBlossomStatement(...args),
  reviseReconciliation: (...args: unknown[]) => fetchBlossomReconciliation(...args),
  creditBlossoms: (...args: unknown[]) => creditBlossoms(...args),
  debitBlossoms: (...args: unknown[]) => debitBlossoms(...args),
  revokeBlossoms: (...args: unknown[]) => revokeBlossoms(...args),
  searchAdminOrganizations: (...args: unknown[]) => searchAdminOrganizations(...args),
}))

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({ can: () => true, roles: ['owner'] }),
}))

const ORG: AdminOrganizationDto = {
  id: 'org-1',
  name: 'Blossom Boutique',
  slug: 'blossom-boutique',
  clerkOrgId: null,
  ownerUserId: 'u-1',
  planTier: 'Bloom',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
}

function statement(overrides: Partial<BlossomStatement> = {}): BlossomStatement {
  return {
    organizationId: ORG.id,
    periodStart: '2026-09-01T00:00:00Z',
    periodEnd: '2026-10-01T00:00:00Z',
    openingBalance: 100,
    items: [],
    total: 0,
    page: 1,
    pageSize: 25,
    closingBalance: 100,
    reconciliation: {
      projectedBalance: 100,
      ledgerDerivedBalance: 100,
      drift: 0,
      isConsistent: true,
    },
    generatedAt: '2026-09-20T00:00:00Z',
    maxWindowDays: 400,
    dataQuality: {
      reconciliationChecked: true,
      openingBalanceFromProjection: true,
      windowCapped: false,
      maxWindowDays: 400,
      notes: ['The opening balance is derived from the balance projection.'],
    },
    ...overrides,
  }
}

function renderAt(entry: string) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[entry]}>
        <Routes>
          <Route path="/admin/:userId/blossoms" element={<AdminBlossomsView />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/** Drives the org picker, which is the one deliberate input before the statement loads. */
async function selectOrganization() {
  const user = userEvent.setup()
  const picker = screen.getByLabelText(/organization/i)
  await user.type(picker, 'blos')
  const option = await screen.findByRole('option', { name: /Blossom Boutique/i })
  await user.click(option)
}

beforeEach(() => {
  fetchBlossomStatement.mockReset()
  fetchBlossomReconciliation.mockReset()
  creditBlossoms.mockReset()
  debitBlossoms.mockReset()
  revokeBlossoms.mockReset()
  searchAdminOrganizations.mockReset()

  searchAdminOrganizations.mockResolvedValue({ items: [ORG], page: 1, pageSize: 10, total: 1 })
  fetchBlossomStatement.mockResolvedValue(statement())
  fetchBlossomReconciliation.mockResolvedValue({
    organizationId: null,
    accounts: [],
    driftedCount: 0,
    accountsChecked: 0,
    reconciliationChecked: null,
    checkedAt: '2026-09-20T00:00:00Z',
    notes: [],
  })
})

/**
 * The redesign's spine. The delivered page held the statement in component state and loaded it only
 * from a button, so nothing appeared until the operator pressed it — and nothing reloaded when they
 * changed organization.
 */
describe('AdminBlossomsView statement loading', () => {
  it('loads the statement as soon as an organization is selected, with no button press', async () => {
    renderAt('/admin/u1/blossoms')

    expect(fetchBlossomStatement).not.toHaveBeenCalled()
    await selectOrganization()

    await waitFor(() => {
      expect(fetchBlossomStatement).toHaveBeenCalledWith(
        ORG.id,
        expect.objectContaining({ page: 1 }),
      )
    })
  })

  it('renders the balance the server returned', async () => {
    fetchBlossomStatement.mockResolvedValue(statement({ closingBalance: 617.6 }))
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    expect(await screen.findByText(/617\.6/)).toBeInTheDocument()
  })

  it('reads its filters from the URL, so a filtered view is shareable', async () => {
    renderAt('/admin/u1/blossoms?kind=entitlement&q=goodwill&page=2&pageSize=25')
    await selectOrganization()

    await waitFor(() => {
      expect(fetchBlossomStatement).toHaveBeenCalledWith(
        ORG.id,
        expect.objectContaining({
          kind: 'entitlement',
          query: 'goodwill',
          page: 2,
          pageSize: 25,
        }),
      )
    })
  })
})

/**
 * `revoke` used to require a hand-typed ledger-entry GUID, and a grant that was not revocable was
 * only discovered by submitting and getting a `409`. The server now reports what is revocable, so
 * the page can offer the action on the row instead of asking for an identifier.
 */
describe('AdminBlossomsView grant operations', () => {
  const grant = {
    id: 'entry-1',
    occurredAt: '2026-09-05T10:00:00Z',
    kind: 'Entitlement',
    entryType: 'TopUpGrant',
    blossomDelta: 500,
    balanceAfter: 600,
    reason: 'Goodwill grant for the September outage.',
    sourceKind: 'Admin',
    sourceRef: null,
    expiresAt: null,
    createdByUserId: null,
    availableToRevoke: 500,
  }

  it('offers revoke inline on an eligible row, and never asks for a GUID', async () => {
    fetchBlossomStatement.mockResolvedValue(statement({ items: [grant], total: 1 }))
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    const table = await screen.findByRole('table')
    const row = within(table).getByText(/Goodwill grant/).closest('tr') as HTMLElement

    // The action is on the row it applies to.
    expect(within(row).getByRole('button', { name: /revoke/i })).toBeInTheDocument()
    // And the delivered "paste the ledger entry id" input is gone.
    expect(screen.queryByLabelText(/ledger entry id/i)).not.toBeInTheDocument()
  })

  it('sends the row identifier and an idempotency key when revoke is used', async () => {
    revokeBlossoms.mockResolvedValue({ data: {}, replayed: false })
    fetchBlossomStatement.mockResolvedValue(statement({ items: [grant], total: 1 }))
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    const user = userEvent.setup()
    const table = await screen.findByRole('table')
    const row = within(table).getByText(/Goodwill grant/).closest('tr') as HTMLElement
    await user.click(within(row).getByRole('button', { name: /revoke/i }))

    // Scoped to the dialog: the page also carries the credit/debit form, which has its own reason
    // field, and an unscoped query would match either.
    const dialog = await screen.findByRole('dialog', { name: /revoke grant/i })
    await user.type(within(dialog).getByLabelText(/reason/i), 'Reversing a duplicate grant.')
    await user.click(within(dialog).getByRole('button', { name: /confirm revoke/i }))

    await waitFor(() => {
      expect(revokeBlossoms).toHaveBeenCalledWith(
        ORG.id,
        expect.objectContaining({ ledgerEntryId: 'entry-1' }),
        expect.any(String),
      )
    })
  })

  it('offers no revoke control on a row that is not revocable', async () => {
    fetchBlossomStatement.mockResolvedValue(
      statement({
        items: [
          { ...grant, id: 'entry-2', entryType: 'PeriodAllocation', availableToRevoke: null },
        ],
        total: 1,
      }),
    )
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    const table = await screen.findByRole('table')
    const row = within(table).getByText(/Goodwill grant/).closest('tr') as HTMLElement

    expect(within(row).queryByRole('button', { name: /revoke/i })).not.toBeInTheDocument()
  })
})

/**
 * The honesty contract. An unchecked reconciliation must never read as consistent, and the
 * projection-derived opening balance has to be visible as such rather than implied away.
 */
describe('AdminBlossomsView reconciliation honesty', () => {
  it('says the status is unknown when the reconciliation was not checked', async () => {
    fetchBlossomStatement.mockResolvedValue(
      statement({
        dataQuality: {
          reconciliationChecked: false,
          openingBalanceFromProjection: true,
          windowCapped: false,
          maxWindowDays: 400,
          notes: [],
        },
      }),
    )
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    expect(await screen.findByText(/reconciliation status unknown/i)).toBeInTheDocument()
    expect(screen.queryByText(/reconciliation consistent/i)).not.toBeInTheDocument()
  })

  it('reports drift as the critical condition it is', async () => {
    fetchBlossomStatement.mockResolvedValue(
      statement({
        reconciliation: {
          projectedBalance: 617.6,
          ledgerDerivedBalance: 612.9,
          drift: 4.7,
          isConsistent: false,
        },
      }),
    )
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    expect(await screen.findByText(/drift detected/i)).toBeInTheDocument()
  })

  it('states that the opening balance comes from the projection', async () => {
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    expect(await screen.findByText(/projection/i)).toBeInTheDocument()
  })

  it('names the effective window cap rather than hardcoding one', async () => {
    fetchBlossomStatement.mockResolvedValue(statement({ maxWindowDays: 400 }))
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    expect(await screen.findByTestId('statement-window-cap')).toHaveTextContent('400')
  })
})

describe('AdminBlossomsView empty and error states', () => {
  it('states an empty window rather than rendering a blank table', async () => {
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    expect(await screen.findByText(/no ledger entries/i)).toBeInTheDocument()
  })

  it('renders the failure with a retry rather than an empty table', async () => {
    fetchBlossomStatement.mockRejectedValue(new Error('statement unavailable'))
    renderAt('/admin/u1/blossoms')
    await selectOrganization()

    expect(await screen.findByText(/could not be loaded/i)).toBeInTheDocument()
  })
})
