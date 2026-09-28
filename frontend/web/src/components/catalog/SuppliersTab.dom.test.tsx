import { render, screen, fireEvent, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { SuppliersTab } from './SuppliersTab'
import type { SupplierMock } from './mockData'

const { toastSuccess, toastError } = vi.hoisted(() => ({
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock('sonner', () => ({
  toast: Object.assign(vi.fn(), {
    success: toastSuccess,
    error: toastError,
    info: vi.fn(),
  }),
}))

vi.mock('@/lib/catalog-api', () => ({
  createSupplier: vi.fn(async (_orgId: string, payload: any) => ({
    id: 'sup-created-1',
    name: payload.name,
    specialty: payload.specialty || 'Fine Handloom Craft',
    location: payload.location || 'Colombo',
    contactEmail: payload.contactEmail || '',
    contactPhone: payload.contactPhone || '',
    minimumOrder: payload.minimumOrder || 0,
    deliveryTimeDays: payload.deliveryTimeDays || 5,
    isActive: true,
    sampleCatalogCount: 0,
  })),
}))

function mockSupplier(overrides: Partial<SupplierMock> = {}): SupplierMock {
  return {
    id: 'sup-1',
    name: 'Varanasi Silk Guild',
    specialty: 'Pure Mulberry Silk & Gold Zari Weaves',
    contactEmail: 'orders@varanasiweavers.in',
    contactPhone: '+91 542 234 5678',
    location: 'Varanasi, India',
    minimumOrder: 45000,
    deliveryTimeDays: 7,
    isActive: true,
    sampleCatalogCount: 3,
    ...overrides,
  }
}

describe('SuppliersTab and AddSupplierModal', () => {
  beforeEach(() => {
    toastSuccess.mockClear()
    toastError.mockClear()
  })

  it('renders empty state when no ateliers are registered', () => {
    render(<SuppliersTab suppliers={[]} />)

    expect(screen.getByText('No Partner Ateliers Found')).toBeInTheDocument()
    expect(screen.getByText('Add Your First Atelier')).toBeInTheDocument()
    expect(screen.getByText('Add Partner Atelier')).toBeInTheDocument()
  })

  it('renders registered partner atelier cards with metrics', () => {
    const supplier = mockSupplier()
    render(<SuppliersTab suppliers={[supplier]} />)

    expect(screen.getByText('Varanasi Silk Guild')).toBeInTheDocument()
    expect(screen.getByText('Pure Mulberry Silk & Gold Zari Weaves')).toBeInTheDocument()
    expect(screen.getByText('Varanasi, India')).toBeInTheDocument()
    expect(screen.getByText('orders@varanasiweavers.in')).toBeInTheDocument()
    expect(screen.getByText('7 days')).toBeInTheDocument()
    expect(screen.getByText('Active Partner')).toBeInTheDocument()
  })

  it('opens Add Partner Atelier modal and creates new atelier on submit', async () => {
    const user = userEvent.setup()
    const onAddSupplier = vi.fn()

    render(
      <SuppliersTab
        suppliers={[]}
        organizationId="org-123"
        onAddSupplier={onAddSupplier}
      />,
    )

    // Click Add Partner Atelier
    const addBtn = screen.getByText('Add Partner Atelier')
    await user.click(addBtn)

    // Modal should be visible
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(screen.getByText('Connect Partner Atelier')).toBeInTheDocument()

    // Fill form
    const nameInput = screen.getByLabelText(/Atelier \/ Supplier Name/i)
    await user.type(nameInput, 'Colombo Heritage Handlooms')

    const specialtyInput = screen.getByLabelText(/Craft Specialty/i)
    await user.type(specialtyInput, 'Handwoven Silks & Cottons')

    const locationInput = screen.getByLabelText(/Location \/ City/i)
    await user.type(locationInput, 'Pettah, Colombo')

    const emailInput = screen.getByLabelText(/Contact Email/i)
    await user.type(emailInput, 'orders@colombohandlooms.lk')

    const phoneInput = screen.getByLabelText(/WhatsApp \/ Phone/i)
    await user.type(phoneInput, '+94 11 234 5678')

    // Submit form
    const submitBtn = screen.getByRole('button', { name: /Connect Atelier/i })
    await user.click(submitBtn)

    await waitFor(() => {
      expect(onAddSupplier).toHaveBeenCalledTimes(1)
    })

    expect(onAddSupplier).toHaveBeenCalledWith(
      expect.objectContaining({
        name: 'Colombo Heritage Handlooms',
        specialty: 'Handwoven Silks & Cottons',
        location: 'Pettah, Colombo',
        contactEmail: 'orders@colombohandlooms.lk',
      }),
    )
    expect(toastSuccess).toHaveBeenCalled()
  })

  it('validates required name field before submitting', async () => {
    const user = userEvent.setup()
    const onAddSupplier = vi.fn()

    render(<SuppliersTab suppliers={[]} onAddSupplier={onAddSupplier} />)

    // Open modal via empty state button
    const firstBtn = screen.getByText('Add Your First Atelier')
    await user.click(firstBtn)

    expect(screen.getByRole('dialog')).toBeInTheDocument()

    // Try submitting without name
    const form = screen.getByRole('dialog').querySelector('form')
    expect(form).not.toBeNull()
    if (form) {
      fireEvent.submit(form)
    }

    expect(onAddSupplier).not.toHaveBeenCalled()
  })
})
