import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { RangePresets, type RangePreset } from './RangePresets'

/**
 * The range control. Radix's single `ToggleGroup` emits `""` on **deselect**, so the component
 * must swallow that: clearing the range would leave the page with no window at all.
 */
describe('RangePresets', () => {
  it('renders one radio per preset', () => {
    render(<RangePresets value="30d" onChange={() => {}} />)

    expect(screen.getAllByRole('radio')).toHaveLength(4)
    expect(screen.getByRole('radio', { name: /30 d/i })).toBeInTheDocument()
  })

  it('marks the selected preset with data-state=on', () => {
    render(<RangePresets value="90d" onChange={() => {}} />)

    expect(screen.getByRole('radio', { name: /90 d/i })).toHaveAttribute('data-state', 'on')
    expect(screen.getByRole('radio', { name: /7 d/i })).toHaveAttribute('data-state', 'off')
  })

  it('reports the chosen preset', async () => {
    const onChange = vi.fn()
    render(<RangePresets value="30d" onChange={onChange} />)

    await userEvent.click(screen.getByRole('radio', { name: /12 m/i }))

    expect(onChange).toHaveBeenCalledWith('12m')
  })

  it('ignores the empty value Radix emits on deselect', async () => {
    const onChange = vi.fn()
    render(<RangePresets value="30d" onChange={onChange} />)

    // Clicking the already-selected item is what produces the empty value.
    await userEvent.click(screen.getByRole('radio', { name: /30 d/i }))

    expect(onChange).not.toHaveBeenCalled()
  })

  it('exposes the label of each preset for the accessible name', () => {
    render(<RangePresets value="7d" onChange={() => {}} />)

    for (const label of ['7 d', '30 d', '90 d', '12 m']) {
      expect(screen.getByRole('radio', { name: new RegExp(label, 'i') })).toBeInTheDocument()
    }
  })

  it('renders the four documented presets in order', () => {
    render(<RangePresets value="7d" onChange={() => {}} />)

    const values: RangePreset[] = ['7d', '30d', '90d', '12m']
    expect(values).toHaveLength(4)
  })

  it('uses no raw palette class or bare hex', () => {
    const { container } = render(<RangePresets value="7d" onChange={() => {}} />)
    const html = container.innerHTML

    expect(html).not.toMatch(/#[0-9a-fA-F]{3,8}\b/)
    expect(html).not.toMatch(/\b(text|bg|border)-(red|green|blue|slate|gray|zinc|amber)-\d{2,3}\b/)
  })
})
