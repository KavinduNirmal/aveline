import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter } from 'react-router-dom'
import { describe, expect, it, vi } from 'vitest'

import { AdminAuditView } from './AdminAudit'

const queryAuditEntries = vi.fn()
vi.mock('@/lib/admin/api', () => ({
  queryAuditEntries: (...args: unknown[]) => queryAuditEntries(...args),
}))

function row(overrides: Record<string, unknown> = {}) {
  return {
    id: '01900000-0000-7000-8000-000000000001',
    occurredAt: '2026-01-01T00:00:00Z',
    organizationId: null,
    actorKind: 'User',
    actorUserId: 'user-1',
    actorRef: null,
    action: 'users.state.updated',
    entityType: 'User',
    entityId: 'entity-1',
    reason: 'policy',
    requestId: 'req-1',
    before: null,
    after: null,
    ...overrides,
  }
}

function renderView(initial = '/') {
  return render(
    <MemoryRouter initialEntries={[initial]}>
      <AdminAuditView />
    </MemoryRouter>,
  )
}

describe('AdminAuditView', () => {
  it('sends the full server-side filter set the operator set', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue({ items: [row()], page: 1, pageSize: 25, total: 1 })

    renderView()

    await waitFor(() => {
      expect(queryAuditEntries).toHaveBeenCalled()
    })

    await userEvent.type(screen.getByLabelText(/action/i), 'users.state')
    await userEvent.type(screen.getByLabelText(/entity type/i), 'User')
    await userEvent.type(screen.getByLabelText(/entity id/i), 'entity-1')
    await userEvent.type(screen.getByLabelText(/actor user id/i), 'user-1')
    await userEvent.type(screen.getByLabelText(/organization id/i), '01900000-0000-7000-8000-00000000ffff')

    await waitFor(() => {
      const last = queryAuditEntries.mock.calls.at(-1)?.[0] as Record<string, unknown>
      expect(last.action).toBe('users.state')
      expect(last.entityType).toBe('User')
      expect(last.entityId).toBe('entity-1')
      expect(last.actorUserId).toBe('user-1')
      expect(last.organizationId).toBe('01900000-0000-7000-8000-00000000ffff')
    })
  })

  it('pages through the ledger', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockResolvedValue({ items: [row()], page: 1, pageSize: 25, total: 60 })

    renderView()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /next/i })).toBeEnabled()
    })

    await userEvent.click(screen.getByRole('button', { name: /next/i }))

    await waitFor(() => {
      const last = queryAuditEntries.mock.calls.at(-1)?.[0] as Record<string, unknown>
      expect(last.page).toBe(2)
    })
  })

  it('renders an error state with a working retry when the query fails', async () => {
    queryAuditEntries.mockReset()
    queryAuditEntries.mockRejectedValueOnce(new Error('500 Internal Server Error'))
    queryAuditEntries.mockResolvedValueOnce({ items: [row()], page: 1, pageSize: 25, total: 1 })

    renderView()

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
    })

    await userEvent.click(screen.getByRole('button', { name: /retry/i }))

    await waitFor(() => {
      expect(screen.getByText(/users\.state\.updated/)).toBeInTheDocument()
    })
  })
})
