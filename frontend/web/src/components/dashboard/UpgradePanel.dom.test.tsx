import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it } from 'vitest'

import { UpgradePanel } from './UpgradePanel'
import type { OrganizationProfileDto } from '@/types/organization'

const ORG = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'Aveline Colombo 07',
  slug: 'aveline-colombo-07',
  clerkOrgId: null,
  ownerUserId: 'u-1',
  address: null,
  phoneNumber: null,
  description: null,
  logoUrl: null,
  planTier: 'Bloom',
} as never as OrganizationProfileDto

function renderPanel() {
  return render(
    <MemoryRouter>
      <UpgradePanel organization={ORG} />
    </MemoryRouter>,
  )
}

describe('UpgradePanel', () => {
  it('names the plan the boutique is on', () => {
    renderPanel()
    expect(screen.getByRole('heading', { name: /upgrade/i })).toBeInTheDocument()
    expect(screen.getByText(/Bloom/)).toBeInTheDocument()
  })

  it('sends the owner to the contact page rather than pretending to check out', () => {
    // The upgrade itself is deferred and no payment provider is connected, so the only honest
    // affordance is a conversation. A "Confirm upgrade" button here would charge nobody and
    // change nothing.
    renderPanel()
    const contact = screen.getByRole('link', { name: /contact us/i })
    expect(contact).toHaveAttribute('href', '/contact')
  })

  it('says the change is arranged with Aveline, not applied instantly', () => {
    renderPanel()
    expect(screen.getByText(/arranged with us/i)).toBeInTheDocument()
  })
})
