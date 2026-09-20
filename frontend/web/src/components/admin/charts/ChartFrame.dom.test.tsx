import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { ChartFrame } from './ChartFrame'

/**
 * The frame's two jobs: give a chart a sized parent, and own the states that are **not** charts.
 *
 * The second one was found the hard way. `ResponsiveContainer` measures its parent and renders what
 * it measured into a plain wrapper — so a message placed inside it collapses to `0×0` whenever the
 * measurement has not resolved, and wraps one character per line. The frame renders `error` and
 * `empty` itself, at the card's full width, and never mounts the chart container for them.
 */
const CONFIG = { value: { label: 'Value', color: 'var(--chart-1)' } }

describe('ChartFrame', () => {
  it('renders the chart in a sized parent when the state is ready', () => {
    const { container } = render(
      <ChartFrame title="A chart" config={CONFIG}>
        <div data-testid="chart-body" />
      </ChartFrame>,
    )

    expect(screen.getByTestId('chart-body')).toBeInTheDocument()
    expect(container.querySelector('[data-slot="chart"]')).not.toBeNull()
  })

  it('renders an error message at full width, outside the chart container', () => {
    const { container } = render(
      <ChartFrame
        title="A chart"
        config={CONFIG}
        state="error"
        stateMessage="The requested resource was not found."
      >
        <div data-testid="chart-body" />
      </ChartFrame>,
    )

    const alert = screen.getByRole('alert')
    expect(alert.className).toContain('w-full')
    // The regression: the chart container must not be mounted for a failed read, because anything
    // inside it lands in recharts' measured zero-sized box.
    expect(container.querySelector('[data-slot="chart"]')).toBeNull()
    expect(screen.queryByTestId('chart-body')).toBeNull()
  })

  it('does not double the full stop when the message already ends in one', () => {
    const { container } = render(
      <ChartFrame title="A chart" config={CONFIG} state="error" stateMessage="Not found.">
        <div />
      </ChartFrame>,
    )

    expect(container.textContent).toContain('Not found.')
    expect(container.textContent).not.toContain('Not found..')
  })

  it('adds a full stop when the message has none', () => {
    const { container } = render(
      <ChartFrame title="A chart" config={CONFIG} state="error" stateMessage="Not found">
        <div />
      </ChartFrame>,
    )

    expect(container.textContent).toContain('Not found.')
  })

  it('renders an empty state as a message rather than a zero-axis chart', () => {
    const { container } = render(
      <ChartFrame title="A chart" config={CONFIG} state="empty">
        <div data-testid="chart-body" />
      </ChartFrame>,
    )

    expect(container.textContent).toMatch(/nothing to show/i)
    expect(container.textContent).toMatch(/a blank is not a zero/i)
    expect(container.querySelector('[data-slot="chart"]')).toBeNull()
  })

  it('names the title and the description it was given', () => {
    render(
      <ChartFrame title="Business actions logged" description="Count of write operations" config={CONFIG}>
        <div />
      </ChartFrame>,
    )

    expect(screen.getByText('Business actions logged')).toBeInTheDocument()
    expect(screen.getByText('Count of write operations')).toBeInTheDocument()
  })
})
