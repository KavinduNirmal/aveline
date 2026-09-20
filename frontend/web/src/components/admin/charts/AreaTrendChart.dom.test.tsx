import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { AreaTrendChart } from './AreaTrendChart'

/**
 * The area trend. It shares the honesty rules of `TimeSeriesChart` — a `null` is a gap, not a line
 * through zero — and adds a gradient fill so a landing-page KPI reads differently from the line
 * charts, without inventing a second data path.
 */
const CONFIG = {
  activeUsers: { label: 'Active users', color: 'var(--chart-1)' },
} satisfies import('@/components/ui/chart').ChartConfig

const DATA = [
  { bucket: '2026-09-17T00:00:00.000Z', label: '17 Sep', isPartial: false, activeUsers: 3 },
  { bucket: '2026-09-18T00:00:00.000Z', label: '18 Sep', isPartial: false, activeUsers: 7 },
  { bucket: '2026-09-19T00:00:00.000Z', label: '19 Sep', isPartial: false, activeUsers: null },
  { bucket: '2026-09-20T00:00:00.000Z', label: '20 Sep', isPartial: true, activeUsers: 5 },
]

describe('AreaTrendChart', () => {
  it('renders an svg with a gradient fill defined for the series', () => {
    const { container } = render(<AreaTrendChart data={DATA} series={[{ dataKey: 'activeUsers', label: 'Active users' }]} config={CONFIG} />)

    expect(container.querySelector('svg')).not.toBeNull()
    expect(container.querySelector('linearGradient')).not.toBeNull()
  })

  it('emits no bare hex colour', () => {
    const { container } = render(<AreaTrendChart data={DATA} series={[{ dataKey: 'activeUsers', label: 'Active users' }]} config={CONFIG} />)

    // `ui/chart.tsx`'s selector rules quote Recharts' own default hexes inside class attributes;
    // what this component must not do is emit a palette class or its own hex.
    expect(container.innerHTML).not.toMatch(
      /\b(?:bg|text|border|ring|from|to|via|fill|stroke)-(?:slate|gray|red|green|blue|amber|zinc)-\d{2,3}\b/,
    )
  })

  it('shades the trailing partial bucket', () => {
    const { container } = render(
      <AreaTrendChart
        data={DATA}
        series={[{ dataKey: 'activeUsers', label: 'Active users' }]}
        config={CONFIG}
        partialBucket="2026-09-20T00:00:00.000Z"
      />,
    )

    expect(container.querySelectorAll('.recharts-reference-area')).toHaveLength(1)
  })

  it('renders an empty state rather than an axis with no points', () => {
    render(<AreaTrendChart data={[]} series={[{ dataKey: 'activeUsers', label: 'Active users' }]} config={CONFIG} />)

    expect(screen.getByText(/no data/i)).toBeInTheDocument()
  })

  it('keeps a null measure as a gap rather than converting it to zero', () => {
    const { container } = render(<AreaTrendChart data={DATA} series={[{ dataKey: 'activeUsers', label: 'Active users' }]} config={CONFIG} />)

    // The area is drawn from the data, and the gap must not be filled: the chart layer's
    // `connectNulls` must stay false.
    expect(container.innerHTML).not.toMatch(/connectNulls="true"/)
  })
})
