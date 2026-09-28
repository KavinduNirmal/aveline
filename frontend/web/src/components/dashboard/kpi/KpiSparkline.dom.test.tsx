import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { KpiSparkline, canDrawSparkline, type KpiSparklinePoint } from './KpiSparkline'

/**
 * The sparkline is a trend glyph, so every test here is about what it must **not** claim.
 *
 * A series the server measured nothing in draws no line at all; a bucket it measured nothing in
 * stays a gap; and the glyph never grows axes, a grid or a tooltip, because a reader must not be
 * invited to take a precise value off a six-rem picture.
 */
const MEASURED: KpiSparklinePoint[] = [
  { bucketStart: '2026-09-01T00:00:00.000Z', value: 1200 },
  { bucketStart: '2026-09-02T00:00:00.000Z', value: 1800 },
  { bucketStart: '2026-09-03T00:00:00.000Z', value: 1500 },
]

const WITH_GAP: KpiSparklinePoint[] = [
  { bucketStart: '2026-09-01T00:00:00.000Z', value: 1200 },
  { bucketStart: '2026-09-02T00:00:00.000Z', value: null },
  { bucketStart: '2026-09-03T00:00:00.000Z', value: 1500 },
]

const ALL_NULL: KpiSparklinePoint[] = [
  { bucketStart: '2026-09-01T00:00:00.000Z', value: null },
  { bucketStart: '2026-09-02T00:00:00.000Z', value: null },
]

function draw(points: KpiSparklinePoint[]) {
  return render(
    <KpiSparkline points={points} label="Gross order value" colorToken="var(--chart-1)" />,
  )
}

describe('KpiSparkline', () => {
  it('renders an area with a gradient for a measured series', () => {
    const { container } = draw(MEASURED)

    expect(container.querySelector('svg')).not.toBeNull()
    expect(container.querySelector('linearGradient')).not.toBeNull()
    expect(container.querySelector('.recharts-area')).not.toBeNull()
  })

  it('keeps a null bucket as a gap rather than drawing through it', () => {
    const { container } = draw(WITH_GAP)

    // The shared constant must stay false: a line drawn through the gap reports a measurement the
    // server never produced.
    expect(container.innerHTML).not.toMatch(/connectNulls="true"/)
  })

  it('states "not measured" rather than rendering an empty axis when no bucket was measured', () => {
    const { container } = draw(ALL_NULL)

    expect(screen.getByText(/not measured in this window/i)).toBeInTheDocument()
    // No chart at all. This also pins the `0×0` trap: prose inside `ChartContainer` collapses.
    expect(container.querySelector('svg')).toBeNull()
  })

  it('renders the empty state for an empty series', () => {
    const { container } = draw([])

    expect(screen.getByText(/not measured in this window/i)).toBeInTheDocument()
    expect(container.querySelector('svg')).toBeNull()
  })

  it('refuses to draw a trend from a single measured bucket', () => {
    // With `connectNulls` false, a measured bucket whose neighbours are both null is a run of one
    // point: recharts emits `M5,22.789L5,31Z`, a zero-length segment whose stroke is `none`. The
    // area would occupy the box and paint nothing — an empty box claiming nothing happened.
    const { container } = draw([
      { bucketStart: '2026-09-01T00:00:00.000Z', value: null },
      { bucketStart: '2026-09-02T00:00:00.000Z', value: 1200 },
      { bucketStart: '2026-09-03T00:00:00.000Z', value: null },
    ])

    expect(screen.getByText(/not measured in this window/i)).toBeInTheDocument()
    expect(container.querySelector('svg')).toBeNull()
  })

  it('reports whether a series can carry a trend line at all', () => {
    // The threshold is exported so a caller can withhold a caption about a glyph it is not drawing.
    expect(canDrawSparkline([])).toBe(false)
    expect(canDrawSparkline(ALL_NULL)).toBe(false)
    expect(canDrawSparkline([{ bucketStart: '2026-09-02T00:00:00.000Z', value: 1200 }])).toBe(false)
    expect(canDrawSparkline(MEASURED)).toBe(true)
  })

  it('draws no axes, grid or tooltip', () => {
    const { container } = draw(MEASURED)

    // Pins the decision so a later "improvement" cannot quietly turn the glyph into a chart.
    expect(container.querySelector('.recharts-cartesian-axis')).toBeNull()
    expect(container.querySelector('.recharts-cartesian-grid')).toBeNull()
    expect(container.querySelector('.recharts-tooltip-wrapper')).toBeNull()
  })

  it('treats a measured zero as a measurement, not as a gap', () => {
    const { container } = draw([
      { bucketStart: '2026-09-01T00:00:00.000Z', value: 0 },
      { bucketStart: '2026-09-02T00:00:00.000Z', value: 0 },
    ])

    // A flat run at zero is a real measurement: the shop genuinely took nothing.
    expect(screen.queryByText(/not measured in this window/i)).not.toBeInTheDocument()
    expect(container.querySelector('svg')).not.toBeNull()
  })

  it('describes the trend in words so it is not sight-only', () => {
    const { container } = draw(MEASURED)

    const described = container.querySelector('[role="img"]')
    expect(described).not.toBeNull()
    expect(described?.getAttribute('aria-label')).toMatch(/gross order value trend: rising/i)
  })

  it('describes a falling series and a flat one', () => {
    const falling = draw([
      { bucketStart: '2026-09-01T00:00:00.000Z', value: 900 },
      { bucketStart: '2026-09-02T00:00:00.000Z', value: 400 },
    ])
    expect(falling.container.querySelector('[role="img"]')?.getAttribute('aria-label')).toMatch(
      /falling over the period/i,
    )

    const flat = draw([
      { bucketStart: '2026-09-01T00:00:00.000Z', value: 400 },
      { bucketStart: '2026-09-02T00:00:00.000Z', value: 400 },
    ])
    expect(flat.container.querySelector('[role="img"]')?.getAttribute('aria-label')).toMatch(
      /flat over the period/i,
    )
  })

  it('describes the period without inventing a date when a bucket start is unparseable', () => {
    // `Intl.DateTimeFormat.format` throws on a non-finite date, so this path must not reach it.
    const { container } = draw([
      { bucketStart: 'not-a-date', value: 100 },
      { bucketStart: '2026-09-02T00:00:00.000Z', value: 200 },
    ])

    const label = container.querySelector('[role="img"]')?.getAttribute('aria-label')
    expect(label).toContain('not-a-date')
    expect(label).toMatch(/rising over the period/i)
  })
})
