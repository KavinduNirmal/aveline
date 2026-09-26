import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { SourcingTab } from './SourcingTab'
import type { SourcingRequestMock } from './mockData'

// The archive confirmation carries the Undo that restores the ticket's own stage, so the toast is
// captured rather than mocked away.
const { toastSuccess } = vi.hoisted(() => ({ toastSuccess: vi.fn() }))

vi.mock('sonner', () => ({
  toast: Object.assign(vi.fn(), { success: toastSuccess, error: vi.fn(), info: vi.fn() }),
}))

/**
 * A board of bespoke commissions grows until it is unreadable, so a ticket folds and a finished
 * ticket leaves. These tests pin both, and the two rules that make archiving safe: an archived
 * ticket is off the board, and the Undo on the archive puts it back where it came from.
 */
function ticket(overrides: Partial<SourcingRequestMock> = {}): SourcingRequestMock {
  return {
    id: 'src-1',
    clientName: 'Samantha Arias',
    category: 'Sarees',
    color: 'Emerald',
    itemDescription: 'Bespoke emerald georgette saree with a hand-rolled edge',
    referenceImageUrl: '',
    supplierId: 'sup-1',
    supplierName: 'Partner Atelier',
    estimatedCost: 50000,
    proposedMarkup: 0.5,
    targetPrice: 75000,
    status: 'pending',
    createdAt: '2026-09-23T09:00:00.000Z',
    ...overrides,
  }
}

function renderTab(requests: SourcingRequestMock[], onUpdateStatus = vi.fn()) {
  render(
    <SourcingTab
      sourcingRequests={requests}
      suppliers={[]}
      onUpdateStatus={onUpdateStatus}
      onAddRequest={vi.fn()}
    />,
  )
  return { onUpdateStatus }
}

function cardFor(control: HTMLElement): HTMLElement {
  return control.closest('[data-slot="card"]') as HTMLElement
}

beforeEach(() => {
  toastSuccess.mockClear()
})

describe('sourcing board', () => {
  it('shows a ticket in its own stage and leaves the archived ones off the board', () => {
    renderTab([
      ticket({ id: 'src-pending', clientName: 'Pending Client' }),
      ticket({ id: 'src-quoted', clientName: 'Quoted Client', status: 'quoted' }),
      ticket({ id: 'src-archived', clientName: 'Archived Client', status: 'archived' }),
    ])

    expect(screen.getByText('Pending Client')).toBeInTheDocument()
    expect(screen.getByText('Quoted Client')).toBeInTheDocument()
    // Off the pipeline: it is not in a column, and it is not silently missing either.
    expect(screen.queryByText('Archived Client')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Archived (1)' })).toBeInTheDocument()
  })
})

describe('folding a ticket', () => {
  it('folds one card to its figures without touching the next', async () => {
    renderTab([
      ticket({ id: 'src-1', clientName: 'First Client' }),
      ticket({ id: 'src-2', clientName: 'Second Client' }),
    ])

    const foldFirst = screen.getByRole('button', {
      name: 'Fold the Sarees ticket for First Client',
    })
    const first = cardFor(foldFirst)
    const second = cardFor(
      screen.getByRole('button', { name: 'Fold the Sarees ticket for Second Client' }),
    )

    expect(within(first).getByText('Atelier Cost:')).toBeInTheDocument()

    await userEvent.click(foldFirst)

    // The detail is gone, and the two figures that decide whether to open it are not.
    expect(within(first).queryByText('Atelier Cost:')).not.toBeInTheDocument()
    expect(within(first).getByText('LKR 75,000.00')).toBeInTheDocument()
    expect(within(first).getByText('+50% margin')).toBeInTheDocument()
    expect(within(first).queryByRole('button', { name: /^Stage for/ })).not.toBeInTheDocument()
    // The neighbour is untouched.
    expect(within(second).getByText('Atelier Cost:')).toBeInTheDocument()
  })

  it('opens a folded card again', async () => {
    renderTab([ticket({ id: 'src-1', clientName: 'First Client' })])

    const fold = screen.getByRole('button', { name: 'Fold the Sarees ticket for First Client' })
    expect(fold).toHaveAttribute('aria-expanded', 'true')

    await userEvent.click(fold)

    // The control stays put and becomes the way back.
    const open = screen.getByRole('button', { name: 'Open the Sarees ticket for First Client' })
    expect(open).toHaveAttribute('aria-expanded', 'false')
    expect(within(cardFor(open)).queryByText('Atelier Cost:')).not.toBeInTheDocument()

    await userEvent.click(open)

    expect(within(cardFor(open)).getByText('Atelier Cost:')).toBeInTheDocument()
  })

  it('folds a whole column with one control, and opens it again', async () => {
    renderTab([
      ticket({ id: 'src-1', clientName: 'First Client' }),
      ticket({ id: 'src-2', clientName: 'Second Client' }),
      ticket({ id: 'src-3', clientName: 'Quoted Client', status: 'quoted' }),
    ])

    await userEvent.click(screen.getByRole('button', { name: 'Fold every ticket in Pending Quote' }))

    // Only the pending column folded; the quoted column is still whole.
    expect(screen.getAllByText('Atelier Cost:')).toHaveLength(1)

    const expand = screen.getByRole('button', { name: 'Expand every ticket in Pending Quote' })
    await userEvent.click(expand)

    expect(screen.getAllByText('Atelier Cost:')).toHaveLength(3)
  })
})

describe('archiving a ticket', () => {
  it('takes the ticket off the board through the status update', async () => {
    const { onUpdateStatus } = renderTab([ticket({ id: 'src-1', clientName: 'First Client' })])

    await userEvent.click(
      screen.getByRole('button', { name: 'Archive the Sarees ticket for First Client' }),
    )

    expect(onUpdateStatus).toHaveBeenCalledWith('src-1', 'archived')
  })

  it('offers an Undo that puts the ticket back in the stage it came from', async () => {
    const { onUpdateStatus } = renderTab([
      ticket({ id: 'src-1', clientName: 'First Client', category: 'Lehengas', status: 'approved' }),
    ])

    await userEvent.click(
      screen.getByRole('button', { name: 'Archive the Lehengas ticket for First Client' }),
    )

    const [, options] = toastSuccess.mock.calls[0] as [
      string,
      { action: { label: string; onClick: () => void } },
    ]
    expect(options.action.label).toBe('Undo')

    options.action.onClick()

    // Back to `approved`, not to the top of the pipeline: the Undo knows what it interrupted.
    expect(onUpdateStatus).toHaveBeenLastCalledWith('src-1', 'approved')
  })

  it('lists the archived tickets behind a toggle and restores one to the board', async () => {
    const { onUpdateStatus } = renderTab([
      ticket({ id: 'src-1', clientName: 'Archived Client', status: 'archived' }),
      ticket({ id: 'src-2', clientName: 'Live Client' }),
    ])

    await userEvent.click(screen.getByRole('button', { name: 'Archived (1)' }))

    const panel = screen.getByText('Archived tickets').closest('[data-slot="card"]') as HTMLElement
    expect(within(panel).getByText('Archived Client')).toBeInTheDocument()

    await userEvent.click(within(panel).getByRole('button', { name: 'Restore' }))

    // The old stage is not recorded, so a restore re-enters at the top rather than guessing one.
    expect(onUpdateStatus).toHaveBeenCalledWith('src-1', 'pending')
  })
})
