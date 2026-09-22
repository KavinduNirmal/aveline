import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { useState } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ConversationAttachmentDto } from '@/lib/conversations-api'
import type { ConversationDto } from '@/types/conversation'

const mocks = vi.hoisted(() => ({
  useAuth: vi.fn(),
  upload: vi.fn(),
  send: vi.fn(),
  fetchConversations: vi.fn(),
  fetchMessages: vi.fn(),
  getOrCreate: vi.fn(),
  decideSignOff: vi.fn(),
  selectCustomer: vi.fn(),
}))

vi.mock('@clerk/react', () => ({
  useAuth: () => mocks.useAuth(),
}))

vi.mock('@microsoft/signalr', () => ({
  HubConnectionBuilder: vi.fn(),
  HubConnectionState: {
    Disconnected: 'Disconnected',
    Connecting: 'Connecting',
    Connected: 'Connected',
    Reconnecting: 'Reconnecting',
  },
}))

vi.mock('@/lib/conversations', () => ({
  createConversationsConnection: vi.fn(() => ({})),
  startConversations: vi.fn(() => () => undefined),
}))

vi.mock('@/lib/conversations-api', () => ({
  fetchConversations: (...args: unknown[]) => mocks.fetchConversations(...args),
  fetchMessages: (...args: unknown[]) => mocks.fetchMessages(...args),
  getOrCreateConversation: (...args: unknown[]) => mocks.getOrCreate(...args),
  sendMessage: (...args: unknown[]) => mocks.send(...args),
  decideSignOff: (...args: unknown[]) => mocks.decideSignOff(...args),
  selectConversationCustomer: (...args: unknown[]) => mocks.selectCustomer(...args),
  uploadConversationAttachment: (...args: unknown[]) => mocks.upload(...args),
}))

import { ConversationsProvider, useConversations } from './ConversationsContext'

const ORG = 'org-1'
const CONV = 'conv-1'

function conversation(): ConversationDto {
  return {
    id: CONV,
    kind: 'Salon',
    customerId: null,
    customerName: null,
    externalRef: null,
    threadId: 'thread-1',
    status: 'Active',
    lastMessageAt: null,
    lastMessagePreview: null,
    lastMessageKind: null,
    lastMessageBlock: null,
    lastMessageAuthor: null,
    lastMessageAgentKey: null,
    markers: [],
  }
}

function attachmentDto(
  overrides: Partial<ConversationAttachmentDto> = {},
): ConversationAttachmentDto {
  return {
    attachmentId: 'att-server-1',
    url: `/api/v1/orgs/${ORG}/conversations/${CONV}/attachments/att-server-1`,
    contentType: 'application/pdf',
    fileName: 'invoice.pdf',
    sizeBytes: 12,
    width: null,
    height: null,
    messageId: null,
    ...overrides,
  }
}

function pdfFile(): File {
  return new File(['%PDF-1.4\n'], 'invoice.pdf', { type: 'application/pdf' })
}

/**
 * Drives the context through its own public surface. The textarea is local state, mirroring the
 * real Composer: the point of the remove case is that removing a chip never reaches it.
 */
function Harness({ file }: { file: File }) {
  const ctx = useConversations()
  const [text, setText] = useState('')
  const [refusals, setRefusals] = useState<string[]>([])
  const tray = ctx.pendingAttachments[CONV] ?? []
  const first = tray[0]

  return (
    <div>
      <span data-testid="active">{ctx.activeConversationId ?? 'none'}</span>
      <span data-testid="ready">{ctx.attachmentsReady(CONV) ? 'READY' : 'NOT_READY'}</span>
      <span data-testid="count">{tray.length}</span>
      <span data-testid="tray">
        {tray
          .map(
            (a) =>
              `${a.status}:${a.storedContentType ?? 'none'}:${
                a.analysable ? 'analysable' : 'not-analysable'
              }`,
          )
          .join('|')}
      </span>
      <span data-testid="refusals">{refusals.join('|')}</span>
      <textarea
        aria-label="message"
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      <button onClick={() => void ctx.attach(CONV, [file])}>attach</button>
      <button
        onClick={async () => {
          const result = await ctx.attach(
            CONV,
            Array.from({ length: 6 }, () => file),
          )
          setRefusals(result.map((refusal) => refusal.message))
        }}
      >
        attach-six
      </button>
      <button
        onClick={() => {
          if (first) void ctx.retryAttachment(CONV, first.id)
        }}
      >
        retry
      </button>
      <button
        onClick={() => {
          if (first) void ctx.removeAttachment(CONV, first.id)
        }}
      >
        remove
      </button>
      <button onClick={() => void ctx.send(text, ['att-held'])}>send</button>
    </div>
  )
}

function renderProvider(file: File) {
  return render(
    <ConversationsProvider organizationId={ORG}>
      <Harness file={file} />
    </ConversationsProvider>,
  )
}

async function waitForActive() {
  await waitFor(() => expect(screen.getByTestId('active').textContent).toBe(CONV))
}

describe('ConversationsProvider pending attachments', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mocks.useAuth.mockReturnValue({
      isLoaded: true,
      isSignedIn: true,
      getToken: vi.fn().mockResolvedValue('token'),
    })
    mocks.fetchConversations.mockResolvedValue({
      items: [conversation()],
      total: 1,
      page: 1,
      pageSize: 50,
    })
    mocks.fetchMessages.mockResolvedValue({ items: [], total: 0, page: 1, pageSize: 100 })
    mocks.getOrCreate.mockResolvedValue(conversation())
  })

  it('uploads a picked file on attach and records the stored type from the response', async () => {
    mocks.upload.mockResolvedValue(attachmentDto())
    renderProvider(pdfFile())
    await waitForActive()

    fireEvent.click(screen.getByRole('button', { name: 'attach' }))

    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))
    await waitFor(() =>
      expect(screen.getByTestId('tray').textContent).toBe(
        'ready:application/pdf:not-analysable',
      ),
    )
    expect(mocks.upload).toHaveBeenCalledTimes(1)
    expect(mocks.upload.mock.calls[0][0]).toBe(ORG)
    expect(mocks.upload.mock.calls[0][1]).toBe(CONV)
    expect(mocks.upload.mock.calls[0][2]).toBeInstanceOf(File)
    expect(mocks.upload.mock.calls[0][3]).toBe('invoice.pdf')
  })

  it('reports not-ready while a chip is uploading and ready once it is stored', async () => {
    let resolveUpload!: (value: ConversationAttachmentDto) => void
    mocks.upload.mockImplementation(
      () =>
        new Promise<ConversationAttachmentDto>((resolve) => {
          resolveUpload = resolve
        }),
    )
    renderProvider(pdfFile())
    await waitForActive()

    fireEvent.click(screen.getByRole('button', { name: 'attach' }))

    await waitFor(() => expect(screen.getByTestId('tray').textContent).toBe('uploading:none:not-analysable'))
    expect(screen.getByTestId('ready').textContent).toBe('NOT_READY')

    resolveUpload(attachmentDto())

    await waitFor(() => expect(screen.getByTestId('ready').textContent).toBe('READY'))
    expect(screen.getByTestId('tray').textContent).toBe('ready:application/pdf:not-analysable')
  })

  it('reuses the same bytes when a failed chip is retried', async () => {
    mocks.upload.mockRejectedValueOnce(new Error('network')).mockResolvedValueOnce(attachmentDto())
    renderProvider(pdfFile())
    await waitForActive()

    fireEvent.click(screen.getByRole('button', { name: 'attach' }))
    await waitFor(() => expect(screen.getByTestId('tray').textContent).toContain('failed'))

    const firstBytes = mocks.upload.mock.calls[0][2]
    fireEvent.click(screen.getByRole('button', { name: 'retry' }))

    await waitFor(() => expect(screen.getByTestId('tray').textContent).toContain('ready'))
    expect(mocks.upload).toHaveBeenCalledTimes(2)
    expect(mocks.upload.mock.calls[1][2]).toBe(firstBytes)
  })

  it('removes a chip without clearing the composed text', async () => {
    mocks.upload.mockResolvedValue(attachmentDto())
    renderProvider(pdfFile())
    await waitForActive()

    fireEvent.change(screen.getByLabelText('message'), {
      target: { value: 'Does this saree match?' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'attach' }))
    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('1'))

    fireEvent.click(screen.getByRole('button', { name: 'remove' }))

    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('0'))
    expect(screen.getByLabelText('message')).toHaveValue('Does this saree match?')
  })

  it('refuses a sixth file client-side and uploads only five', async () => {
    mocks.upload.mockResolvedValue(attachmentDto())
    renderProvider(pdfFile())
    await waitForActive()

    fireEvent.click(screen.getByRole('button', { name: 'attach-six' }))

    await waitFor(() => expect(screen.getByTestId('count').textContent).toBe('5'))
    expect(mocks.upload).toHaveBeenCalledTimes(5)
    await waitFor(() =>
      expect(screen.getByTestId('refusals').textContent).toBe(
        'A message may carry at most 5 attachments.',
      ),
    )
  })

  it('decides analysability from the upload response, never the declared type', async () => {
    // The picked file declared a PDF; the response says the stored bytes are JPEG. The chip must
    // follow the response, and the upload must still have happened (it never blocks).
    mocks.upload.mockResolvedValue(attachmentDto({ contentType: 'image/jpeg', fileName: 'page.jpg' }))
    renderProvider(pdfFile())
    await waitForActive()

    fireEvent.click(screen.getByRole('button', { name: 'attach' }))

    await waitFor(() =>
      expect(screen.getByTestId('tray').textContent).toBe('ready:image/jpeg:analysable'),
    )
    expect(mocks.upload).toHaveBeenCalledTimes(1)
  })

  it('sends the held attachment ids with a client message id stable across a retry', async () => {
    mocks.send.mockRejectedValue(new Error('offline'))
    renderProvider(pdfFile())
    await waitForActive()

    fireEvent.change(screen.getByLabelText('message'), {
      target: { value: 'Here is the invoice' },
    })
    fireEvent.click(screen.getByRole('button', { name: 'send' }))
    await waitFor(() => expect(mocks.send).toHaveBeenCalledTimes(1))

    fireEvent.click(screen.getByRole('button', { name: 'send' }))
    await waitFor(() => expect(mocks.send).toHaveBeenCalledTimes(2))

    const first = mocks.send.mock.calls[0]
    const second = mocks.send.mock.calls[1]
    expect(first[2]).toBe('Here is the invoice')
    expect(first[3]).toEqual(['att-held'])
    expect(second[3]).toEqual(['att-held'])
    expect(typeof first[4]).toBe('string')
    expect(first[4]).toBeTruthy()
    expect(second[4]).toBe(first[4])
  })
})
