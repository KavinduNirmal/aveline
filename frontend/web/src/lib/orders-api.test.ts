import { describe, expect, it, vi } from 'vitest'
import { apiClient } from './api'
import {
  cancelOrder,
  fetchOrderById,
  fetchOrders,
  recalculateOrder,
  updateOrderStatus,
} from './orders-api'

describe('orders-api', () => {
  it('fetchOrders queries orders endpoint with parameters', async () => {
    const mockData = { items: [], page: 1, pageSize: 20, total: 0 }
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockData })

    const result = await fetchOrders('org-123', {
      status: 'pending_approval',
      page: 2,
      pageSize: 10,
    })

    expect(getSpy).toHaveBeenCalledWith('/api/v1/orgs/org-123/orders', {
      params: {
        status: 'pending_approval',
        customerId: undefined,
        orderType: undefined,
        fromDate: undefined,
        toDate: undefined,
        page: 2,
        pageSize: 10,
      },
      signal: undefined,
    })
    expect(result).toEqual(mockData)
  })

  it('fetchOrderById queries specific order', async () => {
    const mockOrder = { id: 'order-1', total: 5000 }
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockOrder })

    const result = await fetchOrderById('org-123', 'order-1')
    expect(getSpy).toHaveBeenCalledWith('/api/v1/orgs/org-123/orders/order-1', {
      signal: undefined,
    })
    expect(result).toEqual(mockOrder)
  })

  it('updateOrderStatus calls patch status', async () => {
    const patchSpy = vi.spyOn(apiClient, 'patch').mockResolvedValueOnce({ data: { status: 'confirmed' } })

    const result = await updateOrderStatus('org-123', 'order-1', 'confirmed')
    expect(patchSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/orders/order-1/status',
      { status: 'confirmed' },
      { signal: undefined },
    )
    expect(result.status).toBe('confirmed')
  })

  it('cancelOrder calls post cancel with optional reason', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: { success: true, message: 'Cancelled' },
    })

    const result = await cancelOrder('org-123', 'order-1', 'Customer changed mind')
    expect(postSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/orders/order-1/cancel',
      {},
      {
        params: { reason: 'Customer changed mind' },
        signal: undefined,
      },
    )
    expect(result.success).toBe(true)
  })

  it('recalculateOrder calls recalculate endpoint', async () => {
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({
      data: { id: 'order-1', margin: 25.5 },
    })

    const result = await recalculateOrder('org-123', 'order-1')
    expect(postSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/orders/order-1/recalculate',
      {},
      { signal: undefined },
    )
    expect(result.margin).toBe(25.5)
  })
})
