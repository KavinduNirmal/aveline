import { apiClient } from '@/lib/api'

export interface BusinessRuleResponseDto {
  id: string
  ruleName: string
  ruleType: string
  ruleValue: string
  isActive: boolean
  description: string | null
  createdAt: string
  updatedAt: string | null
}

export interface CreateBusinessRuleDto {
  ruleName: string
  ruleType: string
  ruleValue: string
  description?: string
  isActive?: boolean
}

export interface UpdateBusinessRuleDto {
  ruleName?: string
  ruleValue?: string
  description?: string
  isActive?: boolean
}

export interface EvaluateOrderRulesRequestDto {
  orderTotal: number
  subtotal: number
  totalCost: number
  discount: number
  customerLoyaltyTier?: string
}

export interface EvaluateOrderRulesResponseDto {
  requiresApproval: boolean
  approvalReason?: string
  ruleFlags: string[]
  triggeredRules: BusinessRuleResponseDto[]
}

const businessRulesBase = (organizationId: string) =>
  `/api/v1/orgs/${organizationId}/business-rules`

export async function fetchBusinessRules(
  organizationId: string,
  activeOnly: boolean = false,
  signal?: AbortSignal,
): Promise<BusinessRuleResponseDto[]> {
  const response = await apiClient.get<BusinessRuleResponseDto[]>(
    businessRulesBase(organizationId),
    {
      params: { activeOnly },
      signal,
    },
  )
  return response.data
}

export async function fetchBusinessRuleById(
  organizationId: string,
  ruleId: string,
  signal?: AbortSignal,
): Promise<BusinessRuleResponseDto> {
  const response = await apiClient.get<BusinessRuleResponseDto>(
    `${businessRulesBase(organizationId)}/${ruleId}`,
    { signal },
  )
  return response.data
}

export async function createBusinessRule(
  organizationId: string,
  dto: CreateBusinessRuleDto,
  signal?: AbortSignal,
): Promise<BusinessRuleResponseDto> {
  const response = await apiClient.post<BusinessRuleResponseDto>(
    businessRulesBase(organizationId),
    dto,
    { signal },
  )
  return response.data
}

export async function updateBusinessRule(
  organizationId: string,
  ruleId: string,
  dto: UpdateBusinessRuleDto,
  signal?: AbortSignal,
): Promise<BusinessRuleResponseDto> {
  const response = await apiClient.put<BusinessRuleResponseDto>(
    `${businessRulesBase(organizationId)}/${ruleId}`,
    dto,
    { signal },
  )
  return response.data
}

export async function deleteBusinessRule(
  organizationId: string,
  ruleId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.delete(`${businessRulesBase(organizationId)}/${ruleId}`, {
    signal,
  })
}

export async function evaluateBusinessRules(
  organizationId: string,
  request: EvaluateOrderRulesRequestDto,
  signal?: AbortSignal,
): Promise<EvaluateOrderRulesResponseDto> {
  const response = await apiClient.post<EvaluateOrderRulesResponseDto>(
    `${businessRulesBase(organizationId)}/evaluate`,
    request,
    { signal },
  )
  return response.data
}
