import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@clerk/react', () => ({
  useAuth: () => ({ isLoaded: true, isSignedIn: false }),
}))

import { ConsentFlowPage } from './ConsentFlowPage'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/privacy/consent-flow" element={<ConsentFlowPage />} />
        <Route path="/privacy/opt-out" element={<div>OPT_OUT</div>} />
        <Route path="*" element={<div>OTHER</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

/** The explainer the disclosure may point at: readable with no account and no boutique session. */
describe('ConsentFlowPage', () => {
  it('renders at /privacy/consent-flow without an account', () => {
    renderAt('/privacy/consent-flow')

    expect(
      screen.getByRole('heading', { level: 1, name: /consent, in plain language/i }),
    ).toBeInTheDocument()
  })

  it('walks the three customer-facing states in order: pending, granted, revoked', () => {
    renderAt('/privacy/consent-flow')

    const stateHeadings = screen
      .getAllByRole('heading', { level: 3 })
      .map((heading) => heading.textContent?.trim())

    expect(stateHeadings.slice(0, 3)).toEqual(['Pending', 'Granted', 'Revoked'])
  })

  it('explains that a revoked customer can grant consent again', () => {
    renderAt('/privacy/consent-flow')

    expect(screen.getByRole('heading', { level: 3, name: /^Re-grant$/i })).toBeInTheDocument()
  })

  it('says what each state means for processing', () => {
    renderAt('/privacy/consent-flow')

    expect(screen.getByText(/nothing is processed until consent is granted/i)).toBeInTheDocument()
    expect(screen.getByText(/messages are answered and remembered/i)).toBeInTheDocument()
    expect(screen.getByText(/processing stops and existing entries are not used/i)).toBeInTheDocument()
  })

  it('links to the live opt-out page and the data policy', () => {
    renderAt('/privacy/consent-flow')

    expect(screen.getByRole('link', { name: /open the opt-out page/i })).toHaveAttribute(
      'href',
      '/privacy/opt-out',
    )
    expect(screen.getByRole('link', { name: /data policy/i })).toHaveAttribute('href', '/privacy')
  })
})
