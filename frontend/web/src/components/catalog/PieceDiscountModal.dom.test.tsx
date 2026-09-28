import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { PieceDiscountModal } from './PieceDiscountModal'
import * as pieceDiscountApi from '@/lib/piece-discount-api'

describe('PieceDiscountModal', () => {
  const defaultProps = {
    isOpen: true,
    onClose: vi.fn(),
    organizationId: 'org-123',
    itemId: 'item-1',
    itemName: 'Crimson Georgette Zari Saree',
    retailPrice: 1250,
    wholesaleCost: 550,
    currentDiscount: null,
    onDiscountSaved: vi.fn(),
  }

  it('renders modal with retail price and default 10% preset breakdown', () => {
    render(<PieceDiscountModal {...defaultProps} />)

    expect(screen.getByText('Allocate Piece Discount')).toBeInTheDocument()
    expect(screen.getByDisplayValue('10')).toBeInTheDocument()
    expect(screen.getByText(/Margin Safe/i)).toBeInTheDocument()
  })

  it('updates commercial preview when preset button is clicked', async () => {
    const user = userEvent.setup()
    render(<PieceDiscountModal {...defaultProps} />)

    const preset20 = screen.getByRole('button', { name: '20%' })
    await user.click(preset20)

    expect(screen.getByDisplayValue('20')).toBeInTheDocument()
  })

  it('calls setPieceDiscount and onDiscountSaved on save', async () => {
    const user = userEvent.setup()
    const setSpy = vi.spyOn(pieceDiscountApi, 'setPieceDiscount').mockResolvedValueOnce({
      ruleId: 'rule-new',
      itemId: 'item-1',
      itemName: 'Crimson Georgette Zari Saree',
      discountPercentage: 0.15,
      isActive: true,
      description: 'Holiday special',
      createdAt: '2026-09-28T00:00:00Z',
      updatedAt: null,
    })

    const onDiscountSaved = vi.fn()
    const onClose = vi.fn()

    render(
      <PieceDiscountModal
        {...defaultProps}
        onDiscountSaved={onDiscountSaved}
        onClose={onClose}
      />,
    )

    const preset15 = screen.getByRole('button', { name: '15%' })
    await user.click(preset15)

    const saveButton = screen.getByRole('button', { name: /save discount/i })
    await user.click(saveButton)

    await waitFor(() => {
      expect(setSpy).toHaveBeenCalledWith('org-123', {
        itemId: 'item-1',
        itemName: 'Crimson Georgette Zari Saree',
        discountPercentage: 0.15,
        description: undefined,
      })
      expect(onDiscountSaved).toHaveBeenCalledWith(
        expect.objectContaining({
          ruleId: 'rule-new',
          discountPercentage: 0.15,
        }),
      )
      expect(onClose).toHaveBeenCalled()
    })
  })

  it('renders remove button when existing discount is active and calls delete on click', async () => {
    const user = userEvent.setup()
    const deleteSpy = vi.spyOn(pieceDiscountApi, 'deletePieceDiscount').mockResolvedValueOnce()
    const onDiscountSaved = vi.fn()
    const onClose = vi.fn()

    const existingDiscount = {
      ruleId: 'rule-existing',
      itemId: 'item-1',
      itemName: 'Crimson Georgette Zari Saree',
      discountPercentage: 0.10,
      isActive: true,
      description: 'Existing promo',
      createdAt: '2026-09-28T00:00:00Z',
      updatedAt: null,
    }

    render(
      <PieceDiscountModal
        {...defaultProps}
        currentDiscount={existingDiscount}
        onDiscountSaved={onDiscountSaved}
        onClose={onClose}
      />,
    )

    const removeBtn = screen.getByRole('button', { name: /remove discount/i })
    expect(removeBtn).toBeInTheDocument()

    await user.click(removeBtn)

    await waitFor(() => {
      expect(deleteSpy).toHaveBeenCalledWith('org-123', 'item-1')
      expect(onDiscountSaved).toHaveBeenCalledWith(null)
      expect(onClose).toHaveBeenCalled()
    })
  })
})
