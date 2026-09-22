import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { FloorTagStudio } from './FloorTagStudio'

function renderStudio(overrides: Partial<React.ComponentProps<typeof FloorTagStudio>> = {}) {
  return render(
    <FloorTagStudio
      organizationId="11111111-1111-1111-1111-111111111111"
      itemId="item-1"
      sku="AVL-001"
      name="Emerald Silk Saree"
      price="1250"
      category="Sarees"
      color="Emerald Green"
      fabric="Pure Mulberry Silk"
      {...overrides}
    />,
  )
}

describe('FloorTagStudio', () => {
  it('prices the tag in the dashboard currency, never in dollars', () => {
    // The tenant surface has one money formatter and it reports LKR. The tag used to print a bare
    // `$1,250`, which quoted the piece in a currency the server never sent.
    renderStudio()

    expect(screen.getByText(/1,250\.00/)).toBeInTheDocument()
    expect(screen.queryByText(/\$/)).not.toBeInTheDocument()
  })

  it('says "not measured" for a missing price instead of printing a zero', () => {
    renderStudio({ price: '' })

    expect(screen.getByText(/not measured/i)).toBeInTheDocument()
  })

  it('treats a missing organisation as an absent field, not a fabricated tenant', () => {
    // F-9: with no boutique id there is no tenant to generate a QR against. The downloads are
    // disabled and the tag says why; printing still works.
    renderStudio({ organizationId: null })

    expect(screen.getByRole('button', { name: /png/i })).toBeDisabled()
    expect(screen.getByRole('button', { name: /svg/i })).toBeDisabled()
    expect(screen.getByText(/downloads need the boutique id/i)).toBeInTheDocument()
  })

  it('lets the operator choose what the tag encodes', () => {
    renderStudio()

    expect(screen.getByRole('radio', { name: /structured json/i })).toBeChecked()
    expect(screen.getByRole('radio', { name: /raw sku/i })).toBeInTheDocument()
  })
})
