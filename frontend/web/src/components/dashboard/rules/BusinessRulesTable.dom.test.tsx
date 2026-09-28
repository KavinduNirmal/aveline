import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const createBusinessRule = vi.fn()
const updateBusinessRule = vi.fn()
const deleteBusinessRule = vi.fn()

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}))

vi.mock('@/lib/business-rules-api', async () => {
  const actual =
    await vi.importActual<typeof import('@/lib/business-rules-api')>('@/lib/business-rules-api')
  return {
    ...actual,
    createBusinessRule: (...args: unknown[]) => createBusinessRule(...args),
    updateBusinessRule: (...args: unknown[]) => updateBusinessRule(...args),
    deleteBusinessRule: (...args: unknown[]) => deleteBusinessRule(...args),
  }
})

import { BusinessRulesTable } from './BusinessRulesTable'
import type { BusinessRuleResponseDto } from '@/lib/business-rules-api'

const ORG = '11111111-1111-1111-1111-111111111111'

function rule(overrides: Partial<BusinessRuleResponseDto> = {}): BusinessRuleResponseDto {
  return {
    id: 'rule-1',
    ruleName: 'High value orders',
    ruleType: 'approval_threshold',
    // The evaluator parses ruleValue as JSON and reads a named property; a bare number is inert.
    ruleValue: '{"threshold":40000}',
    isActive: true,
    description: 'Orders over LKR 40,000 need approval',
    createdAt: '2026-09-20T10:00:00Z',
    updatedAt: null,
    ...overrides,
  }
}

function renderTable(
  props: Partial<ComponentProps<typeof BusinessRulesTable>> = {},
) {
  const onReload = vi.fn().mockResolvedValue(undefined)
  render(
    <BusinessRulesTable
      organizationId={ORG}
      rules={[rule()]}
      isLoading={false}
      canManage
      onReload={onReload}
      {...props}
    />,
  )
  return { onReload }
}

describe('BusinessRulesTable', () => {
  beforeEach(() => {
    createBusinessRule.mockReset().mockResolvedValue(rule())
    updateBusinessRule.mockReset().mockResolvedValue(rule())
    deleteBusinessRule.mockReset().mockResolvedValue(undefined)
  })

  it('creates a rule with the canonical type and a JSON payload the evaluator reads', async () => {
    const user = userEvent.setup()
    renderTable()

    await user.click(screen.getByRole('button', { name: /add rule/i }))
    await user.type(screen.getByLabelText(/rule name/i), 'Big spenders')
    await user.type(screen.getByLabelText(/value \/ limit/i), '75000')
    await user.click(screen.getByRole('button', { name: /create rule/i }))

    await waitFor(() => expect(createBusinessRule).toHaveBeenCalledTimes(1))
    expect(createBusinessRule).toHaveBeenCalledWith(ORG, {
      ruleName: 'Big spenders',
      // `highvaluethreshold` is not a value the service's switch matches; `approval_threshold` is.
      ruleType: 'approval_threshold',
      ruleValue: '{"threshold":75000}',
      description: undefined,
      isActive: true,
    })
  })

  it('leaves a non-numeric value as typed rather than inventing a number', async () => {
    const user = userEvent.setup()
    renderTable()

    await user.click(screen.getByRole('button', { name: /add rule/i }))
    await user.type(screen.getByLabelText(/rule name/i), 'Manual note')
    await user.type(screen.getByLabelText(/value \/ limit/i), 'see policy doc')
    await user.click(screen.getByRole('button', { name: /create rule/i }))

    await waitFor(() => expect(createBusinessRule).toHaveBeenCalledTimes(1))
    expect(createBusinessRule.mock.calls[0][1]).toMatchObject({ ruleValue: 'see policy doc' })
  })

  it('reads a stored minimum-margin payload back into the field and re-serialises it', async () => {
    const user = userEvent.setup()
    renderTable({ rules: [rule({ ruleType: 'min_margin', ruleValue: '{"min_margin":0.25}' })] })

    await user.click(screen.getByRole('button', { name: /edit/i }))
    const valueInput = screen.getByLabelText(/threshold \/ value/i)
    expect(valueInput).toHaveValue('0.25')

    await user.clear(valueInput)
    await user.type(valueInput, '0.3')
    await user.click(screen.getByRole('button', { name: /save changes/i }))

    await waitFor(() => expect(updateBusinessRule).toHaveBeenCalledTimes(1))
    expect(updateBusinessRule).toHaveBeenCalledWith(ORG, 'rule-1', {
      ruleValue: '{"min_margin":0.3}',
      description: 'Orders over LKR 40,000 need approval',
    })
  })

  it('maps a discount rule to the max_discount property the evaluator reads', async () => {
    const user = userEvent.setup()
    renderTable({ rules: [rule({ ruleType: 'discount', ruleValue: '{"max_discount":0.15}' })] })

    await user.click(screen.getByRole('button', { name: /edit/i }))
    const valueInput = screen.getByLabelText(/threshold \/ value/i)
    expect(valueInput).toHaveValue('0.15')

    await user.clear(valueInput)
    await user.type(valueInput, '0.2')
    await user.click(screen.getByRole('button', { name: /save changes/i }))

    await waitFor(() => expect(updateBusinessRule).toHaveBeenCalledTimes(1))
    expect(updateBusinessRule.mock.calls[0][2]).toMatchObject({
      ruleValue: '{"max_discount":0.2}',
    })
  })

  it('falls back to the raw payload when a stored value is not the JSON it claims to be', async () => {
    const user = userEvent.setup()
    renderTable({ rules: [rule({ ruleType: 'high_value', ruleValue: 'not json' })] })

    await user.click(screen.getByRole('button', { name: /edit/i }))
    expect(screen.getByLabelText(/threshold \/ value/i)).toHaveValue('not json')
  })

  it('toggles a rule without touching its type or value', async () => {
    const user = userEvent.setup()
    const { onReload } = renderTable()

    await user.click(screen.getByRole('switch', { name: /toggle high value orders/i }))

    await waitFor(() => expect(updateBusinessRule).toHaveBeenCalledTimes(1))
    expect(updateBusinessRule).toHaveBeenCalledWith(ORG, 'rule-1', { isActive: false })
    expect(onReload).toHaveBeenCalled()
  })

  it('deletes a rule', async () => {
    const user = userEvent.setup()
    renderTable()

    await user.click(screen.getByRole('button', { name: /delete/i }))

    await waitFor(() => expect(deleteBusinessRule).toHaveBeenCalledWith(ORG, 'rule-1'))
  })

  it('offers no mutating affordance to a role without orders:manage', () => {
    renderTable({ canManage: false })

    expect(screen.queryByRole('button', { name: /add rule/i })).toBeNull()
    expect(screen.queryByRole('button', { name: /edit/i })).toBeNull()
    expect(screen.getByText(/view only/i)).toBeInTheDocument()
    expect(screen.getByRole('switch', { name: /toggle high value orders/i })).toBeDisabled()
  })

  it('says so when no rules are configured', () => {
    renderTable({ rules: [] })
    expect(screen.getByText(/no business rules configured yet/i)).toBeInTheDocument()
  })
})
