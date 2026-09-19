import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminPricingRulesView } from './AdminPricingRules'

const fetchPricingRules = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchPricingRules: (...args: unknown[]) => fetchPricingRules(...args),
  recomputePricingRule: vi.fn(),
}))

vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => ({ can: () => true, roles: ['owner'] }),
}))

const RULE = {
  id: 'rule-1',
  scopeKind: 'Global',
  provider: 'openai',
  model: 'gpt-4o',
  unitsPerBlossom: 1000,
  minimumChargeBlossoms: 1,
  roundingMode: 'Up',
  roundingDecimals: 2,
  effectiveFrom: '2026-01-01T00:00:00Z',
  effectiveTo: null,
  status: 'Active',
  version: 3,
  changeReason: 'Q1 review',
  createdByUserId: 'user-1',
  approvedByUserId: 'user-2',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
}

function renderView() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminPricingRulesView />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('AdminPricingRulesView', () => {
  it('renders a rule with its scope, window, status and version', async () => {
    fetchPricingRules.mockReset()
    fetchPricingRules.mockResolvedValue({ items: [RULE], total: 1, page: 1, pageSize: 50 })

    renderView()

    await waitFor(() => {
      expect(screen.getByText('gpt-4o')).toBeInTheDocument()
    })
    expect(screen.getByText(/openai/)).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
    expect(screen.getByText('v3')).toBeInTheDocument()
    // A null effectiveTo means the rule is still in force.
    expect(screen.getAllByText(/in force/i).length).toBeGreaterThan(0)
  })

  it('draws one timeline bar per rule, so overlapping windows are visible', async () => {
    fetchPricingRules.mockReset()
    fetchPricingRules.mockResolvedValue({
      items: [RULE, { ...RULE, id: 'rule-2', model: 'gpt-4o-mini', status: 'Scheduled' }],
      total: 2,
      page: 1,
      pageSize: 50,
    })

    renderView()

    await waitFor(() => {
      expect(screen.getAllByTestId('rule-timeline-bar')).toHaveLength(2)
    })
  })

  it('carries the legacy-formula banner, because a pricing write can change nothing', async () => {
    fetchPricingRules.mockReset()
    fetchPricingRules.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 50 })

    renderView()

    await waitFor(() => {
      expect(screen.getAllByText(/legacy formula/i).length).toBeGreaterThan(0)
    })
  })

  it('renders the paged envelope, not a bare array', async () => {
    fetchPricingRules.mockReset()
    fetchPricingRules.mockResolvedValue({ items: [RULE], total: 120, page: 1, pageSize: 50 })

    renderView()

    await waitFor(() => {
      expect(screen.getByText(/1–50 of 120/)).toBeInTheDocument()
    })
    const table = screen.getByRole('table')
    expect(within(table).getAllByRole('row').length).toBeGreaterThan(1)
  })

  it('renders the failure rather than a substitute rule', async () => {
    fetchPricingRules.mockReset()
    fetchPricingRules.mockRejectedValue(new Error('503'))

    renderView()

    await waitFor(() => {
      expect(screen.getByText(/pricing rules could not be loaded/i)).toBeInTheDocument()
    })
  })
})
