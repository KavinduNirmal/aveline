import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminOrgsView } from './AdminOrgs'

const searchAdminOrganizations = vi.fn()
const setEntitlementOverrides = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  searchAdminOrganizations: (...args: unknown[]) => searchAdminOrganizations(...args),
  setEntitlementOverrides: (...args: unknown[]) => setEntitlementOverrides(...args),
}))

const ORG = {
  id: 'org-1',
  name: 'Aveline Boutique',
  slug: 'aveline-boutique',
  clerkOrgId: 'org_clerk_1',
  ownerUserId: 'user-1',
  planTier: 'Bloom',
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-02T00:00:00Z',
}

async function openOverrideDialog() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminOrgsView />
      </MemoryRouter>
    </QueryClientProvider>,
  )
  await waitFor(() => {
    expect(screen.getByText('Aveline Boutique')).toBeInTheDocument()
  })
  await userEvent.click(screen.getByRole('button', { name: /entitlements/i }))
  await userEvent.type(screen.getByPlaceholderText(/reason for audit log/i), 'contract term')
}

describe('AdminOrgsView entitlement overrides', () => {
  it('renders a reload-and-retry affordance for a 409 override-overlap', async () => {
    searchAdminOrganizations.mockReset()
    setEntitlementOverrides.mockReset()
    searchAdminOrganizations.mockResolvedValue({ items: [ORG], page: 1, pageSize: 50, total: 1 })
    setEntitlementOverrides.mockRejectedValue({ status: 409, code: 'override-overlap' })

    await openOverrideDialog()
    await userEvent.click(screen.getByRole('button', { name: /apply override/i }))

    await waitFor(() => {
      expect(screen.getAllByText(/reload and retry/i).length).toBeGreaterThan(0)
    })
    // The two failures must not be conflated: an overlap is not a field error.
    expect(screen.queryByText(/must be an integer/i)).toBeNull()
  })

  it('renders a field-level error for a 400 validation problem', async () => {
    searchAdminOrganizations.mockReset()
    setEntitlementOverrides.mockReset()
    searchAdminOrganizations.mockResolvedValue({ items: [ORG], page: 1, pageSize: 50, total: 1 })
    setEntitlementOverrides.mockRejectedValue({
      status: 400,
      message: 'Validation failed',
      errors: { 'overrides[0].value': ['Value must be an integer for this key.'] },
    })

    await openOverrideDialog()
    await userEvent.click(screen.getByRole('button', { name: /apply override/i }))

    await waitFor(() => {
      expect(screen.getByText(/must be an integer/i)).toBeInTheDocument()
    })
    expect(screen.queryByText(/reload and retry/i)).toBeNull()
  })

  it('sends the valueType the key implies, not a hard-coded boolean', async () => {
    searchAdminOrganizations.mockReset()
    setEntitlementOverrides.mockReset()
    searchAdminOrganizations.mockResolvedValue({ items: [ORG], page: 1, pageSize: 50, total: 1 })
    setEntitlementOverrides.mockResolvedValue({})

    await openOverrideDialog()
    await userEvent.click(screen.getByRole('button', { name: /apply override/i }))

    await waitFor(() => {
      expect(setEntitlementOverrides).toHaveBeenCalled()
    })
    const [, request] = setEntitlementOverrides.mock.calls[0]
    // The default key is `feature:custom_styling`, so this one *is* boolean — but the value
    // must be the JSON boolean, not the form string.
    expect(request.overrides[0].valueType).toBe('Boolean')
    expect(request.overrides[0].value).toBe(true)
    expect(typeof request.overrides[0].value).toBe('boolean')
  })
})
