import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { StrictMode } from 'react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchCustomerBook = vi.fn()
const fetchCustomer = vi.fn()
const fetchCustomerInteractions = vi.fn()
const deleteCustomer = vi.fn()
const updateCustomer = vi.fn()
const recordCustomerInteraction = vi.fn()

vi.mock('@/lib/customers-api', () => ({
  fetchCustomerBook: (...args: unknown[]) => fetchCustomerBook(...args),
  fetchCustomer: (...args: unknown[]) => fetchCustomer(...args),
  fetchCustomerInteractions: (...args: unknown[]) => fetchCustomerInteractions(...args),
  deleteCustomer: (...args: unknown[]) => deleteCustomer(...args),
  updateCustomer: (...args: unknown[]) => updateCustomer(...args),
  recordCustomerInteraction: (...args: unknown[]) => recordCustomerInteraction(...args),
  createWalkInCustomer: vi.fn(),
}))

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}))

import { ApiError } from '@/lib/api-error'

import { CustomersPanel } from './CustomersPanel'

const ORG = {
  id: '11111111-1111-1111-1111-111111111111',
  name: 'House of Fashions',
  slug: 'house-of-fashions',
  clerkOrgId: null,
  ownerUserId: 'u-1',
  address: null,
  phoneNumber: null,
  description: null,
  logoUrl: null,
  planTier: 'Bloom',
  hasCompletedOnboarding: true,
  createdAt: '2026-01-01T00:00:00Z',
} as never

const BOOK = {
  items: [
    {
      customerId: '22222222-2222-2222-2222-222222222222',
      fullName: 'Nadia Client',
      nickname: 'Nadi',
      level: 'vip',
      status: 'returning',
      phoneNumber: '+94771234567',
      lastVisitAtUtc: '2026-09-18T10:00:00Z',
      visitCount: 3,
      totalSpent: 42000,
    },
    {
      customerId: '33333333-3333-3333-3333-333333333333',
      fullName: null,
      nickname: null,
      level: null,
      status: 'new',
      phoneNumber: '',
      lastVisitAtUtc: null,
      visitCount: 0,
      totalSpent: 0,
    },
  ],
  total: 2,
  page: 1,
  pageSize: 25,
}

describe('CustomersPanel', () => {
  const INTERACTIONS = { items: [], total: 0, page: 1, pageSize: 20 }

  const DETAIL = {
    customerId: '22222222-2222-2222-2222-222222222222',
    fullName: 'Nadia Client',
    nickname: 'Nadi',
    phoneNumber: '+94771234567',
    email: 'nadia@example.lk',
    level: 'vip',
    status: 'returning',
    totalSpent: 42000,
    visitCount: 3,
    lastVisitAtUtc: null,
    loyaltyTierIsDerived: 1,
    createdAtUtc: '2026-01-01T00:00:00Z',
    updatedAtUtc: null,
    interactionCount: 0,
    tags: [],
  }

  beforeEach(() => {
    fetchCustomerBook.mockReset().mockResolvedValue(BOOK)
    fetchCustomer.mockReset().mockResolvedValue(DETAIL)
    fetchCustomerInteractions.mockReset().mockResolvedValue(INTERACTIONS)
    deleteCustomer.mockReset()
    updateCustomer.mockReset()
    recordCustomerInteraction.mockReset()
  })

  it('renders the client book from the server page', async () => {
    render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)

    expect(await screen.findByText('Nadia Client')).toBeInTheDocument()
    expect(screen.getByText('Unnamed client')).toBeInTheDocument()
    // A measured zero is shown as a zero amount, not as "not measured".
    expect(screen.getAllByText(/0\.00/).length).toBeGreaterThan(0)
  })

  it('offers no write affordance without customers:manage', async () => {
    render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)
    await screen.findByText('Nadia Client')

    // The server would refuse a create with 403, so the button does not exist for staff.
    expect(screen.queryByRole('button', { name: /add client/i })).not.toBeInTheDocument()
  })

  it('offers the add-client affordance to a manager', async () => {
    render(<CustomersPanel organization={ORG} role="org:boutique_manager" />)
    await screen.findByText('Nadia Client')

    expect(screen.getByRole('button', { name: /add client/i })).toBeInTheDocument()
  })

  it('renders an error state with a retry rather than a stale table', async () => {
    fetchCustomerBook.mockRejectedValueOnce(new Error('network'))
    render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)

    expect(await screen.findByText(/could not load the client book/i)).toBeInTheDocument()
    expect(screen.queryByText('Nadia Client')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })

  it('renders an empty state that says what will fill it', async () => {
    fetchCustomerBook.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
    render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)

    expect(await screen.findByText(/no clients match this view/i)).toBeInTheDocument()
  })

  it('hides edit and remove without customers:manage, and shows them with it', async () => {
    const { unmount } = render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)
    await userEvent.click(await screen.findByText('Nadia Client'))
    await waitFor(() => expect(fetchCustomer).toHaveBeenCalled())
    const staffSheet = await screen.findByRole('dialog')
    expect(within(staffSheet).queryByRole('button', { name: /edit/i })).not.toBeInTheDocument()
    expect(within(staffSheet).queryByRole('button', { name: /remove/i })).not.toBeInTheDocument()
    unmount()

    render(<CustomersPanel organization={ORG} role="org:boutique_manager" />)
    await userEvent.click(await screen.findByText('Nadia Client'))
    const managerSheet = await screen.findByRole('dialog')
    expect(within(managerSheet).getByRole('button', { name: /edit/i })).toBeInTheDocument()
    expect(within(managerSheet).getByRole('button', { name: /remove/i })).toBeInTheDocument()
  })

  it('renders a not-found state, not an empty one, when the client is not in this boutique', async () => {
    fetchCustomer.mockRejectedValue(new ApiError(404, 'Not found'))
    render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)

    await userEvent.click(await screen.findByText('Nadia Client'))

    expect(
      await screen.findByText(/this client is not in this boutique/i),
    ).toBeInTheDocument()
  })

  it('says the amount is "amount taken" and that no Blossoms are charged', async () => {
    render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)
    await screen.findByText('Nadia Client')

    await userEvent.click(screen.getByText('Nadia Client'))
    await userEvent.click(await screen.findByRole('button', { name: /log a visit/i }))

    expect(await screen.findByLabelText(/amount taken/i)).toBeInTheDocument()
    expect(screen.getByText(/no blossoms are charged for a visit/i)).toBeInTheDocument()
    // The log-visit dialog renders no income figure.
    expect(screen.queryByText(/income/i)).not.toBeInTheDocument()
  })

  it('shows the book, not an error card, when the first request is aborted by React StrictMode', async () => {
    // The reported defect: on a cold load every page showed "Try again" until it was pressed. In
    // development React StrictMode mounts, unmounts and mounts again, so the effect's cleanup
    // aborts the first request. axios then rejects that request with a cancellation, and the
    // panel counted it as a load failure. The second request is fine, and "Try again" worked
    // because it issues a request no one aborts - which is exactly the shape the user described.
    fetchCustomerBook.mockImplementation((...args: unknown[]) => {
      const signal = args[2] as AbortSignal | undefined
      if (signal?.aborted) {
        return Promise.reject(new ApiError(0, 'canceled', 'ERR_CANCELED'))
      }
      return new Promise((resolve, reject) => {
        signal?.addEventListener('abort', () =>
          reject(new ApiError(0, 'canceled', 'ERR_CANCELED')),
        )
        resolve(BOOK)
      })
    })

    render(
      <StrictMode>
        <CustomersPanel organization={ORG} role="org:boutique_staff" />
      </StrictMode>,
    )

    expect(await screen.findByText('Nadia Client')).toBeInTheDocument()
    expect(screen.queryByText(/could not load the client book/i)).not.toBeInTheDocument()
  })

  it('ignores a cancelled request that settles after a newer request already returned', async () => {
    // The out-of-order half of the same defect. The first request is aborted, and its rejection can
    // land *after* the replacement has already rendered a page. Left unguarded it blanked the table
    // and raised the error card over data that was on screen and correct.
    let rejectStale: ((reason: unknown) => void) | null = null as ((reason: unknown) => void) | null
    fetchCustomerBook
      .mockImplementationOnce(
        (...args: unknown[]) =>
          new Promise((_resolve, reject) => {
            rejectStale = reject as (reason: unknown) => void
            const signal = args[2] as AbortSignal | undefined
            signal?.addEventListener('abort', () =>
              reject(new ApiError(0, 'canceled', 'ERR_CANCELED')),
            )
          }),
      )
      .mockResolvedValue(BOOK)

    render(<CustomersPanel organization={ORG} role="org:boutique_staff" />)
    await waitFor(() => expect(fetchCustomerBook).toHaveBeenCalledTimes(1))

    // A newer request (a filter change) returns first and paints the rows.
    fireEvent.change(screen.getByLabelText(/search clients/i), { target: { value: 'N' } })
    expect(await screen.findByText('Nadia Client')).toBeInTheDocument()

    // The stale, cancelled request settles last.
    rejectStale?.(new ApiError(0, 'canceled', 'ERR_CANCELED'))

    await waitFor(() => expect(fetchCustomerBook).toHaveBeenCalledTimes(2))
    await waitFor(() =>
      expect(screen.queryByText(/could not load the client book/i)).not.toBeInTheDocument(),
    )
    expect(screen.getByText('Nadia Client')).toBeInTheDocument()
  })
})
