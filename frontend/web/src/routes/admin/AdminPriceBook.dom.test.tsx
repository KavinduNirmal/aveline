import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminPriceBookView } from './AdminPriceBook'

const fetchPriceBook = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  fetchPriceBook: (...args: unknown[]) => fetchPriceBook(...args),
}))

const ENTRY = {
  id: 'entry-1',
  planTier: 'Bloom',
  organizationId: null,
  skuKind: 'Blossom',
  skuCode: 'BL-100',
  blossomQuantity: 100,
  priceLkr: 2500,
  effectiveFrom: '2026-01-01T00:00:00Z',
  effectiveTo: null,
  status: 'Active',
  changeReason: 'Q1 pricing review',
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
}

function renderView() {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter>
        <AdminPriceBookView />
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

/**
 * `GET /admin/pricing/price-book` is a **bare unpaginated array** (rule 9), so the page must not
 * send paging parameters and must not render a pager.
 */
describe('AdminPriceBookView', () => {
  it('renders a resolved price entry with its window and status', async () => {
    fetchPriceBook.mockReset()
    fetchPriceBook.mockResolvedValue([ENTRY])

    renderView()

    await waitFor(() => {
      expect(screen.getByText('BL-100')).toBeInTheDocument()
    })
    expect(screen.getByText('Bloom')).toBeInTheDocument()
    expect(screen.getByText('2,500')).toBeInTheDocument()
    expect(screen.getByText('Active')).toBeInTheDocument()
    // A null effectiveTo means "still in force", not a blank cell.
    expect(screen.getByText(/in force/i)).toBeInTheDocument()
  })

  it('does not send paging parameters', async () => {
    fetchPriceBook.mockReset()
    fetchPriceBook.mockResolvedValue([ENTRY])

    renderView()

    await waitFor(() => {
      expect(fetchPriceBook).toHaveBeenCalled()
    })
    expect(fetchPriceBook).toHaveBeenCalledWith({})
  })

  it('renders an empty state rather than a blank table', async () => {
    fetchPriceBook.mockReset()
    fetchPriceBook.mockResolvedValue([])

    renderView()

    await waitFor(() => {
      expect(screen.getByText(/no price book entries/i)).toBeInTheDocument()
    })
  })

  it('renders the failure rather than a substitute price', async () => {
    fetchPriceBook.mockReset()
    fetchPriceBook.mockRejectedValue(new Error('503'))

    renderView()

    await waitFor(() => {
      expect(screen.getByText(/price book could not be loaded/i)).toBeInTheDocument()
    })
  })
})
