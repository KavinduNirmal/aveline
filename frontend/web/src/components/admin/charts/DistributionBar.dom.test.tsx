import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import type { ChartConfig } from '@/components/ui/chart'

import { DistributionBar } from './DistributionBar'

/**
 * The plan-mix distribution as a single stacked bar: one row, one segment per plan tier, so the
 * free-versus-premium split is readable at a glance on the landing page.
 *
 * It takes **counts**, not percentages, and derives the proportions itself, so a tier with no
 * measurable value is named rather than silently omitted from the total.
 */
const CONFIG = {
  Seed: { label: 'Seed', color: 'var(--chart-4)' },
  Bloom: { label: 'Bloom', color: 'var(--chart-1)' },
  Orchid: { label: 'Orchid', color: 'var(--chart-2)' },
} satisfies ChartConfig

const SEGMENTS = [
  { key: 'Seed', label: 'Seed', value: 8, free: true },
  { key: 'Bloom', label: 'Bloom', value: 3, free: false },
  { key: 'Orchid', label: 'Orchid', value: 1, free: false },
]

describe('DistributionBar', () => {
  it('renders one stacked bar segment per tier', () => {
    const { container } = render(<DistributionBar segments={SEGMENTS} config={CONFIG} />)

    expect(container.querySelectorAll('.recharts-bar-rectangle')).toHaveLength(3)
  })

  it('names the total and the proportion of each side, once', () => {
    const { container } = render(<DistributionBar segments={SEGMENTS} config={CONFIG} />)

    // 8 of 12 free, 4 of 12 premium.
    expect(container.textContent).toContain('12 organizations')
    expect(container.textContent).toContain('67% free (8)')
    expect(container.textContent).toContain('33% premium (4)')
    expect(screen.getAllByText(/67%/)).toHaveLength(1)
  })

  it('renders a legend entry, with its count, for every tier it was given', () => {
    render(<DistributionBar segments={SEGMENTS} config={CONFIG} />)

    for (const segment of SEGMENTS) {
      expect(screen.getByText(segment.label)).toBeInTheDocument()
      // The count sits beside the label as its own node.
      expect(screen.getByText(String(segment.value))).toBeInTheDocument()
    }
  })

  it('shows an empty state rather than a zero-width bar when there is nothing to divide', () => {
    render(<DistributionBar segments={[]} config={CONFIG} />)

    expect(screen.getByText(/no data/i)).toBeInTheDocument()
  })

  it('says so rather than dividing by zero when every segment is zero', () => {
    render(
      <DistributionBar
        segments={[
          { key: 'Seed', label: 'Seed', value: 0, free: true },
          { key: 'Bloom', label: 'Bloom', value: 0, free: false },
        ]}
        config={CONFIG}
      />,
    )

    expect(screen.getByText(/no organizations/i)).toBeInTheDocument()
  })

  it('emits no raw palette utility', () => {
    const { container } = render(<DistributionBar segments={SEGMENTS} config={CONFIG} />)

    expect(container.innerHTML).not.toMatch(
      /\b(?:bg|text|border|ring|from|to|via|fill|stroke)-(?:slate|gray|red|green|blue|amber|zinc)-\d{2,3}\b/,
    )
  })
})
