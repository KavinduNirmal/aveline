import { describe, expect, it, vi } from 'vitest'
import { apiClient } from './api'
import {
  deletePieceDiscount,
  fetchPieceDiscountByItemId,
  fetchPieceDiscounts,
  setPieceDiscount,
} from './piece-discount-api'

describe('piece-discount-api', () => {
  it('fetchPieceDiscounts queries all active piece discounts for an org', async () => {
    const mockDiscounts = [
      {
        ruleId: 'rule-1',
        itemId: 'item-101',
        itemName: 'Crimson Georgette Zari Saree',
        discountPercentage: 0.10,
        isActive: true,
        description: 'Mid-season saree promo',
        createdAt: '2026-09-28T00:00:00Z',
        updatedAt: null,
      },
    ]
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockDiscounts })

    const result = await fetchPieceDiscounts('org-123')
    expect(getSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules/piece-discounts',
      { signal: undefined },
    )
    expect(result).toEqual(mockDiscounts)
  })

  it('fetchPieceDiscountByItemId returns discount when present', async () => {
    const mockDiscount = {
      ruleId: 'rule-1',
      itemId: 'item-101',
      itemName: 'Crimson Georgette Zari Saree',
      discountPercentage: 0.10,
      isActive: true,
      description: null,
      createdAt: '2026-09-28T00:00:00Z',
      updatedAt: null,
    }
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockDiscount })

    const result = await fetchPieceDiscountByItemId('org-123', 'item-101')
    expect(getSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules/piece-discounts/item-101',
      { signal: undefined },
    )
    expect(result).toEqual(mockDiscount)
  })

  it('fetchPieceDiscountByItemId returns null on 404', async () => {
    vi.spyOn(apiClient, 'get').mockRejectedValueOnce({
      response: { status: 404 },
    })

    const result = await fetchPieceDiscountByItemId('org-123', 'non-existent')
    expect(result).toBeNull()
  })

  it('setPieceDiscount posts payload to create or update piece discount', async () => {
    const payload = {
      itemId: 'item-101',
      itemName: 'Crimson Georgette Zari Saree',
      discountPercentage: 0.10,
      description: 'Mid-season saree promo',
    }
    const mockCreated = {
      ruleId: 'rule-1',
      ...payload,
      isActive: true,
      createdAt: '2026-09-28T00:00:00Z',
      updatedAt: null,
    }
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: mockCreated })

    const result = await setPieceDiscount('org-123', payload)
    expect(postSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules/piece-discounts',
      payload,
      { signal: undefined },
    )
    expect(result).toEqual(mockCreated)
  })

  it('deletePieceDiscount calls DELETE endpoint', async () => {
    const deleteSpy = vi.spyOn(apiClient, 'delete').mockResolvedValueOnce({})

    await deletePieceDiscount('org-123', 'item-101')
    expect(deleteSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules/piece-discounts/item-101',
      { signal: undefined },
    )
  })
})
