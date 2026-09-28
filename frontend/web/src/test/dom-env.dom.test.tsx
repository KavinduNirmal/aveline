import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

/**
 * The A0 harness assertion. Without a DOM project every admin UI test would have to be a
 * string-rendering test, which cannot assert what is absent from the DOM — and the
 * absence assertions are the ones the overhaul's acceptance tests depend on.
 */
function Probe(): React.JSX.Element {
  return <div data-testid="probe">admin-dom-ok</div>
}

describe('the jsdom test project', () => {
  it('provides a document and renders React through Testing Library', () => {
    expect(typeof document).not.toBe('undefined')
    render(<Probe />)
    expect(screen.getByTestId('probe')).toBeInTheDocument()
  })

  it('provides the jest-dom matchers', () => {
    render(<Probe />)
    expect(screen.getByText('admin-dom-ok')).toBeVisible()
  })
})
