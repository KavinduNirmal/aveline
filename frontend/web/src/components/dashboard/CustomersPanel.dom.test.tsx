import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { StrictMode } from 'react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const fetchCustomerBook = vi.fn()
const fetchCustomer = vi.fn()
const fetchCustomerInteractions = vi.fn()
const fetchCustomerBrief = vi.fn()
const fetchCustomerMemories = vi.fn()
const fetchCustomerEvents = vi.fn()
const deleteCustomer = vi.fn()
const updateCustomer = vi.fn()
const recordCustomerInteraction = vi.fn()

vi.mock('@/lib/customers-api', () => ({
  fetchCustomerBook: (...args: unknown[]) => fetchCustomerBook(...args),
  fetchCustomer: (...args: unknown[]) => fetchCustomer(...args),
  fetchCustomerInteractions: (...args: unknown[]) => fetchCustomerInteractions(...args),
  fetchCustomerBrief: (...args: unknown[]) => fetchCustomerBrief(...args),
  fetchCustomerMemories: (...args: unknown[]) => fetchCustomerMemories(...args),
  fetchCustomerEvents: (...args: unknown[]) => fetchCustomerEvents(...args),
  deleteCustomer: (...args: unknown[]) => deleteCustomer(...args),
  updateCustomer: (...args: unknown[]) => updateCustomer(...args),
  recordCustomerInteraction: (...args: unknown[]) => recordCustomerInteraction(...args),
  createWalkInCustomer: vi.fn(),
}))

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}))

import { toast } from 'sonner'

import { ApiError } from '@/lib/api-error'

import { CustomersPanel } from './CustomersPanel'

/** The org id as a plain string, so an assertion can name it without fighting the `as never` cast. */
const ORG_ID = '11111111-1111-1111-1111-111111111111'

const ORG = {
  id: ORG_ID,
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

/** One occasion ahead and one already past, so "upcoming" has to be decided rather than assumed. */
const EVENTS = [
  {
    id: 'evt-future',
    eventType: 'wedding',
    eventDate: '2026-12-01T00:00:00Z',
    description: null,
    isActive: true,
  },
  {
    id: 'evt-past',
    eventType: 'birthday',
    eventDate: '2020-01-01T00:00:00Z',
    description: null,
    isActive: true,
  },
]

const MEMORIES = [
  {
    id: 'mem-1',
    customerId: '22222222-2222-2222-2222-222222222222',
    content: 'Prefers emerald silk',
    category: 'preference',
    source: 'conversation',
    isExplicit: true,
    confidence: 0.9,
    createdAtUtc: '2026-09-01T00:00:00Z',
  },
  {
    id: 'mem-2',
    customerId: '22222222-2222-2222-2222-222222222222',
    content: 'The delivery arrived late',
    category: 'complaint',
    source: 'conversation',
    isExplicit: true,
    confidence: 0.9,
    createdAtUtc: '2026-09-02T00:00:00Z',
  },
]

describe('CustomersPanel', () => {
  const CUSTOMER_ID = '22222222-2222-2222-2222-222222222222'
  const INTERACTIONS = { items: [], total: 0, page: 1, pageSize: 20 }

  /**
   * The panel is one component over two URLs, so the tests name which one they are on rather than
   * repeating the prop block twenty times.
   */
  function renderPanel(options: {
    role?: string
    openCustomerId?: string | null
    onOpenCustomer?: (customer: { customerId: string }) => void
    onCloseCustomer?: () => void
    strict?: boolean
  } = {}) {
    const panel = (
      <CustomersPanel
        organization={ORG}
        role={options.role ?? 'org:boutique_staff'}
        openCustomerId={options.openCustomerId ?? null}
        onOpenCustomer={options.onOpenCustomer ?? vi.fn()}
        onCloseCustomer={options.onCloseCustomer ?? vi.fn()}
      />
    )
    return render(options.strict ? <StrictMode>{panel}</StrictMode> : panel)
  }

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
    description: null,
    consentStatus: 'granted',
    preferences: [],
  }

  const BRIEF = {
    customerId: '22222222-2222-2222-2222-222222222222',
    customerName: 'Nadia Client',
    description: 'Collector of hand-woven silks; prefers private appointments.',
    status: 'returning',
    consentStatus: 'granted',
    preferenceSummary: 'general: emerald silk',
    tags: ['vip'],
    upcomingEvents: [
      {
        id: 'evt-1',
        eventType: 'wedding',
        eventDate: '2026-12-01T00:00:00Z',
        description: null,
      },
    ],
    memories: [
      {
        id: 'mem-1',
        customerId: '22222222-2222-2222-2222-222222222222',
        content: 'Prefers emerald silk',
        category: 'preference',
        source: 'conversation',
        isExplicit: true,
        confidence: 0.9,
        createdAtUtc: '2026-09-01T00:00:00Z',
      },
    ],
    generatedAtUtc: '2026-09-27T00:00:00Z',
  }

  beforeEach(() => {
    fetchCustomerBook.mockReset().mockResolvedValue(BOOK)
    fetchCustomer.mockReset().mockResolvedValue(DETAIL)
    fetchCustomerInteractions.mockReset().mockResolvedValue(INTERACTIONS)
    fetchCustomerBrief.mockReset().mockResolvedValue(BRIEF)
    fetchCustomerMemories.mockReset().mockResolvedValue(MEMORIES)
    fetchCustomerEvents.mockReset().mockResolvedValue(EVENTS)
    deleteCustomer.mockReset()
    updateCustomer.mockReset()
    vi.mocked(toast.success).mockReset()
    vi.mocked(toast.error).mockReset()
    recordCustomerInteraction.mockReset()
  })

  it('renders the client book from the server page', async () => {
    renderPanel()

    expect(await screen.findByText('Nadia Client')).toBeInTheDocument()
    expect(screen.getByText('Unnamed client')).toBeInTheDocument()
    // A measured zero is shown as a zero amount, not as "not measured".
    expect(screen.getAllByText(/0\.00/).length).toBeGreaterThan(0)
  })

  it('offers no write affordance without customers:manage', async () => {
    renderPanel()
    await screen.findByText('Nadia Client')

    // The server would refuse a create with 403, so the button does not exist for staff.
    expect(screen.queryByRole('button', { name: /add client/i })).not.toBeInTheDocument()
  })

  it('offers the add-client affordance to a manager', async () => {
    renderPanel({ role: 'org:boutique_manager' })
    await screen.findByText('Nadia Client')

    expect(screen.getByRole('button', { name: /add client/i })).toBeInTheDocument()
  })

  it('renders an error state with a retry rather than a stale table', async () => {
    fetchCustomerBook.mockRejectedValueOnce(new Error('network'))
    renderPanel()

    expect(await screen.findByText(/could not load the client book/i)).toBeInTheDocument()
    expect(screen.queryByText('Nadia Client')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
  })

  it('renders an empty state that says what will fill it', async () => {
    fetchCustomerBook.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 25 })
    renderPanel()

    expect(await screen.findByText(/no clients match this view/i)).toBeInTheDocument()
  })

  it('opens a client by navigating to their own URL, not by opening a sheet', async () => {
    // The record is a page. A sheet cannot be linked to, and it was only ever as wide as the
    // viewport it slid over, which is why the book's row navigates rather than disclosing.
    const onOpenCustomer = vi.fn()
    renderPanel({ onOpenCustomer })

    await userEvent.click(await screen.findByText('Nadia Client'))

    expect(onOpenCustomer).toHaveBeenCalledWith(
      expect.objectContaining({ customerId: CUSTOMER_ID }),
    )
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
  })

  it('hides edit and remove without customers:manage, and shows them with it', async () => {
    const { unmount } = renderPanel({ openCustomerId: CUSTOMER_ID })
    await screen.findByRole('button', { name: /log a visit/i })
    expect(screen.queryByRole('button', { name: /edit/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /remove/i })).not.toBeInTheDocument()
    unmount()

    renderPanel({ role: 'org:boutique_manager', openCustomerId: CUSTOMER_ID })
    expect(await screen.findByRole('button', { name: /edit/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /remove/i })).toBeInTheDocument()
  })

  it('does not fetch the book while a client page is open', async () => {
    // Two URLs, two reads. A list request behind an open record is work whose result nothing on
    // screen can use.
    renderPanel({ openCustomerId: CUSTOMER_ID })
    await screen.findByRole('button', { name: /log a visit/i })

    expect(fetchCustomerBook).not.toHaveBeenCalled()
    expect(fetchCustomer).toHaveBeenCalledWith(ORG_ID, CUSTOMER_ID, expect.anything())
  })

  it('says who the client is, what they are worth and how they may be reached', async () => {
    renderPanel({ openCustomerId: CUSTOMER_ID })

    expect(await screen.findByRole('heading', { name: 'Nadia Client' })).toBeInTheDocument()
    // The counter's own name for them is shown alongside the full name, not instead of it.
    expect(screen.getByText(/known at the counter as nadi/i)).toBeInTheDocument()
    expect(screen.getByText('Spent with us')).toBeInTheDocument()
    expect(screen.getByText(/consent granted/i)).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'nadia@example.lk' })).toBeInTheDocument()
  })

  it('separates the notes the client stated from the ones the agent inferred', async () => {
    // The distinction is the reason the panel is worth reading, and it is only real if the read
    // path carries it: this is the surface where an associate decides whether to act on a note.
    // The complaint is checked alongside the preference, because collapsing a complaint into a
    // preference is the failure this panel exists to make visible.
    renderPanel({ openCustomerId: CUSTOMER_ID })

    const card = await screen.findByTestId('customer-memories')
    // Both notes print the label, because both were stated: the assertion is the count, not a
    // single match, and `getAllBy` is what proves the label is attached to each note rather than
    // to the once-per-card heading.
    expect(within(card).getAllByText(/stated by the client/i).length).toBeGreaterThan(1)
    expect(within(card).getByText('Prefers emerald silk')).toBeInTheDocument()
    expect(within(card).getByText(/complaint/i)).toBeInTheDocument()
    expect(within(card).getByText('The delivery arrived late')).toBeInTheDocument()
  })

  it('shows the next occasion apart, and does not call a past one upcoming', async () => {
    renderPanel({ openCustomerId: CUSTOMER_ID })

    // EVENTS carries one future occasion and one from 2020. The band shows the deadline, and the
    // side column lists what is ahead rather than trusting the collection to be "upcoming".
    expect(await screen.findByText(/in \d+ days/i)).toBeInTheDocument()
    expect(screen.getAllByText(/coming up/i).length).toBeGreaterThan(0)
    expect(screen.getByText('Past occasions')).toBeInTheDocument()
    expect(screen.getAllByText(/wedding/i).length).toBeGreaterThan(0)
    expect(screen.getAllByText(/birthday/i).length).toBeGreaterThan(0)
  })

  it('saves the editable subset from the client page, description included', async () => {
    // The description is the one prose field an associate writes and the brief leads with, so it
    // travels with the edit rather than being a field the form forgets. `status` is deliberately
    // absent: the server derives it and ignores a client value.
    updateCustomer.mockResolvedValue(DETAIL)
    renderPanel({ role: 'org:boutique_manager', openCustomerId: CUSTOMER_ID })

    await userEvent.click(await screen.findByRole('button', { name: /edit/i }))
    await userEvent.clear(screen.getByLabelText('Description'))
    await userEvent.type(screen.getByLabelText('Description'), 'Collector of hand-woven silks.')
    await userEvent.click(screen.getByRole('button', { name: /save changes/i }))

    await waitFor(() => expect(updateCustomer).toHaveBeenCalled())
    expect(updateCustomer).toHaveBeenCalledWith(
      ORG_ID,
      CUSTOMER_ID,
      expect.objectContaining({
        fullName: 'Nadia Client',
        description: 'Collector of hand-woven silks.',
      }),
    )
    // The form closes and the record is re-read, so the page cannot show what it just replaced.
    await waitFor(() => expect(fetchCustomer).toHaveBeenCalledTimes(2))
  })

  it('refuses a phone number another client already holds, in the server\'s own terms', async () => {
    // A 409 is a data-integrity problem the associate has to resolve, not a generic failure, so the
    // sentence names the cause.
    updateCustomer.mockRejectedValue(new ApiError(409, 'Conflict', 'customer-phone-conflict'))
    renderPanel({ role: 'org:boutique_manager', openCustomerId: CUSTOMER_ID })

    await userEvent.click(await screen.findByRole('button', { name: /edit/i }))
    await userEvent.click(screen.getByRole('button', { name: /save changes/i }))

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Could not save this client',
        expect.objectContaining({
          description: 'Another client in this boutique already has that phone number.',
        }),
      ),
    )
    // The form stays open: the edit was not applied, and closing it would imply it was.
    expect(screen.getByRole('button', { name: /save changes/i })).toBeInTheDocument()
  })

  it('will not remove a client whose orders are still live', async () => {
    deleteCustomer.mockRejectedValue(new ApiError(409, 'Conflict', 'customer-has-open-orders'))
    const onCloseCustomer = vi.fn()
    renderPanel({
      role: 'org:boutique_manager',
      openCustomerId: CUSTOMER_ID,
      onCloseCustomer,
    })

    await userEvent.click(await screen.findByRole('button', { name: /remove/i }))

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Could not remove this client',
        expect.objectContaining({
          description: 'This client has orders that are still live. Close or cancel them first.',
        }),
      ),
    )
    // A refused removal must not leave the page: the client is still there.
    expect(onCloseCustomer).not.toHaveBeenCalled()
  })

  it('leaves the client page only once a removal is accepted', async () => {
    deleteCustomer.mockResolvedValue(undefined)
    const onCloseCustomer = vi.fn()
    renderPanel({
      role: 'org:boutique_manager',
      openCustomerId: CUSTOMER_ID,
      onCloseCustomer,
    })

    await userEvent.click(await screen.findByRole('button', { name: /remove/i }))

    await waitFor(() => expect(onCloseCustomer).toHaveBeenCalled())
  })

  it('bounds the activity rail instead of letting it grow with the history', async () => {
    // An unbounded list pushed every section below it off the page. The rail is a fixed-height
    // window that scrolls, and the count in the heading is what says how much history there is.
    const many = Array.from({ length: 12 }, (_, index) => ({
      interactionId: `int-${index}`,
      occurredAtUtc: `2026-09-${String(index + 1).padStart(2, '0')}T10:00:00Z`,
      channel: 'in_person',
      direction: 'inbound',
      note: `Exchange ${index}`,
      countedAsVisit: index === 0,
    }))
    fetchCustomerInteractions.mockResolvedValue({ items: many, total: 12, page: 1, pageSize: 20 })

    renderPanel({ openCustomerId: CUSTOMER_ID })

    const rail = await screen.findByTestId('customer-activity-scroll')
    expect(rail.className).toMatch(/h-80/)
    // All twelve are in the DOM; the box scrolls rather than the list being truncated.
    expect(within(rail).getByText('Exchange 11')).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: /recent activity/i })).toBeInTheDocument()
  })

  it('leads the client page with the pre-contact brief', async () => {
    // The brief is a read of its own, not a projection of the record: an associate opens it to
    // decide how to approach the client, so the description and what is coming up come first and
    // each stored note says whether the client stated it or the agent inferred it.
    renderPanel({ openCustomerId: CUSTOMER_ID })

    const brief = await screen.findByText(/before you make contact/i)
    const section = brief.closest('section')!
    expect(within(section).getByText(/collector of hand-woven silks/i)).toBeInTheDocument()
    expect(within(section).getByText('general: emerald silk')).toBeInTheDocument()
    expect(within(section).getByText('Prefers emerald silk')).toBeInTheDocument()
    expect(within(section).getByText(/stated by the client/i)).toBeInTheDocument()
  })

  it('says why the memory section is empty when consent is not granted', async () => {
    // "Opted out" and "nothing on file" are different facts, and an associate acting on the wrong
    // one decides the boutique knows nothing about a client whose notes simply may not be shown.
    fetchCustomerBrief.mockResolvedValue({ ...BRIEF, consentStatus: 'revoked', memories: [] })

    renderPanel({ openCustomerId: CUSTOMER_ID })

    expect(
      await screen.findByText(/opted out, so nothing derived from their messages/i),
    ).toBeInTheDocument()
  })

  it('renders a not-found state, not an empty one, when the client is not in this boutique', async () => {
    fetchCustomer.mockRejectedValue(new ApiError(404, 'Not found'))
    renderPanel({ openCustomerId: CUSTOMER_ID })

    expect(
      await screen.findByText(/this client is not in this boutique/i),
    ).toBeInTheDocument()
  })

  it('says the amount is "amount taken" and that no Blossoms are charged', async () => {
    renderPanel({ openCustomerId: CUSTOMER_ID })

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

    renderPanel({ strict: true })

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

    renderPanel()
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
