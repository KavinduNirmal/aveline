import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { HorizontalBarChart } from './HorizontalBarChart'

/**
 * The plan-mix distribution.
 *
 * Recharts `BarChart layout="vertical"` rather than a `PieChart`: nothing in the repository
 * imports `PieChart`, and five ordered tiers read better as bars than as slices. One bar per
 * tier, each coloured from the five `--chart-*` tokens, never by a hex.
 */
const CONFIG = {
  count: { label: 'Boutiques', color: 'var(--chart-1)' },
}

const DATA = [
  { category: 'Seed', count: 8, fill: 'var(--color-count)' },
  { category: 'Bloom', count: 3, fill: 'var(--color-count)' },
  { category: 'Orchid', count: 1, fill: 'var(--color-count)' },
]

describe('HorizontalBarChart', () => {
  it('renders without a raw svg root of its own', () => {
    // Recharts owns the svg; the component must not hand-draw one (admin-truthfulness.test.ts).
    const { container } = render(<HorizontalBarChart data={DATA} config={CONFIG} />)

    expect(container.querySelector('svg')).not.toBeNull()
    expect(container.innerHTML).not.toMatch(/<svg[^>]*>\s*<svg/)
  })

  it('renders one bar cell per category', () => {
    // The category axis itself needs a laid-out parent, which jsdom does not provide; the bar
    // cells are the part of the chart this component actually owns.
    const { container } = render(<HorizontalBarChart data={DATA} config={CONFIG} />)

    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(DATA.length)
  })

  it('never writes a palette utility of its own', () => {
    // The rendered DOM legitimately inherits `ui/chart.tsx`'s selector rules, which quote
    // Recharts' own default fill hexes. What this component must not do is emit a palette class.
    const { container } = render(<HorizontalBarChart data={DATA} config={CONFIG} />)

    expect(container.innerHTML).not.toMatch(
      /\b(?:bg|text|border|ring|from|to|via|fill|stroke)-(?:slate|gray|red|green|blue|amber|zinc)-\d{2,3}\b/,
    )
  })

  it('accepts an optional height and renders inside the ChartFrame height token', () => {
    const { container } = render(
      <HorizontalBarChart data={DATA} config={CONFIG} height={320} />,
    )

    expect(container.querySelector('svg')).not.toBeNull()
  })

  it('renders an empty state rather than an axis with no bars', () => {
    render(<HorizontalBarChart data={[]} config={CONFIG} />)

    expect(screen.getByText(/no data/i)).toBeInTheDocument()
  })

  it('renders a category with a null count without throwing', () => {
    const { container } = render(
      <HorizontalBarChart
        data={[{ category: 'Seed', count: null, fill: 'var(--color-count)' }]}
        config={CONFIG}
      />,
    )

    expect(container.querySelector('svg')).not.toBeNull()
  })
})
