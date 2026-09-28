import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { SiteFooter } from './SiteFooter'

function renderFooter() {
  return render(
    <MemoryRouter>
      <SiteFooter />
    </MemoryRouter>,
  )
}

describe('SiteFooter', () => {
  it('links Privacy at the data policy, not at the terms anchor', () => {
    renderFooter()

    expect(screen.getByRole('link', { name: 'Privacy' })).toHaveAttribute('href', '/privacy')
  })

  it('leaves no link pointing at /terms#privacy for the policy', () => {
    renderFooter()

    const links = screen.getAllByRole('link') as HTMLAnchorElement[]
    expect(links.map((link) => link.getAttribute('href'))).not.toContain('/terms#privacy')
  })
})
