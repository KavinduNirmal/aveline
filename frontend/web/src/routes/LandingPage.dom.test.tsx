import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@clerk/react', () => ({
  useAuth: () => ({ isLoaded: true, isSignedIn: false }),
}))

// The heavy, animated children are not what this test is about; stubbing them keeps the assertion
// on the one thing that matters, which is where the privacy section sits in the narrative.
vi.mock('@/components/site/AuroraField', () => ({ AuroraField: () => <div /> }))
vi.mock('@/components/site/HeroSlideshow', () => ({ HeroSlideshow: () => <div /> }))
vi.mock('@/components/site/SocialProofBar', () => ({ SocialProofBar: () => <div /> }))
vi.mock('@/components/site/TestimonialsSection', () => ({ TestimonialsSection: () => <div /> }))
vi.mock('@/components/site/PhoneMockup', () => ({ PhoneMockup: () => <div /> }))

import { LandingPage } from './LandingPage'

beforeEach(() => {
  sessionStorage.setItem('aveline:alpha-dialog-seen', '1')
})

describe('LandingPage — the privacy section', () => {
  it('renders the privacy commitment on the public landing page', () => {
    render(
      <MemoryRouter>
        <LandingPage />
      </MemoryRouter>,
    )

    expect(screen.getByRole('heading', { name: /data, on their terms/i })).toBeInTheDocument()
  })

  it('places it after the problem section, so the commitment answers the problem', () => {
    render(
      <MemoryRouter>
        <LandingPage />
      </MemoryRouter>,
    )

    const problem = screen.getByText(/slipping through the cracks/i)
    const privacy = screen.getByRole('heading', { name: /data, on their terms/i })

    const position = problem.compareDocumentPosition(privacy)
    expect(position & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(position & Node.DOCUMENT_POSITION_PRECEDING).toBeFalsy()
  })
})
