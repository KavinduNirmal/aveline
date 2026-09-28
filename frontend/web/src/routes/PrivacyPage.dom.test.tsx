import { render, screen } from '@testing-library/react'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@clerk/react', () => ({
  useAuth: () => ({ isLoaded: true, isSignedIn: false }),
}))

import { PrivacyPage } from './PrivacyPage'

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/privacy" element={<PrivacyPage />} />
        <Route path="/privacy/consent-flow" element={<div>CONSENT_FLOW</div>} />
        <Route path="/privacy/opt-out" element={<div>OPT_OUT</div>} />
        <Route path="*" element={<div>OTHER</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

/** The policy a WhatsApp disclosure links to must be a full notice, not the one-line anchor. */
describe('PrivacyPage', () => {
  it('renders the living data policy at /privacy', () => {
    renderAt('/privacy')

    expect(screen.getByRole('heading', { level: 1, name: /data policy/i })).toBeInTheDocument()
  })

  it('states who is the controller and who is the processor (Q-8)', () => {
    renderAt('/privacy')

    expect(screen.getByText(/your boutique is the controller/i)).toBeInTheDocument()
    expect(screen.getByText(/aveline is the processor/i)).toBeInTheDocument()
  })

  it('says plainly that Instagram is not covered, without promising parity (Q-5)', () => {
    renderAt('/privacy')

    expect(screen.getByText(/instagram is not yet covered/i)).toBeInTheDocument()

    const pageText = document.body.textContent ?? ''
    expect(pageText).not.toMatch(/instagram parity/i)
    expect(pageText).not.toMatch(/the same on instagram/i)
    expect(pageText).not.toMatch(/instagram is covered/i)
  })

  it('names the consent states and links to the explainer', () => {
    renderAt('/privacy')

    expect(screen.getByText(/pending/i)).toBeInTheDocument()
    expect(screen.getByText(/granted/i)).toBeInTheDocument()
    expect(screen.getByText(/revoked/i)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: /consent flow/i })).toHaveAttribute(
      'href',
      '/privacy/consent-flow',
    )
  })

  it('links the opt-out flow a customer can run without an account', () => {
    renderAt('/privacy')

    const optOutLinks = screen.getAllByRole('link', { name: /opt[- ]out/i })
    expect(optOutLinks).toHaveLength(1)
    expect(optOutLinks[0]).toHaveAttribute('href', '/privacy/opt-out')
  })

  it('reads the boutique slug the disclosure appended as ?org=', () => {
    renderAt('/privacy?org=house-of-fashions')

    expect(screen.getByText(/house-of-fashions/)).toBeInTheDocument()
  })

  it('does not invent a boutique when the link carries no slug', () => {
    renderAt('/privacy')

    expect(screen.queryByText(/house-of-fashions/)).not.toBeInTheDocument()
  })
})
