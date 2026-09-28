import { render, screen, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import { AuditTrailPanel } from './AuditTrailPanel'

vi.mock('@/hooks/useAuditTrail', () => ({
  useAuditTrail: () => ({
    entries: [],
    isLoading: false,
    error: null,
    hasMore: false,
    loadMore: vi.fn(),
    refresh: vi.fn(),
  }),
}))

/**
 * The drawer had two X buttons: `SheetContent` renders its own close control, and the panel added a
 * second one inside the header.
 */
describe('AuditTrailPanel', () => {
  it('exposes exactly one close control', () => {
    render(<AuditTrailPanel open onClose={() => undefined} />)
    const dialog = screen.getByRole('dialog')
    expect(within(dialog).getAllByRole('button', { name: /close/i })).toHaveLength(1)
  })
})
