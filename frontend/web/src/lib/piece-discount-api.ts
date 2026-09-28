import { apiClient } from '@/lib/api'

export interface PieceDiscountResponseDto {
  ruleId: string
  itemId: string
  itemName: string
  discountPercentage: number
  isActive: boolean
  description: string | null
  createdAt: string
  updatedAt: string | null
}

export interface SetPieceDiscountPayload {
  itemId: string
  itemName: string
  discountPercentage: number
  description?: string | null
}

const pieceDiscountsBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/business-rules/piece-discounts`

export async function fetchPieceDiscounts(
  organizationId: string,
  signal?: AbortSignal,
): Promise<PieceDiscountResponseDto[]> {
  const response = await apiClient.get<PieceDiscountResponseDto[]>(
    pieceDiscountsBase(organizationId),
    { signal },
  )
  return response.data
}

export async function fetchPieceDiscountByItemId(
  organizationId: string,
  itemId: string,
  signal?: AbortSignal,
): Promise<PieceDiscountResponseDto | null> {
  try {
    const response = await apiClient.get<PieceDiscountResponseDto>(
      `${pieceDiscountsBase(organizationId)}/${itemId}`,
      { signal },
    )
    return response.data
  } catch (error: any) {
    if (error?.response?.status === 404) {
      return null
    }
    throw error
  }
}

export async function setPieceDiscount(
  organizationId: string,
  payload: SetPieceDiscountPayload,
  signal?: AbortSignal,
): Promise<PieceDiscountResponseDto> {
  const response = await apiClient.post<PieceDiscountResponseDto>(
    pieceDiscountsBase(organizationId),
    payload,
    { signal },
  )
  return response.data
}

export async function deletePieceDiscount(
  organizationId: string,
  itemId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.delete(`${pieceDiscountsBase(organizationId)}/${itemId}`, {
    signal,
  })
}
