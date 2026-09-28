import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { OrgPicker } from './OrgPicker'

const searchAdminOrganizations = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  searchAdminOrganizations: (...args: unknown[]) => searchAdminOrganizations(...args),
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

function renderPicker(onSelect = vi.fn()) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  render(
    <QueryClientProvider client={queryClient}>
      <OrgPicker value={null} onSelect={onSelect} />
    </QueryClientProvider>,
  )
  return onSelect
}

/**
 * The delivered console made an operator paste an organization GUID by hand; this is the plan's
 * replacement.
 */
describe('OrgPicker', () => {
  it('does not search until the term is meaningful', async () => {
    searchAdminOrganizations.mockReset()
    renderPicker()

    await userEvent.type(screen.getByLabelText(/organization/i), 'a')
    expect(searchAdminOrganizations).not.toHaveBeenCalled()
  })

  it('searches by term and reports the selected organization, not a bare id', async () => {
    searchAdminOrganizations.mockReset()
    searchAdminOrganizations.mockResolvedValue({ items: [ORG], page: 1, pageSize: 10, total: 1 })
    const onSelect = renderPicker()

    await userEvent.type(screen.getByLabelText(/organization/i), 'avel')

    await waitFor(() => {
      expect(searchAdminOrganizations).toHaveBeenCalledWith(
        expect.objectContaining({ q: 'avel' }),
      )
    })
    const option = await screen.findByRole('option', { name: /aveline boutique/i })
    await userEvent.click(option)
    expect(onSelect).toHaveBeenCalledWith(ORG)
  })

  it('says so when nothing matches rather than showing an empty box', async () => {
    searchAdminOrganizations.mockReset()
    searchAdminOrganizations.mockResolvedValue({ items: [], page: 1, pageSize: 10, total: 0 })
    renderPicker()

    await userEvent.type(screen.getByLabelText(/organization/i), 'zzz')

    await waitFor(() => {
      expect(screen.getByText(/no organizations match/i)).toBeInTheDocument()
    })
  })
})
