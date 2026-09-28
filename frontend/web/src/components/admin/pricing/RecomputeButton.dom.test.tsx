import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { RecomputeButton } from './RecomputeButton'

const session = {
  can: (_permission: string) => false,
}
vi.mock('@/contexts/AdminSessionContext', () => ({
  useAdminSession: () => session,
}))

const RESULT = {
  ruleId: 'rule-1',
  processedRecords: 0,
  affectedOrganizations: 0,
  totalDelta: 0,
  recomputedAt: '2026-01-01T00:00:00Z',
}

describe('RecomputeButton', () => {
  it('is enabled for a caller holding pricing:backdate and issues a real request', async () => {
    session.can = (permission: string) => permission === 'pricing:backdate'
    const recompute = vi.fn().mockResolvedValue(RESULT)

    render(<RecomputeButton ruleId="rule-1" recompute={recompute} />)
    const button = screen.getByRole('button', { name: /recompute/i })
    expect(button).toBeEnabled()

    await userEvent.click(button)
    await waitFor(() => {
      expect(recompute).toHaveBeenCalledWith('rule-1')
    })
  })

  it('is disabled for an admin and issues no request at all', async () => {
    // admin holds pricing:view + pricing:manage but never pricing:backdate.
    const adminGrants = new Set(['pricing:view', 'pricing:manage'])
    session.can = (permission: string) => adminGrants.has(permission)
    const recompute = vi.fn()

    render(<RecomputeButton ruleId="rule-1" recompute={recompute} />)
    const button = screen.getByRole('button', { name: /recompute/i })
    expect(button).toBeDisabled()
    expect(button).toHaveAttribute('title', expect.stringMatching(/pricing:backdate/))

    await userEvent.click(button, { pointerEventsCheck: 0 })
    expect(recompute).not.toHaveBeenCalled()
  })
})
