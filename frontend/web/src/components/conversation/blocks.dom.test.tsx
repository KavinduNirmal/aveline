import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { ContentBlock } from './blocks'

// The bytes must come through the shared authenticated axios client (the one that attaches the
// Clerk bearer token), so the client is mocked here and asserted on below. `vi.hoisted` keeps the
// spy reference stable across the hoisted `vi.mock` factory.
const { apiGet } = vi.hoisted(() => ({ apiGet: vi.fn() }))

vi.mock('@/lib/api', () => ({
  apiClient: { get: apiGet },
}))

import { BlockList, BlockRenderer } from './blocks'
import { MessageBubble } from './MessageBubble'
import type { ChatMessage } from '@/contexts/ConversationsContext'

/** The authenticated serve route a stored attachment carries in `block.url` (strategy §3.8). */
const STORED_ROUTE =
  '/api/v1/orgs/org-1/conversations/conv-1/attachments'

function attachmentBlock(
  attachmentId: string,
  overrides: Partial<ContentBlock> = {},
): ContentBlock {
  return {
    type: 'attachment',
    attachmentId,
    url: `${STORED_ROUTE}/${attachmentId}`,
    contentType: 'image/png',
    fileName: 'dress.png',
    sizeBytes: 12 * 1024,
    ...overrides,
  }
}

function pdfBlock(attachmentId: string): ContentBlock {
  return attachmentBlock(attachmentId, {
    contentType: 'application/pdf',
    fileName: 'menu.pdf',
    sizeBytes: 2 * 1024 * 1024,
  })
}

// jsdom implements neither `URL.createObjectURL` nor `URL.revokeObjectURL`; the component needs
// both, so they are stubbed and asserted on. `createObjectURL` hands back a distinct blob URL per
// call so the revoke assertion names the exact URL the mounted block minted.
let objectUrlSeq = 0
const createObjectURL = vi.fn(() => `blob:attachment-${++objectUrlSeq}`)
const revokeObjectURL = vi.fn()

function stubObjectUrls() {
  objectUrlSeq = 0
  createObjectURL.mockClear()
  revokeObjectURL.mockClear()
  Object.defineProperty(URL, 'createObjectURL', {
    configurable: true,
    writable: true,
    value: createObjectURL,
  })
  Object.defineProperty(URL, 'revokeObjectURL', {
    configurable: true,
    writable: true,
    value: revokeObjectURL,
  })
}

beforeEach(() => {
  apiGet.mockReset()
  stubObjectUrls()
})

describe('AttachmentBlock rendering and access', () => {
  it('fetches an image through the authenticated client, not through an <img src> of the stored route', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })

    const block = attachmentBlock('a-image')
    render(<BlockRenderer block={block} />)

    const image = await screen.findByRole('img', { name: 'dress.png' })

    // The bytes were requested from the stored authenticated route, through the shared client,
    // as an `arraybuffer` — the strategy's option (a), not a minted token URL.
    expect(apiGet).toHaveBeenCalledTimes(1)
    expect(apiGet).toHaveBeenCalledWith(`${STORED_ROUTE}/a-image`, {
      responseType: 'arraybuffer',
    })
    const requestedUrl = apiGet.mock.calls[0][0] as string
    expect(requestedUrl).not.toMatch(/token/i)

    // The element the browser actually loads is an object URL, never the stored route.
    expect(image.getAttribute('src')).toMatch(/^blob:/)
    expect(image.getAttribute('src')).not.toBe(block.url)
    expect(image.getAttribute('src')).not.toContain('/attachments/')
  })

  it('renders a thumbnail button that opens a shadcn dialog viewer and notifies the thread', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })
    const onOpenAttachment = vi.fn()

    render(<BlockList blocks={[attachmentBlock('a-open')]} onOpenAttachment={onOpenAttachment} />)

    const image = await screen.findByRole('img', { name: 'dress.png' })
    await userEvent.click(image)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByRole('img', { name: 'dress.png' })).toBeInTheDocument()
    expect(onOpenAttachment).toHaveBeenCalledWith('a-open')
  })

  it('fetches each attachment id once, however many blocks show it', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })

    render(
      <BlockList
        blocks={[attachmentBlock('a-cached'), attachmentBlock('a-cached')]}
      />,
    )

    await waitFor(() => expect(screen.getAllByRole('img')).toHaveLength(2))
    // Two mounts, one authenticated request: the bytes are cached by attachment id.
    expect(apiGet).toHaveBeenCalledTimes(1)
  })

  it('revokes the object URL when the block unmounts', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })

    const { unmount } = render(<BlockRenderer block={attachmentBlock('a-revoke')} />)
    await screen.findByRole('img', { name: 'dress.png' })

    expect(createObjectURL).toHaveBeenCalledTimes(1)
    expect(revokeObjectURL).not.toHaveBeenCalled()

    unmount()

    expect(revokeObjectURL).toHaveBeenCalledTimes(1)
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:attachment-1')
  })

  it('offers Open and Download for a PDF and previews it in an <embed> best-effort', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(8),
      headers: { 'content-type': 'application/pdf' },
    })

    render(<BlockRenderer block={pdfBlock('a-pdf')} />)

    const open = await screen.findByRole('button', { name: /open/i })
    const download = screen.getByRole('link', { name: /download/i })

    expect(download).toHaveAttribute('download', 'menu.pdf')
    expect(download.getAttribute('href')).toMatch(/^blob:/)

    await userEvent.click(open)

    const dialog = await screen.findByRole('dialog')
    expect(within(dialog).getByTitle('menu.pdf')).toBeInTheDocument()
    expect(within(dialog).getByText('2.0 MB')).toBeInTheDocument()
  })

  it('falls back to the file name and size when the fetch rejects, never a broken image', async () => {
    apiGet.mockRejectedValue(new Error('403 from the conversation policy'))

    render(<BlockRenderer block={attachmentBlock('a-failed')} />)

    const chip = await waitFor(() => {
      const fallback = document.querySelector('[data-state="unavailable"]')
      expect(fallback).not.toBeNull()
      return fallback as HTMLElement
    })

    expect(chip).toHaveTextContent('dress.png')
    expect(chip).toHaveTextContent('12 KB')
    // No broken <img> and no affordance that would open one.
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /open/i })).not.toBeInTheDocument()
  })

  it('never fetches without both an attachment id and a stored route', () => {
    render(<BlockRenderer block={attachmentBlock('a-noop', { attachmentId: undefined })} />)
    render(<BlockRenderer block={attachmentBlock('a-noop-2', { url: undefined })} />)

    expect(apiGet).not.toHaveBeenCalled()
    expect(screen.getAllByText('dress.png')).toHaveLength(2)
  })

  it('captions an opaque generated filename as "Photo" and keeps the real name on the control', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })
    const generated = '8b420c6fbc31eea2d64a5e2b1c3d4e5f.jpg'

    render(<BlockRenderer block={attachmentBlock('a-hash', { fileName: generated })} />)

    const image = await screen.findByRole('img', { name: generated })
    const plate = image.closest('button') as HTMLButtonElement

    // The wall of hex is not the caption; it stays reachable as the tooltip and in the viewer.
    expect(within(plate).getByText('Photo')).toBeInTheDocument()
    expect(within(plate).queryByText(generated)).not.toBeInTheDocument()
    expect(plate).toHaveAttribute('title', generated)
  })

  it('keeps a human filename in the caption', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })

    render(<BlockRenderer block={attachmentBlock('a-human')} />)

    const image = await screen.findByRole('img', { name: 'dress.png' })
    const plate = image.closest('button') as HTMLButtonElement
    expect(within(plate).getByText('dress.png')).toBeInTheDocument()
  })

  it('tints the plate with the bubble ink on the staff surface, not a background-coloured hole', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })

    render(<BlockList blocks={[attachmentBlock('a-own')]} tone="own" />)

    const image = await screen.findByRole('img', { name: 'dress.png' })
    const plate = image.closest('button') as HTMLButtonElement
    expect(plate.className).toContain('text-primary-foreground')
    expect(plate.className).not.toContain('bg-background')
  })

  it('puts a staff message\'s image attachment on the staff surface', async () => {
    apiGet.mockResolvedValue({
      data: new ArrayBuffer(4),
      headers: { 'content-type': 'image/png' },
    })
    const message = {
      id: 'm-own',
      conversationId: 'c1',
      authorKind: 'Staff',
      agentKey: null,
      authorUserId: 'u1',
      kind: 'Note',
      contentBlocks: [attachmentBlock('a-msg')],
      contentHash: null,
      replyToMessageId: null,
      status: 'Published',
      createdAt: new Date().toISOString(),
    } as unknown as ChatMessage

    render(<MessageBubble message={message} isOwn />)

    const image = await screen.findByRole('img', { name: 'dress.png' })
    const plate = image.closest('button') as HTMLButtonElement
    expect(plate.className).toContain('text-primary-foreground')
  })
})
