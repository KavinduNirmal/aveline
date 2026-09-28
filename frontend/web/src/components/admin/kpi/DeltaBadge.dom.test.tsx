import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { DeltaBadge } from './DeltaBadge'

/**
 * The period-over-period delta. `null` renders **nothing at all**, not `0%`: "no previous
 * window to compare against" is not "no change".
 */
describe('DeltaBadge', () => {
  it('renders a positive delta with an explicit plus sign', () => {
    render(<DeltaBadge delta={12.5} />)

    expect(screen.getByText('+12.5%')).toBeInTheDocument()
  })

  it('renders a negative delta with its sign', () => {
    render(<DeltaBadge delta={-8.1} />)

    expect(screen.getByText('-8.1%')).toBeInTheDocument()
  })

  it('renders a zero delta as 0%, because zero change is a measurement', () => {
    render(<DeltaBadge delta={0} />)

    expect(screen.getByText('0%')).toBeInTheDocument()
  })

  it('renders nothing for a null delta', () => {
    const { container } = render(<DeltaBadge delta={null} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing for an undefined delta', () => {
    const { container } = render(<DeltaBadge delta={undefined} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders nothing for a NaN delta', () => {
    const { container } = render(<DeltaBadge delta={Number.NaN} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('rounds to one decimal place', () => {
    render(<DeltaBadge delta={3.14159} />)

    expect(screen.getByText('+3.1%')).toBeInTheDocument()
  })

  it('carries a direction the surface can style, without a raw palette class', () => {
    const { container } = render(<DeltaBadge delta={5} />)
    const html = container.innerHTML

    expect(html).not.toMatch(/#[0-9a-fA-F]{3,8}\b/)
    expect(html).not.toMatch(/\b(text|bg|border)-(red|green|blue|slate|gray|zinc|amber)-\d{2,3}\b/)
  })
})
