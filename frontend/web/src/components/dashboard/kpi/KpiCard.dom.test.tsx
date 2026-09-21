import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { KpiCard } from './KpiCard'

/**
 * The distinction this component exists to make: **`null` is not `0`.**
 *
 * `UsagePanel` used to compute `usage?.blossomUsed ?? 0` and render "0 used" for a period the API had
 * not measured. On a dashboard, an invented zero reads as a real measurement, which is the exact
 * failure the whole tenant truthfulness contract exists to prevent.
 */
describe('KpiCard', () => {
  it('renders a measured value', () => {
    render(<KpiCard label="Gross order value" value={42000} />)

    expect(screen.getByText('Gross order value')).toBeInTheDocument()
    expect(screen.getByText(/42,000\.00/)).toBeInTheDocument()
  })

  it('renders a measured zero as zero, not as "not measured"', () => {
    render(<KpiCard label="Orders" value={0} format="count" />)

    // No orders is a real measurement: the shop genuinely took none.
    expect(screen.getByText('0')).toBeInTheDocument()
    expect(screen.queryByText(/not measured/i)).not.toBeInTheDocument()
  })

  it('renders null as "not measured" and never as zero', () => {
    render(<KpiCard label="Average order value" value={null} />)

    expect(screen.getByText(/not measured/i)).toBeInTheDocument()
    expect(screen.queryByText(/0\.00/)).not.toBeInTheDocument()
  })

  it('renders undefined the same way as null', () => {
    render(<KpiCard label="Margin" value={undefined} />)

    expect(screen.getByText(/not measured/i)).toBeInTheDocument()
  })

  it('says why the figure is missing when it can', () => {
    render(
      <KpiCard
        label="Margin"
        value={null}
        note="No orders carry a cost yet, so a margin cannot be computed."
      />,
    )

    expect(screen.getByText(/no orders carry a cost yet/i)).toBeInTheDocument()
  })

  it('renders a caveat badge when the figure is less trustworthy', () => {
    render(
      <KpiCard
        label="Margin"
        value={6000}
        caveat="cost data incomplete"
        note="One contributing order has no wholesale cost."
      />,
    )

    // The figure is still shown, with the reason it may be wrong beside it.
    expect(screen.getByText(/6,000\.00/)).toBeInTheDocument()
    expect(screen.getByText('cost data incomplete')).toBeInTheDocument()
    expect(screen.getByText(/no wholesale cost/i)).toBeInTheDocument()
  })

  it('formats a percentage', () => {
    render(<KpiCard label="Margin %" value={40} format="percent" />)

    expect(screen.getByText('40%')).toBeInTheDocument()
  })

  it('groups a count with thousands separators', () => {
    render(<KpiCard label="Clients" value={1200} format="count" />)

    expect(screen.getByText(/1,200/)).toBeInTheDocument()
  })
})
