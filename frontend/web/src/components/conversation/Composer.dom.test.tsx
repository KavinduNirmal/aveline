import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import type { ComponentProps } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { PendingAttachment } from '@/contexts/ConversationsContext'
import {
  ATTACHMENT_CAP_MESSAGE,
  MAX_ATTACHMENT_BYTES,
  type AttachmentRefusal,
} from '@/lib/attachment-preparation'
import { formatFileSize } from '@/lib/image-optimizer'

import { Composer } from './Composer'

const mocks = vi.hoisted(() => ({ toastError: vi.fn() }))

vi.mock('sonner', () => ({
  toast: { error: mocks.toastError, success: vi.fn(), info: vi.fn() },
}))

const SEND_FAILURE = /could not be sent/i

/** The exact message `attachment-preparation` builds for an over-cap payload. */
const SIZE_MESSAGE =
  `big.jpg is ${formatFileSize(8 * 1024 * 1024)}. ` +
  `Attachments may be at most ${formatFileSize(MAX_ATTACHMENT_BYTES)}. ` +
  'Try a smaller photo or a JPEG.'

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
    // The tray reads this flag rather than the error wording; transient is the default fixture.
    retryable: true,
    ...overrides,
  }
}

function pdf(name = 'invoice.pdf'): File {
  return new File(['%PDF-1.4\n'], name, { type: 'application/pdf' })
}

/**
 * jsdom cannot point an input at the OS picker, so the chosen files are defined directly and the
 * change event is fired the way the browser would fire it.
 */
function pickFiles(files: File[]) {
  const input = screen.getByLabelText('Choose files to attach')
  Object.defineProperty(input, 'files', { value: files, writable: true, configurable: true })
  fireEvent.change(input)
}

function renderComposer(props: Partial<ComponentProps<typeof Composer>> = {}) {
  const onSend = vi.fn().mockResolvedValue(undefined)
  const onAttach = vi.fn().mockResolvedValue([] as AttachmentRefusal[])
  const onRetryAttachment = vi.fn()
  const onRemoveAttachment = vi.fn()
  render(
    <Composer
      onSend={onSend}
      onAttach={onAttach}
      onRetryAttachment={onRetryAttachment}
      onRemoveAttachment={onRemoveAttachment}
      {...props}
    />,
  )
  return { onSend, onAttach, onRetryAttachment, onRemoveAttachment }
}

describe('Composer attachments', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders a paperclip with an accessible name', () => {
    renderComposer()
    expect(screen.getByRole('button', { name: 'Attach files' })).toBeInTheDocument()
  })

  it('offers a hidden multi-file input restricted to the attachment allow-list', () => {
    renderComposer()
    const input = screen.getByLabelText('Choose files to attach')
    expect(input).toHaveAttribute('type', 'file')
    expect(input).toHaveAttribute('multiple')
    expect(input).toHaveAttribute('accept', 'image/*,application/pdf')
  })

  it('passes the chosen files to onAttach', async () => {
    const { onAttach } = renderComposer()
    const file = pdf()

    pickFiles([file])

    await waitFor(() => expect(onAttach).toHaveBeenCalledWith([file]))
  })

  it('disables send with an accessible reason while a chip is uploading', async () => {
    renderComposer({
      pendingAttachments: [
        chip({ status: 'uploading', attachmentId: null, storedContentType: null }),
      ],
    })
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Here it is' } })

    const send = screen.getByRole('button', { name: 'Send message' })
    expect(send).toBeDisabled()
    expect(send).toHaveAccessibleDescription(/waiting for invoice\.pdf to finish uploading/i)
  })

  it('disables send with an accessible reason while a chip has failed', () => {
    renderComposer({
      pendingAttachments: [
        chip({
          status: 'failed',
          attachmentId: null,
          storedContentType: null,
          error: 'That file could not be uploaded. Check your connection and try again.',
        }),
      ],
    })
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Here it is' } })

    const send = screen.getByRole('button', { name: 'Send message' })
    expect(send).toBeDisabled()
    expect(send).toHaveAccessibleDescription(/retry or remove/i)
  })

  it('refuses a sixth file with the cap message and never calls onAttach', async () => {
    const { onAttach } = renderComposer({
      pendingAttachments: [0, 1, 2, 3, 4].map((i) =>
        chip({ id: `chip-${i}`, fileName: `file-${i}.pdf`, attachmentId: `att-${i}` }),
      ),
    })

    pickFiles([pdf('sixth.pdf')])

    expect(await screen.findByText(ATTACHMENT_CAP_MESSAGE)).toBeInTheDocument()
    expect(onAttach).not.toHaveBeenCalled()
    expect(mocks.toastError).toHaveBeenCalledWith(ATTACHMENT_CAP_MESSAGE)
  })

  it('shows the size refusal for an over-cap file the preparation layer returned', async () => {
    const refusal: AttachmentRefusal = {
      status: 'refused',
      reason: 'over-cap',
      fileName: 'big.jpg',
      byteLength: 8 * 1024 * 1024,
      limitBytes: MAX_ATTACHMENT_BYTES,
      message: SIZE_MESSAGE,
    }
    const onAttach = vi.fn().mockResolvedValue([refusal])
    renderComposer({ onAttach })

    pickFiles([new File(['x'], 'big.jpg', { type: 'image/jpeg' })])

    expect(await screen.findByText(SIZE_MESSAGE)).toBeInTheDocument()
    expect(mocks.toastError).toHaveBeenCalledWith(SIZE_MESSAGE)
    expect(onAttach).toHaveBeenCalledTimes(1)
  })

  it('keeps the text and says so when a send fails', async () => {
    const onSend = vi.fn().mockRejectedValue(new Error('offline'))
    renderComposer({ onSend })
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Keep me' } })

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    expect(await screen.findByText(SEND_FAILURE)).toBeInTheDocument()
    expect(screen.getByLabelText('Message')).toHaveValue('Keep me')
    expect(onSend).toHaveBeenCalledWith('Keep me', undefined)
  })

  it('clears the textarea only once the send is confirmed', async () => {
    const { onSend } = renderComposer()
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Send me' } })

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(onSend).toHaveBeenCalledTimes(1))
    await waitFor(() => expect(screen.getByLabelText('Message')).toHaveValue(''))
  })

  it('binds the ids of the stored chips to the send', async () => {
    const { onSend } = renderComposer({
      pendingAttachments: [
        chip({ id: 'a', attachmentId: 'att-a' }),
        chip({ id: 'b', attachmentId: 'att-b' }),
      ],
    })
    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Two files' } })

    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(onSend).toHaveBeenCalledWith('Two files', ['att-a', 'att-b']))
  })

  it('wires retry and remove for a failed chip', () => {
    const { onRetryAttachment, onRemoveAttachment } = renderComposer({
      pendingAttachments: [
        chip({
          status: 'failed',
          attachmentId: null,
          storedContentType: null,
          error: 'That file could not be uploaded. Check your connection and try again.',
        }),
      ],
    })

    fireEvent.click(screen.getByRole('button', { name: 'Retry upload of invoice.pdf' }))
    fireEvent.click(screen.getByRole('button', { name: 'Remove invoice.pdf' }))

    expect(onRetryAttachment).toHaveBeenCalledWith('chip-1')
    expect(onRemoveAttachment).toHaveBeenCalledWith('chip-1')
  })

  it('still sends text while a refused file is displayed and never retries it', async () => {
    const refusal: AttachmentRefusal = {
      status: 'refused',
      reason: 'unsupported-type',
      fileName: 'notes.txt',
      message: "notes.txt is not an image or a PDF, so it can't be attached.",
    }
    const { onSend } = renderComposer({ onAttach: vi.fn().mockResolvedValue([refusal]) })

    pickFiles([new File(['x'], 'notes.txt', { type: 'text/plain' })])
    expect(await screen.findByText(refusal.message)).toBeInTheDocument()

    fireEvent.change(screen.getByLabelText('Message'), { target: { value: 'Text only anyway' } })
    fireEvent.click(screen.getByRole('button', { name: 'Send message' }))

    await waitFor(() => expect(onSend).toHaveBeenCalledWith('Text only anyway', undefined))
  })
})
