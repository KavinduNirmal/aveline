import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { PrivacySection } from './PrivacySection'

function renderSection() {
  return render(
    <MemoryRouter>
      <PrivacySection />
    </MemoryRouter>,
  )
}

describe('PrivacySection', () => {
  it('states the commitment in a headline', () => {
    renderSection()

    expect(
      screen.getByRole('heading', { name: /data, on their terms/i }),
    ).toBeInTheDocument()
  })

  it('carries the three commitments as a list, not prose', () => {
    renderSection()

    const items = screen.getAllByRole('listitem')
    expect(items).toHaveLength(3)
    expect(screen.getByText(/the truth, first/i)).toBeInTheDocument()
    expect(screen.getByText(/opt out in one tap/i)).toBeInTheDocument()
    expect(screen.getByText(/copy it, or erase it/i)).toBeInTheDocument()
  })

  it('links to the data policy', () => {
    renderSection()

    expect(screen.getByRole('link', { name: /read our data policy/i })).toHaveAttribute(
      'href',
      '/privacy',
    )
  })

  it('links to the consent flow explainer', () => {
    renderSection()

    expect(screen.getByRole('link', { name: /view consent flow/i })).toHaveAttribute(
      'href',
      '/privacy/consent-flow',
    )
  })

  it('does not claim Instagram is covered while no Instagram provider ships', () => {
    renderSection()

    expect(screen.queryByText(/instagram/i)).not.toBeInTheDocument()
  })
})
