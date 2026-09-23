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

/**
 * The customer's own words, relayed from WhatsApp.
 *
 * The associate has to read this as the customer talking and not as an agent's summary, so the
 * block names the channel, spaces the handle it arrived with, and keeps its own surface.
 */
describe('client message block', () => {
  const client = (overrides: Partial<ContentBlock> = {}): ContentBlock => ({
    type: 'client_message',
    from: '94763475058',
    text: 'Do you still have the emerald green saree?',
    ...overrides,
  })

  it('names the customer and the channel the message arrived on', () => {
    const { container } = render(<BlockRenderer block={client()} />)

    expect(screen.getByText('Customer')).toBeInTheDocument()
    // Queried by slot: the official glyph carries its own <title>WhatsApp</title>, so the channel's
    // name is not the only node in the block holding that word.
    const channel = container.querySelector('[data-slot="client-channel"]') as HTMLElement
    expect(channel).toHaveTextContent('WhatsApp')
    expect(screen.getByText('Do you still have the emerald green saree?')).toBeInTheDocument()
  })

  it('spaces the raw WhatsApp handle the way the rest of the dashboard spaces numbers', () => {
    render(<BlockRenderer block={client({ from: '94763475058' })} />)

    // The channel hands over `94763475058`; a wall of eleven digits is unreadable in a thread.
    expect(screen.getByText('+94 76 34 75 058')).toBeInTheDocument()
  })

  it('leaves a handle that is not Sri Lankan exactly as the channel gave it', () => {
    render(<BlockRenderer block={client({ from: '+15551234567' })} />)

    // Forcing the local country code onto it would invent an address the customer does not have.
    expect(screen.getByText('+15551234567')).toBeInTheDocument()
  })

  it('renders without a handle rather than naming the customer "WhatsApp"', () => {
    render(<BlockRenderer block={client({ from: undefined })} />)

    expect(screen.getByText('Customer')).toBeInTheDocument()
    expect(screen.getByText('Do you still have the emerald green saree?')).toBeInTheDocument()
  })

  it('shows the channel mark beside the channel name, not in place of it', () => {
    const { container } = render(<BlockRenderer block={client()} />)

    // The mark is the official WhatsApp glyph, drawn inside the brand-green badge.
    const mark = container.querySelector('svg path')
    expect(mark).not.toBeNull()
    expect(container.querySelector('.bg-whatsapp')).not.toBeNull()
  })

  it('keeps the customer\'s words readable on the staff bubble', () => {
    const { container } = render(<BlockRenderer block={client()} tone="own" />)

    // The staff bubble fills with `primary` and sets `text-primary-foreground`. A card that
    // inherited that ink would paint white words on its own white surface and say nothing at all,
    // so the card states the colour its surface carries.
    const card = container.querySelector('figure') as HTMLElement
    expect(card.className).toContain('text-card-foreground')
  })

  it('keeps the customer\'s line breaks, which are part of what they said', () => {
    const { container } = render(
      <BlockRenderer block={client({ text: 'Hello,\nDo you have it in green?' })} />,
    )

    const words = container.querySelector('figure p') as HTMLElement
    expect(words.textContent).toBe('Hello,\nDo you have it in green?')
    expect(words.className).toContain('whitespace-pre-wrap')
  })
})

/**
 * Entity mentions as pills (ADR-019).
 *
 * A mention is how staff point the resolver at an exact customer. Drawn as plain text it reads as
 * stray punctuation, so the block lifts it into a pill — and the pill has to be readable on both
 * bubble surfaces, because a staff note is typed into the `primary`-filled bubble.
 */
describe('entity mentions', () => {
  function pills(container: HTMLElement): HTMLElement[] {
    return Array.from(container.querySelectorAll<HTMLElement>('[data-slot="mention"]'))
  }

  it('lifts a customer mention out of the prose', () => {
    const { container } = render(
      <BlockRenderer block={{ type: 'text', text: '@Samantha Arias — any events?' }} />,
    )

    const [pill] = pills(container)
    expect(pill).toHaveTextContent('@Samantha Arias')
    expect(pill).toHaveAttribute('title', 'Customer mention: Samantha Arias')
    // The prose around the pill is untouched.
    expect(container.textContent).toBe('@Samantha Arias — any events?')
  })

  it('stops the pill where the resolver stopped, leaving the prose behind it alone', () => {
    const { container } = render(
      <BlockRenderer block={{ type: 'text', text: '@jason smith, dropped by to browse' }} />,
    )

    const [pill] = pills(container)
    expect(pill).toHaveTextContent('@jason smith')
    // The comma ends the name; everything after it stays the sentence it was.
    expect(container.textContent).toBe('@jason smith, dropped by to browse')
  })

  it('trims the prose a greedy capture swallowed, exactly as the resolver trims it', () => {
    const { container } = render(
      <BlockRenderer block={{ type: 'text', text: '@jason smith dropped by' }} />,
    )

    // No delimiter, so the tail is trimmed only because "by" and "dropped" are stop words.
    expect(pills(container)[0]).toHaveTextContent('@jason smith')
  })

  it('lifts a phone mention and sets it in figures that line up', () => {
    const { container } = render(
      <BlockRenderer block={{ type: 'text', text: 'reach her on #0771234567 please' }} />,
    )

    const [pill] = pills(container)
    expect(pill).toHaveTextContent('#0771234567')
    expect(pill).toHaveAttribute('data-mention-kind', 'phone')
    expect(pill.className).toContain('tabular-nums')
  })

  it('tints the pill for the bubble it sits in, rather than vanishing into it', () => {
    const block: ContentBlock = { type: 'text', text: '@Samantha Arias' }

    const { container: onCard } = render(<BlockList blocks={[block]} />)
    const { container: onStaff } = render(<BlockList blocks={[block]} tone="own" />)

    expect(pills(onCard)[0].className).toContain('text-primary')
    expect(pills(onStaff)[0].className).toContain('text-primary-foreground')
  })

  it('lifts mentions inside a suggestion, which is drafted text too', () => {
    const { container } = render(
      <BlockRenderer
        block={{ type: 'suggestion', text: 'Tell @Samantha Arias the saree is back.' }}
      />,
    )

    expect(pills(container)).toHaveLength(1)
    expect(pills(container)[0]).toHaveTextContent('@Samantha Arias')
  })

  it('leaves an escaped token as the text it was typed as', () => {
    const { container } = render(
      <BlockRenderer block={{ type: 'text', text: 'type \\@ to mention someone' }} />,
    )

    expect(pills(container)).toHaveLength(0)
    expect(container.textContent).toBe('type \\@ to mention someone')
  })

  it("leaves the customer's own words alone, because a mention is the staff's affordance", () => {
    const { container } = render(
      <BlockRenderer
        block={{ type: 'client_message', from: '94763475058', text: 'Is @silksbyamelia yours?' }}
      />,
    )

    // A customer's at-sign is a handle they typed, not an entity any lookup read.
    expect(pills(container)).toHaveLength(0)
    expect(container).toHaveTextContent('Is @silksbyamelia yours?')
  })
})

/**
 * A recommendation set is a row, not a column.
 *
 * Elle emits its `piece` blocks back to back and its `look` blocks after them, so the old
 * one-block-per-line list stacked a curated set into a single visible card. These tests pin the
 * grouping, the row's `auto-fit` sizing, and the definite bubble width the sizing depends on: with
 * a shrink-to-fit bubble, a grid resolves to a single track and the pieces stack again.
 */
describe('product tiles', () => {
  const piece = (name: string): ContentBlock => ({
    type: 'piece',
    name,
    price: 1000,
    size: 'M',
    stock: 2,
  })

  function tileGrids(container: HTMLElement): HTMLElement[] {
    return Array.from(container.querySelectorAll<HTMLElement>('[data-slot="tile-grid"]'))
  }

  /** An Elle message, so the bubble renders through the same path the Salon and drawer use. */
  function agentMessage(blocks: ContentBlock[]): ChatMessage {
    return {
      id: 'm-tiles',
      conversationId: 'c1',
      authorKind: 'Agent',
      agentKey: 'elle',
      authorUserId: null,
      kind: 'Note',
      contentBlocks: blocks,
      contentHash: null,
      replyToMessageId: null,
      status: 'Published',
      createdAt: '2026-09-23T09:00:00.000Z',
    } as unknown as ChatMessage
  }

  it('lays consecutive pieces out in one row instead of stacking them', () => {
    const { container } = render(
      <BlockList blocks={[piece('One'), piece('Two'), piece('Three')]} />,
    )

    const grids = tileGrids(container)
    expect(grids).toHaveLength(1)
    expect(within(grids[0]).getByText('One')).toBeInTheDocument()
    expect(within(grids[0]).getByText('Two')).toBeInTheDocument()
    expect(within(grids[0]).getByText('Three')).toBeInTheDocument()
    // `auto-fit` counts columns against the width the bubble hands the grid.
    expect(grids[0].className).toContain(
      'grid-cols-[repeat(auto-fit,minmax(min(100%,11rem),1fr))]',
    )
  })

  it('keeps a run of many tiles in one grid, so a long set wraps rather than breaks the row', () => {
    const { container } = render(
      <BlockList
        blocks={[piece('One'), piece('Two'), piece('Three'), piece('Four'), piece('Five')]}
      />,
    )

    const grids = tileGrids(container)
    expect(grids).toHaveLength(1)
    expect(within(grids[0]).getByText('Five')).toBeInTheDocument()
  })

  it('puts a look with its own photograph in the same row as the pieces', () => {
    const { container } = render(
      <BlockList
        blocks={[
          { ...piece('Emerald Green Georgette Saree'), imageUrl: 'https://cdn/saree.jpg' },
          {
            type: 'look',
            name: 'Galle Sunset Soiree',
            imageUrl: 'https://cdn/look.jpg',
            text: 'Balance the green with tonal gold.',
          },
        ]}
      />,
    )

    const grids = tileGrids(container)
    expect(grids).toHaveLength(1)
    expect(within(grids[0]).getByText('Emerald Green Georgette Saree')).toBeInTheDocument()
    expect(within(grids[0]).getByText('Balance the green with tonal gold.')).toBeInTheDocument()
  })

  it('drops a look\'s plate when the photograph is one of the pieces\' own', () => {
    const { container } = render(
      <BlockList
        blocks={[
          { ...piece('Emerald Green Georgette Saree'), imageUrl: 'https://cdn/saree.jpg' },
          {
            type: 'look',
            name: 'Look: Boutique Collection',
            // Elle's composer borrowed the first matched piece's photo as the look's own, so the
            // Salon showed one saree twice: once as the piece, once as the look.
            imageUrl: 'https://cdn/saree.jpg',
            text: 'Keep the silhouette clean and let the fabric do the talking.',
          },
        ]}
      />,
    )

    // One photograph, on the piece: the look keeps its words and loses the copy.
    expect(container.querySelectorAll('img')).toHaveLength(1)
    expect(screen.getByText('Keep the silhouette clean and let the fabric do the talking.')).toBeInTheDocument()
    expect(screen.getByText('Look: Boutique Collection')).toBeInTheDocument()
  })

  it('reads a look without a photograph as its styling note, never as a blank plate', () => {
    const { container } = render(
      <BlockList
        blocks={[
          {
            type: 'look',
            name: 'Look: Boutique Collection',
            text: 'Anchor it with a muted gold blouse in a matte finish.',
          },
        ]}
      />,
    )

    // No plate to fill: the note is the block, and it carries the look's name at the row's width.
    expect(container.querySelector('img')).toBeNull()
    expect(tileGrids(container)).toHaveLength(0)
    expect(screen.getByText('Look: Boutique Collection')).toBeInTheDocument()
    expect(screen.getByText('Anchor it with a muted gold blouse in a matte finish.')).toBeInTheDocument()
  })

  it('does not widen the bubble for a look whose photograph was borrowed', () => {
    const message = agentMessage([
      { ...piece('One'), imageUrl: 'https://cdn/one.jpg' },
      { type: 'look', name: 'Look', imageUrl: 'https://cdn/one.jpg', text: 'Wear it with gold.' },
    ])

    const { container } = render(<MessageBubble message={message} isOwn={false} />)

    // The look is a note after normalisation, so there is no row for the bubble to size against.
    const bubble = container.querySelector('[data-slot="message-bubble"]') as HTMLElement
    expect(bubble.className).not.toContain('w-full')
  })

  it('starts a new row when prose separates two pieces, so a caption stays with its tile', () => {
    const { container } = render(
      <BlockList
        blocks={[piece('One'), { type: 'text', text: 'It comes in three sizes.' }, piece('Two')]}
      />,
    )

    const grids = tileGrids(container)
    expect(grids).toHaveLength(2)
    expect(within(grids[0]).getByText('One')).toBeInTheDocument()
    expect(within(grids[0]).queryByText('Two')).not.toBeInTheDocument()
    expect(within(grids[1]).getByText('Two')).toBeInTheDocument()
  })

  it('gives a message that carries a tile row the bubble\'s full width, which the row sizes against', () => {
    const { container } = render(<MessageBubble message={agentMessage([piece('One'), piece('Two')])} isOwn={false} />)

    const bubble = container.querySelector('[data-slot="message-bubble"]') as HTMLElement
    expect(bubble.className).toContain('w-full')
    // The photograph is what the paper-thin case lacks: two pieces with no image at all still get
    // a row, because the bubble's own width is what the grid counts its columns against.
    expect(tileGrids(container)).toHaveLength(1)
  })

  it('leaves a lone tile shrink-to-fit, so the card is capped rather than the bubble', () => {
    const { container } = render(<MessageBubble message={agentMessage([piece('Only')])} isOwn={false} />)

    const bubble = container.querySelector('[data-slot="message-bubble"]') as HTMLElement
    expect(bubble.className).not.toContain('w-full')
    expect(tileGrids(container)[0].className).toContain('max-w-64')
  })

  it('leaves a text-only bubble shrink-to-fit', () => {
    const { container } = render(
      <MessageBubble message={agentMessage([{ type: 'text', text: 'Three pieces match.' }])} isOwn={false} />,
    )

    const bubble = container.querySelector('[data-slot="message-bubble"]') as HTMLElement
    expect(bubble.className).not.toContain('w-full')
  })

  it('renders a piece without a photograph as a stated absence, not a broken image', () => {
    render(<BlockRenderer block={piece('Ivory Organza')} />)

    expect(screen.getByText('No photograph')).toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
  })

  it('does not cap the row when tiles share it, so two cards fill the width between them', () => {
    const { container } = render(<BlockList blocks={[piece('One'), piece('Two')]} />)

    const grids = tileGrids(container)
    expect(grids).toHaveLength(1)
    expect(grids[0].className).not.toContain('max-w-64')
  })
})
