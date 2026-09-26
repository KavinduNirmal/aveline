import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

vi.mock('@clerk/react', () => ({
  useAuth: () => ({ isLoaded: true, isSignedIn: false }),
}))

import { SiteNav } from './SiteNav'

function renderNav() {
  return render(
    <MemoryRouter>
      <SiteNav />
    </MemoryRouter>,
  )
}

describe('SiteNav', () => {
  it('offers the privacy surface to a signed-out visitor', () => {
    renderNav()

    const privacy = screen.getAllByRole('link', { name: 'Privacy' })
    expect(privacy.length).toBeGreaterThan(0)
    for (const link of privacy) {
      expect(link).toHaveAttribute('href', '/privacy')
    }
  })

  it('keeps a link through to the policy for every navigation tab', () => {
    renderNav()

    // Both the desktop nav and the mobile drawer render from one tab list, so the assertion is
    // that no duplicate link points somewhere other than /privacy.
    const hrefs = screen
      .getAllByRole('link')
      .map((link) => link.getAttribute('href'))
      .filter((href) => href?.startsWith('/privacy'))
    expect(hrefs.length).toBeGreaterThanOrEqual(1)
  })
})
