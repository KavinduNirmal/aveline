import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import type { BlossomStatementItem } from '@/types/admin'
import { BalanceTimeline } from './BalanceTimeline'

function item(overrides: Partial<BlossomStatementItem> = {}): BlossomStatementItem {
  return {
    id: 'entry-1',
    occurredAt: '2026-09-05T10:00:00Z',
    kind: 'Entitlement',
    entryType: 'TopUpGrant',
    blossomDelta: 500,
    balanceAfter: 600,
    reason: 'A grant with a long enough reason.',
    sourceKind: 'Admin',
    sourceRef: null,
    expiresAt: null,
    createdByUserId: null,
    ...overrides,
  }
}

/**
 * The balance over the window, on one axis.
 *
 * The table states each row's balance but not the shape of the movement, so a discontinuity is
 * something an operator has to infer by reading down a column of numbers. The timeline makes it
 * visible, which is the difference between noticing drift and being told about it.
 */
describe('BalanceTimeline', () => {
  it('draws one mark per row, in the order the server returned them', () => {
    render(
      <BalanceTimeline
        items={[
          item({ id: 'a', balanceAfter: 600 }),
          item({ id: 'b', balanceAfter: 597 }),
          item({ id: 'c', balanceAfter: 597 }),
        ]}
      />,
    )

    expect(screen.getAllByTestId('timeline-mark')).toHaveLength(3)
  })

  it('states the window it covers, from the first and last balances', () => {
    render(
      <BalanceTimeline
        items={[
          item({ id: 'a', balanceAfter: 600 }),
          item({ id: 'b', balanceAfter: 450 }),
        ]}
      />,
    )

    expect(screen.getByText(/600/)).toBeInTheDocument()
    expect(screen.getByText(/450/)).toBeInTheDocument()
  })

  it('states an empty window rather than rendering a bare axis', () => {
    render(<BalanceTimeline items={[]} />)

    expect(screen.getByText(/no movement in this window/i)).toBeInTheDocument()
    expect(screen.queryAllByTestId('timeline-mark')).toHaveLength(0)
  })

  it('marks a discontinuity so a jump is not read as a slope', () => {
    // A period allowance lands every month; drawing it as ordinary movement would hide that the
    // balance steps rather than drifts.
    render(
      <BalanceTimeline
        items={[
          item({ id: 'a', entryType: 'PeriodAllocation', balanceAfter: 1500 }),
          item({ id: 'b', entryType: 'AdminDebit', balanceAfter: 100 }),
        ]}
      />,
    )

    expect(screen.getByTestId('timeline-step')).toBeInTheDocument()
  })

  it('has no step marker when the movement is smooth', () => {
    render(
      <BalanceTimeline
        items={[
          item({ id: 'a', entryType: 'AdminDebit', balanceAfter: 600 }),
          item({ id: 'b', entryType: 'AdminDebit', balanceAfter: 597 }),
        ]}
      />,
    )

    expect(screen.queryByTestId('timeline-step')).not.toBeInTheDocument()
  })
})
