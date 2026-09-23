import { apiClient } from '@/lib/api'

export interface OrderItemDto {
  id: string
  orderId: string
  catalogItemId: string
  title: string
  quantity: number
  unitPrice: number
  unitCost: number
  totalPrice: number
  totalCost: number
  margin: number
}

export interface OrderResponseDto {
  id: string
  organizationId: string
  customerId: string
  customerName: string
  orderType: string
  status: string
  subtotal: number
  discount: number
  total: number
  totalCost: number
  margin: number
  createdBy: string | null
  createdAt: string
  updatedAt: string | null
  items: OrderItemDto[]
}

export interface PagedOrders {
  items: OrderResponseDto[]
  page: number
  pageSize: number
  total: number
}

export interface OrderQueryParameters {
  status?: string
  customerId?: string
  orderType?: string
  fromDate?: string
  toDate?: string
  page?: number
  pageSize?: number
}

export interface UpdateOrderStatusPayload {
  status: string
}

const ordersBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/orders`

export async function fetchOrders(
  organizationId: string,
  query: OrderQueryParameters = {},
  signal?: AbortSignal,
): Promise<PagedOrders> {
  const response = await apiClient.get<PagedOrders>(ordersBase(organizationId), {
    params: {
      status: query.status || undefined,
      customerId: query.customerId || undefined,
      orderType: query.orderType || undefined,
      fromDate: query.fromDate || undefined,
      toDate: query.toDate || undefined,
      page: query.page ?? 1,
      pageSize: query.pageSize ?? 20,
    },
    signal,
  })
  return response.data
}

export async function fetchOrderById(
  organizationId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<OrderResponseDto> {
  const response = await apiClient.get<OrderResponseDto>(
    `${ordersBase(organizationId)}/${orderId}`,
    { signal },
  )
  return response.data
}

export async function updateOrderStatus(
  organizationId: string,
  orderId: string,
  status: string,
  signal?: AbortSignal,
): Promise<OrderResponseDto> {
  const response = await apiClient.patch<OrderResponseDto>(
    `${ordersBase(organizationId)}/${orderId}/status`,
    { status },
    { signal },
  )
  return response.data
}

export async function cancelOrder(
  organizationId: string,
  orderId: string,
  reason?: string,
  signal?: AbortSignal,
): Promise<{ success: boolean; message: string }> {
  const response = await apiClient.post<{ success: boolean; message: string }>(
    `${ordersBase(organizationId)}/${orderId}/cancel`,
    {},
    {
      params: { reason: reason || undefined },
      signal,
    },
  )
  return response.data
}

export async function recalculateOrder(
  organizationId: string,
  orderId: string,
  signal?: AbortSignal,
): Promise<OrderResponseDto> {
  const response = await apiClient.post<OrderResponseDto>(
    `${ordersBase(organizationId)}/${orderId}/recalculate`,
    {},
    { signal },
  )
  return response.data
}
