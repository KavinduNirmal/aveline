import { describe, expect, it, vi } from 'vitest'
import { apiClient } from './api'
import {
  createBusinessRule,
  deleteBusinessRule,
  evaluateBusinessRules,
  fetchBusinessRuleById,
  fetchBusinessRules,
  updateBusinessRule,
} from './business-rules-api'

describe('business-rules-api', () => {
  it('fetchBusinessRules queries business rules with activeOnly option', async () => {
    const mockRules = [{ id: 'rule-1', ruleName: 'High Value Threshold', isActive: true }]
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockRules })

    const result = await fetchBusinessRules('org-123', true)
    expect(getSpy).toHaveBeenCalledWith('/api/v1/orgs/org-123/business-rules', {
      params: { activeOnly: true },
      signal: undefined,
    })
    expect(result).toEqual(mockRules)
  })

  it('fetchBusinessRuleById queries specific rule', async () => {
    const mockRule = { id: 'rule-1', ruleName: 'Minimum Margin' }
    const getSpy = vi.spyOn(apiClient, 'get').mockResolvedValueOnce({ data: mockRule })

    const result = await fetchBusinessRuleById('org-123', 'rule-1')
    expect(getSpy).toHaveBeenCalledWith('/api/v1/orgs/org-123/business-rules/rule-1', {
      signal: undefined,
    })
    expect(result).toEqual(mockRule)
  })

  it('createBusinessRule posts new rule', async () => {
    const newRuleDto = {
      ruleName: 'VIP Discount Limit',
      ruleType: 'DiscountLimit',
      ruleValue: '10',
      isActive: true,
    }
    const createdRule = { id: 'rule-2', ...newRuleDto }
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: createdRule })

    const result = await createBusinessRule('org-123', newRuleDto)
    expect(postSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules',
      newRuleDto,
      { signal: undefined },
    )
    expect(result).toEqual(createdRule)
  })

  it('updateBusinessRule puts updated rule fields', async () => {
    const updateDto = { ruleValue: '45000', isActive: true }
    const updatedRule = { id: 'rule-1', ruleValue: '45000', isActive: true }
    const putSpy = vi.spyOn(apiClient, 'put').mockResolvedValueOnce({ data: updatedRule })

    const result = await updateBusinessRule('org-123', 'rule-1', updateDto)
    expect(putSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules/rule-1',
      updateDto,
      { signal: undefined },
    )
    expect(result).toEqual(updatedRule)
  })

  it('deleteBusinessRule calls delete endpoint', async () => {
    const deleteSpy = vi.spyOn(apiClient, 'delete').mockResolvedValueOnce({})

    await deleteBusinessRule('org-123', 'rule-1')
    expect(deleteSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules/rule-1',
      { signal: undefined },
    )
  })

  it('evaluateBusinessRules posts evaluation request', async () => {
    const req = {
      orderTotal: 50000,
      subtotal: 50000,
      totalCost: 35000,
      discount: 0,
      customerLoyaltyTier: 'VIP',
    }
    const evalResponse = {
      requiresApproval: true,
      approvalReason: 'Order exceeds threshold',
      ruleFlags: ['HIGH_VALUE_THRESHOLD_EXCEEDED'],
      triggeredRules: [],
    }
    const postSpy = vi.spyOn(apiClient, 'post').mockResolvedValueOnce({ data: evalResponse })

    const result = await evaluateBusinessRules('org-123', req)
    expect(postSpy).toHaveBeenCalledWith(
      '/api/v1/orgs/org-123/business-rules/evaluate',
      req,
      { signal: undefined },
    )
    expect(result).toEqual(evalResponse)
  })
})
