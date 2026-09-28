import { fireEvent, render, screen } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'

import type { PendingAttachment } from '@/contexts/ConversationsContext'

import { AttachmentTray } from './AttachmentTray'

/**
 * The tray's own contract: every chip is named, an upload in flight is an honest indeterminate
 * busy state, a failure carries the server-facing reason in words, and retry is offered only where
 * pressing it can change the outcome.
 */

function chip(overrides: Partial<PendingAttachment> = {}): PendingAttachment {
  return {
    id: 'chip-1',
    fileName: 'invoice.pdf',
    payload: new Blob(['%PDF-1.4']),
    status: 'ready',
    attachmentId: 'att-1',
    storedContentType: 'application/pdf',
    analysable: false,
    sizeBytes: 12,
    // The classification the tray reads instead of matching the error wording. Transient is the
    // default here; the two refusal cases override it to false.
    retryable: true,
    ...overrides,
  }
}

const TRANSIENT_ERROR = 'That file could not be uploaded. Check your connection and try again.'
const PERMISSION_ERROR = "You don't have access to this thread."
const GONE_ERROR = 'This thread is no longer available.'

// The refusals the server will not reconsider: no retry is offered for either.
const PERMANENT = { retryable: false } as const

function renderTray(attachments: PendingAttachment[]) {
  const onRetry = vi.fn()
  const onRemove = vi.fn()
  render(<AttachmentTray attachments={attachments} onRetry={onRetry} onRemove={onRemove} />)
  return { onRetry, onRemove }
}

describe('AttachmentTray', () => {
  it('renders nothing when there are no pending attachments', () => {
    const { container } = render(
      <AttachmentTray attachments={[]} onRetry={vi.fn()} onRemove={vi.fn()} />,
    )
    expect(container).toBeEmptyDOMElement()
  })

  it('names each chip and shows its size and stored state', () => {
    renderTray([chip()])
    expect(screen.getByText('invoice.pdf')).toBeInTheDocument()
    expect(screen.getByText('12 B')).toBeInTheDocument()
    expect(screen.getByText('Ready')).toBeInTheDocument()
  })

  it('shows an indeterminate busy state, never a fabricated percentage, while uploading', () => {
    renderTray([chip({ status: 'uploading', attachmentId: null, storedContentType: null })])

    expect(screen.getByLabelText('Uploading invoice.pdf')).toBeInTheDocument()
    expect(screen.getByText(/uploading/i)).toBeInTheDocument()
    // No invented progress number anywhere in the tray.
    expect(screen.queryByText(/%/)).not.toBeInTheDocument()
  })

  it('renders the specific failure message and offers retry for a transient failure', () => {
    const { onRetry } = renderTray([
      chip({ status: 'failed', attachmentId: null, storedContentType: null, error: TRANSIENT_ERROR }),
    ])

    expect(screen.getByText(TRANSIENT_ERROR)).toBeInTheDocument()
    const retry = screen.getByRole('button', { name: 'Retry upload of invoice.pdf' })
    fireEvent.click(retry)
    expect(onRetry).toHaveBeenCalledWith('chip-1')
  })

  it('does not offer retry for a permission refusal the server will not reconsider', () => {
    renderTray([
      chip({
        status: 'failed',
        attachmentId: null,
        storedContentType: null,
        error: PERMISSION_ERROR,
        ...PERMANENT,
      }),
    ])

    expect(screen.getByText(PERMISSION_ERROR)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /retry upload/i })).not.toBeInTheDocument()
  })

  it('does not offer retry for a conversation that is gone', () => {
    renderTray([
      chip({
        status: 'failed',
        attachmentId: null,
        storedContentType: null,
        error: GONE_ERROR,
        ...PERMANENT,
      }),
    ])

    expect(screen.getByText(GONE_ERROR)).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /retry upload/i })).not.toBeInTheDocument()
  })

  it('offers a remove control for every chip, whatever its state', () => {
    const { onRemove } = renderTray([
      chip({ id: 'a', fileName: 'first.pdf' }),
      chip({
        id: 'b',
        fileName: 'second.pdf',
        status: 'failed',
        attachmentId: null,
        error: TRANSIENT_ERROR,
      }),
      chip({ id: 'c', fileName: 'third.pdf', status: 'uploading', attachmentId: null }),
    ])

    fireEvent.click(screen.getByRole('button', { name: 'Remove second.pdf' }))
    expect(onRemove).toHaveBeenCalledWith('b')
    expect(screen.getByRole('button', { name: 'Remove first.pdf' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Remove third.pdf' })).toBeInTheDocument()
  })

  it('says that a stored PDF is not readable by the visual assistant', () => {
    renderTray([chip()])
    expect(screen.getByText(/won't read this document/i)).toBeInTheDocument()
  })

  it('does not add the not-analysable note to an analysable image', () => {
    renderTray([chip({ storedContentType: 'image/jpeg', analysable: true })])

    expect(screen.queryByText(/won't read this document/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/can't analyse/i)).not.toBeInTheDocument()
  })
})
