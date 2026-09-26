import { useEffect, useState, type ReactNode } from 'react'
import { Maximize2 } from 'lucide-react'

import { WhatsAppIcon } from '@/components/dashboard/BrandIcons'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Separator } from '@/components/ui/separator'
import { Spinner } from '@/components/ui/spinner'
import { apiClient } from '@/lib/api'
import { formatLkPhone } from '@/lib/boutique'
import { cn } from '@/lib/utils'
import { ActionableBlock } from './ActionableBlock'
import { blockTitle, type BlockActionBridge } from './blockActions'
import { MentionText } from './Mentions'
import { isTileBlock, withoutBorrowedLookImages } from './tileBlocks'

/** An option in a customer-resolution `choice` block. */
export interface ChoiceOption {
  customerId: string
  fullName?: string | null
  status?: string | null
  lastVisitAt?: string | null
}

/** One citation in a `sources` block: the page an answer was grounded in (ADR-025). */
export interface SourceItem {
  title: string
  /** The page to open. Absent when the answer was grounded in something unaddressable. */
  url?: string
  heading?: string
}

/** A single typed content block from a message's `contentBlocks` array. */
export interface ContentBlock {
  type: string
  text?: string
  itemId?: string
  name?: string
  price?: number
  size?: string
  stock?: number
  imageUrl?: string
  columns?: string[]
  rows?: string[][]
  approvalId?: string
  orderId?: string
  amount?: number
  reason?: string
  from?: string
  status?: string
  prompt?: string
  options?: ChoiceOption[]
  /** A `sources` block's citations. */
  items?: SourceItem[]
  /** The thread's own `attachment` block (D8). */
  attachmentId?: string
  url?: string
  contentType?: string
  fileName?: string
  sizeBytes?: number
  width?: number
  height?: number
  [key: string]: unknown
}

import type { Persona } from './persona'

/**
 * Which bubble an attachment sits in. The surface has to flip with it: a card painted for the
 * neutral agent bubble reads as a white patch on the staff bubble's primary fill.
 */
type AttachmentTone = 'own' | 'other'

interface BlockRendererProps {
  block: ContentBlock
  onSignOff?: (approved: boolean) => void
  onSelectCustomer?: (customerId: string) => void
  /** Called with the attachment id when a thread attachment is opened for viewing. */
  onOpenAttachment?: (attachmentId: string) => void
  persona?: Persona | null
  tone?: AttachmentTone
  /** The message this block belongs to; the action rail names it for regenerate and share. */
  messageId?: string
  /** The thread behind the action rail. Absent draws the block with no rail at all. */
  bridge?: BlockActionBridge
}

/** Renders a single content block by type. */
export function BlockRenderer({
  block,
  onSignOff,
  onSelectCustomer,
  onOpenAttachment,
  persona,
  tone = 'other',
  messageId,
  bridge,
}: BlockRendererProps) {
  switch (block.type) {
    case 'text':
      // Staff point the resolver at an exact customer with a mention, so their own words are the
      // one place a `@name` in the transcript is an entity rather than punctuation (ADR-019).
      return (
        <MentionText
          text={block.text ?? ''}
          tone={tone}
          className="whitespace-pre-wrap text-sm leading-relaxed"
        />
      )
    case 'piece':
      return (
        <PieceBlock
          block={block}
          onSignOff={onSignOff}
          persona={persona}
          messageId={messageId}
          bridge={bridge}
        />
      )
    case 'at_a_glance':
      return <AtAGlanceBlock block={block} onSignOff={onSignOff} persona={persona} />
    case 'sign_off':
      return <SignOffBlock block={block} onSignOff={onSignOff} persona={persona} />
    case 'client_message':
      return <ClientMessageBlock block={block} onSignOff={onSignOff} persona={persona} />
    case 'payment':
      return <PaymentBlock block={block} onSignOff={onSignOff} persona={persona} />
    case 'courier':
      return <CourierBlock block={block} onSignOff={onSignOff} persona={persona} />
    case 'suggestion':
      return (
        <SuggestionBlock
          block={block}
          onSignOff={onSignOff}
          persona={persona}
          tone={tone}
          messageId={messageId}
          bridge={bridge}
        />
      )
    case 'look':
      return (
        <LookBlock
          block={block}
          onSignOff={onSignOff}
          persona={persona}
          tone={tone}
          messageId={messageId}
          bridge={bridge}
        />
      )
    case 'choice':
      return <ChoiceBlock block={block} onSelectCustomer={onSelectCustomer} />
    case 'sources':
      return <SourcesBlock block={block} />
    case 'attachment':
      return (
        <AttachmentBlock
          key={block.attachmentId}
          block={block}
          onOpenAttachment={onOpenAttachment}
          tone={tone}
        />
      )
    default:
      return null
  }
}

/**
 * The citations behind a handbook-grounded answer (ADR-025).
 *
 * Rendered as links rather than prose, which is exactly why the agent emits a structured `sources`
 * block: a frontend cannot reliably find a link inside model-written text. A plain anchor rather
 * than a router link, because the documentation is its own public layout and opening it in a new
 * tab leaves the Salon exactly where it was.
 */
function SourcesBlock({ block }: { block: ContentBlock }) {
  const items = (block.items ?? []).filter((item) => item?.title)
  if (items.length === 0) {
    return null
  }

  return (
    <div
      className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted-foreground"
      data-testid="sources-block"
    >
      <span className="text-[10px] font-medium uppercase tracking-wide">Sources</span>
      {items.map((item, index) =>
        item.url ? (
          <a
            key={`${item.title}-${index}`}
            href={item.url}
            target="_blank"
            rel="noreferrer"
            className="underline underline-offset-2 hover:text-foreground"
          >
            {item.title}
          </a>
        ) : (
          <span key={`${item.title}-${index}`}>{item.title}</span>
        ),
      )}
    </div>
  )
}

/** A customer-resolution choice: pick which customer you meant (Issue #161). */
function ChoiceBlock({
  block,
  onSelectCustomer,
}: {
  block: ContentBlock
  onSelectCustomer?: (customerId: string) => void
}) {
  const options = block.options ?? []
  if (options.length === 0) return null
  return (
    <div className="space-y-2">
      {block.prompt && <p className="whitespace-pre-wrap text-sm leading-relaxed">{block.prompt}</p>}
      <div className="space-y-1.5">
        {options.map((option) => (
          <button
            key={option.customerId}
            type="button"
            onClick={() => onSelectCustomer?.(option.customerId)}
            className="flex w-full items-center justify-between gap-2 rounded-lg border px-3 py-2 text-left text-sm transition-colors hover:bg-muted disabled:opacity-50"
            disabled={!onSelectCustomer}
          >
            <span className="min-w-0">
              <span className="block truncate font-medium">
                {option.fullName ?? 'Customer'}
              </span>
              {option.status && (
                <span className="text-xs capitalize text-muted-foreground">{option.status}</span>
              )}
            </span>
            {option.lastVisitAt && (
              <span className="shrink-0 text-xs text-muted-foreground">
                {new Date(option.lastVisitAt).toLocaleDateString()}
              </span>
            )}
          </button>
        ))}
      </div>
    </div>
  )
}

/**
 * One catalogue piece as a tile, not a panel.
 *
 * An answer routinely carries several pieces, and the old full-width card turned each one into a
 * band that pushed the next off-screen, so a curated set read as a single recommendation. The tile
 * is deliberately narrow: a 4:3 plate, the name, size and stock, then the money. `BlockList` is what
 * keeps it thin, by gridding a run of tiles instead of stacking them.
 *
 * Its rail is one segment wide — an item block forwards, and nothing else — so the tile's foot is a
 * single joined strip rather than a row of chips.
 */
function PieceBlock({ block, messageId, bridge }: BlockRendererProps) {
  const name = block.name ?? 'Piece'
  return (
    <ActionableBlock
      block={block}
      messageId={messageId}
      bridge={bridge}
      title={blockTitle(block)}
      className="rounded-xl border border-border/70 bg-card"
    >
      {block.imageUrl ? (
        <img
          loading="lazy"
          decoding="async"
          src={block.imageUrl}
          alt={name}
          className="aspect-4/3 w-full bg-muted object-cover"
        />
      ) : (
        // A missing photograph is a state, not a broken image or a stretched blank.
        <div className="flex aspect-4/3 w-full items-center justify-center bg-muted text-[10px] text-muted-foreground">
          No photograph
        </div>
      )}
      <div className="flex flex-1 flex-col gap-1 p-2.5">
        <p className="line-clamp-2 font-serif text-xs font-medium leading-snug" title={name}>
          {name}
        </p>
        <div className="flex flex-wrap items-center gap-x-1.5 gap-y-1">
          {block.size && (
            <span className="min-w-0 truncate text-[10px] text-muted-foreground">
              Size {block.size}
            </span>
          )}
          {typeof block.stock === 'number' && (
            <Badge variant="secondary" className="h-4 px-1.5 text-[10px] font-normal">
              {block.stock} in stock
            </Badge>
          )}
        </div>
        {/* The money is the last line and sits on the floor of the tile, so a row's prices line up
            even when one name wraps to a second line and the others do not. */}
        {typeof block.price === 'number' && (
          <p className="mt-auto pt-1 text-xs font-semibold tabular-nums text-primary">
            LKR {block.price.toLocaleString()}
          </p>
        )}
      </div>
    </ActionableBlock>
  )
}

function AtAGlanceBlock({ block }: BlockRendererProps) {
  const columns = block.columns ?? []
  const rows = block.rows ?? []
  if (columns.length === 0) return null
  return (
    <div className="overflow-hidden rounded-lg border">
      <table className="w-full text-left text-xs">
        <thead className="bg-muted">
          <tr>
            {columns.map((col) => (
              <th key={col} className="px-3 py-2 font-medium text-muted-foreground">
                {col}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row, i) => (
            <tr key={i} className="border-t">
              {row.map((cell, j) => (
                <td key={j} className="px-3 py-2">
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function SignOffBlock({ block, onSignOff }: BlockRendererProps) {
  return (
    <Card className="border-primary/20 bg-primary/5">
      <CardHeader className="p-3 pb-1">
        <CardTitle className="text-sm">Approval needed</CardTitle>
        {block.reason && (
          <CardDescription className="text-xs">{block.reason}</CardDescription>
        )}
      </CardHeader>
      <CardContent className="p-3 pt-2">
        {typeof block.amount === 'number' && (
          <p className="mb-2 text-sm font-semibold text-primary">
            LKR {block.amount.toLocaleString()}
          </p>
        )}
        {onSignOff && (
          <div className="flex gap-2">
            <Button size="sm" onClick={() => onSignOff(true)}>
              Approve
            </Button>
            <Button size="sm" variant="outline" onClick={() => onSignOff(false)}>
              Reject
            </Button>
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * The customer's handle as a person reads it.
 *
 * WhatsApp hands the number over as a bare `94763475058`. A Sri Lankan one gets the convention the
 * rest of the dashboard already spaces numbers with; anything else is left exactly as the channel
 * gave it, because forcing the local country code onto a foreign number would invent an address the
 * customer does not have.
 */
function customerHandle(handle: string): string {
  const digits = handle.replace(/\D/g, '')
  const isSriLankan =
    (digits.length === 11 && digits.startsWith('94')) ||
    (digits.length === 10 && digits.startsWith('0'))
  return isSriLankan ? formatLkPhone(handle) : handle
}

/**
 * An inbound customer message, relayed from WhatsApp into the thread.
 *
 * These are the customer's own words rather than an agent's summary, so the block is drawn as the
 * message it arrived as: the WhatsApp mark and the handle name the channel, and the text sits in an
 * inbound chat bubble on the channel's canvas. It keeps its own surface whatever bubble the thread
 * placed it in, which is why it ignores `tone` — a tint of the staff bubble's ink would read as
 * Aveline talking, and the whole point of the block is that she is not.
 */
function ClientMessageBlock({ block }: BlockRendererProps) {
  const handle = typeof block.from === 'string' ? block.from.trim() : ''

  return (
    // The card paints its own surface, so it states its own ink rather than inheriting it: the
    // staff bubble's `text-primary-foreground` is white, which on this white card is invisible.
    <figure className="overflow-hidden rounded-xl border border-whatsapp/25 bg-card text-card-foreground">
      <figcaption className="flex items-center gap-2.5 border-b border-whatsapp/20 bg-whatsapp/10 px-3 py-2">
        <span className="flex size-7 shrink-0 items-center justify-center rounded-full bg-whatsapp text-whatsapp-foreground">
          <WhatsAppIcon className="size-4" />
        </span>
        <span className="min-w-0 flex-1">
          <span className="block text-[10px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
            Customer
          </span>
          {handle && (
            <span className="block truncate text-[13px] font-medium" title={handle}>
              {customerHandle(handle)}
            </span>
          )}
        </span>
        {/* The channel is named in ink, not in the brand green: white on the mark fails contrast at
            label size, and the mark beside it is already the brand. */}
        <span
          data-slot="client-channel"
          className="shrink-0 rounded-full bg-whatsapp/15 px-2 py-0.5 text-[10px] font-semibold text-whatsapp-deep"
        >
          WhatsApp
        </span>
      </figcaption>
      {block.text && (
        <div className="bg-whatsapp-canvas px-3 py-2.5">
          {/* `w-fit` so the bubble hugs what the customer actually wrote — a one-line question in a
              full-width plate reads as an empty form, which is the opposite of a chat. */}
          <div className="relative w-fit max-w-[95%] rounded-2xl rounded-tl-sm bg-card px-3 py-2 shadow-xs">
            {/* The inbound tail: a right-pointing triangle in the bubble's own fill, so it matches
                the bubble in either theme without carrying a border of its own. */}
            <span
              aria-hidden
              className="absolute -left-[7px] top-0 size-0 border-y-[7px] border-r-[7px] border-y-transparent border-r-card"
            />
            {/* Deliberately plain text, not `MentionText`: these are the customer's own words. A
                `@` a customer types is a handle or an at-sign, not an entity the resolver read — a
                mention is a staff affordance, and only staff messages are resolved for one. */}
            <p className="whitespace-pre-wrap text-sm leading-relaxed">{block.text}</p>
          </div>
        </div>
      )}
    </figure>
  )
}

function PaymentBlock({ block }: BlockRendererProps) {
  return (
    <div className="rounded-lg border border-commerce/20 bg-commerce/5 p-3">
      <p className="text-xs font-medium text-commerce">Payment</p>
      {typeof block.amount === 'number' && (
        <p className="mt-1 text-sm font-semibold">LKR {block.amount.toLocaleString()}</p>
      )}
      {block.status && <Badge className="mt-2">{block.status}</Badge>}
    </div>
  )
}

function CourierBlock({ block }: BlockRendererProps) {
  return (
    <div className="rounded-lg border border-border bg-muted/40 p-3">
      <p className="text-xs font-medium text-muted-foreground">Delivery</p>
      {block.status && <p className="mt-1 text-sm">{block.status}</p>}
    </div>
  )
}

/**
 * The tinted surface an editorial note wears: Elle's gold, Ava's rose, Lina's wine, and the app's
 * accent when the note is not attributed to an agent.
 */
function personaSurface(persona: Persona | null | undefined) {
  const border =
    persona?.key === 'ava'
      ? 'border-memory/20'
      : persona?.key === 'elle'
        ? 'border-visual/20'
        : persona?.key === 'lina'
          ? 'border-commerce/20'
          : 'border-primary/20'

  const bg =
    persona?.key === 'ava'
      ? 'bg-memory/5'
      : persona?.key === 'elle'
        ? 'bg-visual/5'
        : persona?.key === 'lina'
          ? 'bg-commerce/5'
          : 'bg-primary/5'

  return { border, bg, text: persona?.text ?? 'text-primary' }
}

function SuggestionBlock({ block, persona, tone, messageId, bridge }: BlockRendererProps) {
  const surface = personaSurface(persona)

  return (
    <ActionableBlock
      block={block}
      messageId={messageId}
      bridge={bridge}
      title={blockTitle(block)}
      tone={tone}
      className={cn('rounded-lg border', surface.border, surface.bg)}
      contentClassName="p-3"
    >
      <p className={cn('text-xs font-medium', surface.text)}>Suggestion</p>
      {block.text && <MentionText text={block.text} tone={tone} className="mt-1 text-sm" />}
    </ActionableBlock>
  )
}

/**
 * Elle's curated look.
 *
 * A look is a pairing and a rationale, and the boutique has no photograph of it — only of each
 * piece. With a picture of its own it is a tile like a piece; without one it is the styling note
 * about the row, labelled with the look's name and read at the row's full width. It is deliberately
 * never given a blank plate to fill.
 */
function LookBlock({ block, persona, tone, messageId, bridge }: BlockRendererProps) {
  const surface = personaSurface(persona)

  if (!block.imageUrl) {
    return (
      <ActionableBlock
        block={block}
        messageId={messageId}
        bridge={bridge}
        title={blockTitle(block)}
        tone={tone}
        className={cn('rounded-lg border', surface.border, surface.bg)}
        contentClassName="p-3"
      >
        <p className={cn('text-xs font-medium', surface.text)}>{block.name ?? 'Look'}</p>
        {block.text && (
          <MentionText text={block.text} tone={tone} className="mt-1 text-sm leading-relaxed" />
        )}
      </ActionableBlock>
    )
  }

  return (
    <ActionableBlock
      block={block}
      messageId={messageId}
      bridge={bridge}
      title={blockTitle(block)}
      tone={tone}
      className="rounded-xl border border-border/70 bg-card"
    >
      <img
        loading="lazy"
        decoding="async"
        src={block.imageUrl}
        alt={block.name ?? 'Look'}
        className="aspect-4/3 w-full bg-muted object-cover"
      />
      {block.text && (
        // The padding sits on a wrapper, not on the clamped paragraph: `overflow: hidden` clips at
        // the padding box, so padding below a line clamp lets the next line show through it.
        <div className="p-2.5">
          <p
            className="line-clamp-4 text-[11px] leading-relaxed text-muted-foreground"
            title={block.text}
          >
            {block.text}
          </p>
        </div>
      )}
    </ActionableBlock>
  )
}

/**
 * A run of consecutive tiles, or one block that has to keep the full bubble width.
 */
type BlockGroup =
  | { kind: 'tiles'; blocks: ContentBlock[] }
  | { kind: 'single'; block: ContentBlock }

/** Groups consecutive tiles together; every other block stands on its own. */
function groupBlocks(blocks: ContentBlock[]): BlockGroup[] {
  const groups: BlockGroup[] = []
  for (const block of blocks) {
    if (isTileBlock(block)) {
      const last = groups[groups.length - 1]
      if (last?.kind === 'tiles') last.blocks.push(block)
      else groups.push({ kind: 'tiles', blocks: [block] })
      continue
    }
    groups.push({ kind: 'single', block })
  }
  return groups
}

/**
 * The row a run of tiles sits in.
 *
 * `auto-fit` sizes the row to the bubble: four across a wide thread, two in a narrower one, one in
 * the floating drawer when there is truly no room for a second. It only works because the message
 * bubble spans its full width whenever the message carries a row of tiles (`MessageBubble`), which
 * gives the grid a definite width to count columns against — under the shrink-to-fit bubble a
 * text-only message gets, a grid with `auto-fit` resolves to a single track and the tiles stack.
 *
 * A run of one is the exception: it stays shrink-to-fit beside its own 16rem cap, so a single piece
 * is a card and not a band, with no dead space beside it inside the bubble.
 */
const TILE_GRID_CLASS = 'grid-cols-[repeat(auto-fit,minmax(min(100%,11rem),1fr))]'

/** A run of product tiles, laid out as the row of a curated set. */
function TileGrid({
  blocks,
  render,
}: {
  blocks: ContentBlock[]
  render: (block: ContentBlock, key: number) => ReactNode
}) {
  return (
    <div
      data-slot="tile-grid"
      className={cn('grid gap-2', TILE_GRID_CLASS, blocks.length === 1 && 'max-w-64')}
    >
      {blocks.map((block, i) => render(block, i))}
    </div>
  )
}

/** Renders a message's ordered content blocks, separated by a hairline. */
export function BlockList({
  blocks,
  onSignOff,
  onSelectCustomer,
  onOpenAttachment,
  persona,
  tone = 'other',
  messageId,
  bridge,
}: {
  blocks: unknown[]
  onSignOff?: (approved: boolean) => void
  onSelectCustomer?: (customerId: string) => void
  onOpenAttachment?: (attachmentId: string) => void
  persona?: Persona | null
  tone?: AttachmentTone
  /** The message the blocks belong to, so each action rail can name it. */
  messageId?: string
  /** The thread's action handlers; absent draws every block without a rail. */
  bridge?: BlockActionBridge
}) {
  const parsed = withoutBorrowedLookImages(blocks ?? []) as ContentBlock[]
  if (parsed.length === 0) return null

  const renderBlock = (block: ContentBlock, key: number) => (
    <BlockRenderer
      key={key}
      block={block}
      onSignOff={onSignOff}
      onSelectCustomer={onSelectCustomer}
      onOpenAttachment={onOpenAttachment}
      persona={persona}
      tone={tone}
      messageId={messageId}
      bridge={bridge}
    />
  )

  const groups = groupBlocks(parsed)

  return (
    <div className="space-y-2">
      {groups.map((group, i) => (
        <div key={i}>
          {i > 0 && <Separator className="my-2" />}
          {group.kind === 'tiles' ? (
            <TileGrid blocks={group.blocks} render={renderBlock} />
          ) : (
            renderBlock(group.block, 0)
          )}
        </div>
      ))}
    </div>
  )
}


/** Bytes fetched for one attachment, with the type the server actually served them as. */
interface AttachmentBytes {
  bytes: ArrayBuffer
  /** The response's own `Content-Type`, never a client-declared one (risk R7). */
  contentType: string
}

/**
 * A process-lifetime cache keyed by attachment id, mirroring mobile's repository-level
 * `_attachmentCache` (`api_thread_repository.dart:87-91`): an attachment row is immutable once
 * written, so scrolling the Salon must not refetch its bytes. A rejected request is evicted so a
 * later mount can retry rather than caching the failure forever.
 */
const attachmentByteCache = new Map<string, Promise<AttachmentBytes>>()

/**
 * Fetches an attachment's bytes through the shared authenticated axios client, which attaches the
 * Clerk bearer token (`api.ts:59-68`). The stored `url` is the authenticated Aveline serve route
 * (`ConversationEndpoints.cs:574-608`), so it is used as the *request* path — never as an `<img>`
 * source, which cannot carry an `Authorization` header.
 *
 * This is deliberately not the `attachment.view` token route: that route is anonymous because the
 * token is the credential, and its 900 s TTL would make a cached image go stale mid-session
 * (strategy §3.8).
 */
function fetchAttachmentBytes(attachmentId: string, url: string): Promise<AttachmentBytes> {
  const cached = attachmentByteCache.get(attachmentId)
  if (cached) return cached

  const request = apiClient
    .get<ArrayBuffer>(url, { responseType: 'arraybuffer' })
    .then((response) => {
      const header = (response.headers as Record<string, unknown> | undefined)?.[
        'content-type'
      ]
      return {
        bytes: response.data,
        contentType: typeof header === 'string' ? header : '',
      }
    })
    .catch((error: unknown) => {
      attachmentByteCache.delete(attachmentId)
      throw error
    })

  attachmentByteCache.set(attachmentId, request)
  return request
}

/** The one-word label a chip leads with. */
function attachmentKindLabel(contentType: string): string {
  if (contentType === 'application/pdf') return 'PDF'
  if (contentType.startsWith('image/')) return 'Image'
  return 'File'
}

/**
 * The quiet caption under a photo. An uploaded photograph is frequently named by its digest
 * (`8b420c6fbc31eea2d64a5e2…`), and a wall of hex in a 256 px bubble is noise, not identity: an
 * opaque name is captioned "Photo" while the real name stays on the tooltip and in the viewer,
 * where there is room for it. A human name (`dress.png`) is shown as it is.
 */
function attachmentDisplayName(fileName: string): string {
  const base = fileName.replace(/\.[a-z0-9]+$/i, '')
  const looksOpaque = /^[0-9a-f]{16,}$/i.test(base) || /^\d{10,}$/.test(base)
  return looksOpaque ? 'Photo' : fileName
}

/**
 * The surface an attachment sits on, keyed to the bubble that holds it.
 *
 * The staff bubble is filled with `primary`, so a `bg-background` card on it reads as a hole punched
 * in the message. On that bubble the plate is a translucent tint of the bubble's own ink; on the
 * neutral agent/guest bubble it is the app's standard muted card. The hover fill matches each so the
 * button never flashes the ghost variant's accent colour.
 */
const ATTACHMENT_SURFACE: Record<AttachmentTone, string> = {
  own: 'border-primary-foreground/25 bg-primary-foreground/10 text-primary-foreground hover:bg-primary-foreground/15 hover:text-primary-foreground',
  other: 'border-border bg-muted/40 text-foreground hover:bg-muted/60 hover:text-foreground',
}

/** The secondary ink for a size, a kind label, or a placeholder on each surface. */
const ATTACHMENT_MUTED: Record<AttachmentTone, string> = {
  own: 'text-primary-foreground/70',
  other: 'text-muted-foreground',
}

/** A thread attachment in a state that has no picture to show: its name and size. */
function AttachmentChip({
  label,
  fileName,
  sizeLabel,
  state,
  tone = 'other',
}: {
  label: string
  fileName: string
  sizeLabel: string | null
  state: 'loading' | 'unavailable' | 'static'
  tone?: AttachmentTone
}) {
  return (
    <div
      data-state={state}
      aria-busy={state === 'loading' || undefined}
      className={cn(
        'flex w-fit max-w-full items-center gap-2 rounded-lg border px-3 py-2',
        ATTACHMENT_SURFACE[tone],
      )}
    >
      <span
        className={cn(
          'shrink-0 text-xs font-medium uppercase tracking-wide',
          ATTACHMENT_MUTED[tone],
        )}
      >
        {label}
      </span>
      <span className="min-w-0 truncate text-sm">{fileName}</span>
      {sizeLabel && (
        <span className={cn('shrink-0 text-xs', ATTACHMENT_MUTED[tone])}>{sizeLabel}</span>
      )}
    </div>
  )
}

/** The shadcn dialog that shows a fetched image at full size, or a best-effort PDF embed. */
function AttachmentViewer({
  open,
  onOpenChange,
  fileName,
  label,
  sizeLabel,
  contentType,
  objectUrl,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  fileName: string
  label: string
  sizeLabel: string | null
  contentType: string
  objectUrl: string
}) {
  const isPdf = contentType === 'application/pdf'
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl">
        <DialogHeader>
          <DialogTitle className="truncate pr-6 text-sm">{fileName}</DialogTitle>
          <DialogDescription className="flex items-center gap-2">
            <span>{label}</span>
            {sizeLabel && <span>{sizeLabel}</span>}
          </DialogDescription>
        </DialogHeader>
        {isPdf ? (
          <embed
            src={objectUrl}
            type="application/pdf"
            title={fileName}
            className="h-[70vh] w-full rounded-md border"
          />
        ) : (
          <div className="max-h-[70vh] overflow-auto">
            <img loading="lazy" decoding="async" src={objectUrl} alt={fileName} className="mx-auto max-w-full" />
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

/**
 * A message attachment. The bytes are fetched through the authenticated client and shown from an
 * object URL; a failed fetch degrades to the name-and-size chip rather than to a broken image,
 * exactly as mobile falls back (`thread_blocks.dart:436-472`). An `<img src={block.url}>` cannot
 * work, because an image element cannot carry the bearer token (strategy §3.8).
 */
function AttachmentBlock({
  block,
  onOpenAttachment,
  tone = 'other',
}: BlockRendererProps) {
  const attachmentId = block.attachmentId
  const route = block.url
  const contentType = block.contentType ?? ''
  const fileName = block.fileName ?? 'Attachment'
  const displayName = attachmentDisplayName(fileName)
  const sizeLabel = typeof block.sizeBytes === 'number' ? formatBytes(block.sizeBytes) : null
  const label = attachmentKindLabel(contentType)
  const isImage = contentType.startsWith('image/')
  const isPdf = contentType === 'application/pdf'
  // Bytes are only worth fetching for something the block can actually show or hand over.
  const canFetch = Boolean(attachmentId && route) && (isImage || isPdf)

  const [objectUrl, setObjectUrl] = useState<string | null>(null)
  const [failed, setFailed] = useState(false)
  const [viewerOpen, setViewerOpen] = useState(false)

  useEffect(() => {
    if (!canFetch || !attachmentId || !route) return

    let cancelled = false
    let created: string | null = null

    fetchAttachmentBytes(attachmentId, route)
      .then(({ bytes, contentType: servedType }) => {
        if (cancelled) return
        // The blob type is the server's served type (falling back to the stored one), so a
        // mislabelled payload is not rendered from a client-declared type.
        created = URL.createObjectURL(
          new Blob([bytes], { type: servedType || contentType }),
        )
        setObjectUrl(created)
      })
      .catch(() => {
        if (!cancelled) setFailed(true)
      })

    return () => {
      cancelled = true
      // The object URL is per mounted block, so revoking it cannot strand another block showing
      // the same (cached) attachment.
      if (created) URL.revokeObjectURL(created)
    }
  }, [attachmentId, route, canFetch, contentType])

  const handleOpen = () => {
    if (attachmentId) onOpenAttachment?.(attachmentId)
    setViewerOpen(true)
  }

  if (failed) {
    return (
      <AttachmentChip
        label={label}
        fileName={fileName}
        sizeLabel={sizeLabel}
        state="unavailable"
        tone={tone}
      />
    )
  }

  // A photo waits in the shape it will arrive in, so the bubble does not jump from a text row to a
  // plate when the bytes land.
  if (canFetch && isImage && objectUrl === null) {
    return (
      <div
        data-state="loading"
        aria-busy="true"
        className={cn(
          'w-64 max-w-full overflow-hidden rounded-xl border',
          ATTACHMENT_SURFACE[tone],
        )}
      >
        <div className="flex aspect-4/3 w-full items-center justify-center border-b border-border/40">
          <Spinner className="size-4" aria-label={`Loading ${fileName}`} />
        </div>
        <div className="flex w-full min-w-0 items-center gap-2 px-2.5 py-2">
          <span
            className={cn(
              'shrink-0 text-[10px] font-semibold uppercase tracking-[0.14em]',
              ATTACHMENT_MUTED[tone],
            )}
          >
            {label}
          </span>
          <span className="min-w-0 flex-1 truncate text-xs font-medium">{displayName}</span>
          {sizeLabel && (
            <span className={cn('shrink-0 text-[10px] tabular-nums', ATTACHMENT_MUTED[tone])}>
              {sizeLabel}
            </span>
          )}
        </div>
      </div>
    )
  }

  if (canFetch && objectUrl === null) {
    return (
      <AttachmentChip
        label={label}
        fileName={fileName}
        sizeLabel={sizeLabel}
        state="loading"
        tone={tone}
      />
    )
  }

  if (objectUrl === null) {
    return (
      <AttachmentChip
        label={label}
        fileName={fileName}
        sizeLabel={sizeLabel}
        state="static"
        tone={tone}
      />
    )
  }

  if (isPdf) {
    return (
      <>
        <div className="flex flex-wrap items-center gap-2 rounded-lg border border-border bg-muted/40 px-3 py-2">
          <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {label}
          </span>
          <span className="truncate text-sm">{fileName}</span>
          {sizeLabel && <span className="text-xs text-muted-foreground">{sizeLabel}</span>}
          <span className="ml-auto flex items-center gap-1.5">
            <Button type="button" size="xs" variant="outline" onClick={handleOpen}>
              Open
            </Button>
            {/* A download link is the guaranteed path; the embed is best-effort. */}
            <Button asChild size="xs" variant="ghost">
              <a href={objectUrl} download={fileName}>
                Download
              </a>
            </Button>
          </span>
        </div>
        <AttachmentViewer
          open={viewerOpen}
          onOpenChange={setViewerOpen}
          fileName={fileName}
          label={label}
          sizeLabel={sizeLabel}
          contentType={contentType}
          objectUrl={objectUrl}
        />
      </>
    )
  }

  return (
    <>
      {/*
        The photo is the block. It is framed as a labelled plate — the image on top, a quiet caption
        rail beneath — rather than a thumbnail with the file beside it, because a side-by-side row
        let a long generated name push past the bubble's edge. The rail truncates instead, and the
        expand glyph only appears on hover or keyboard focus, so the photograph is never competing
        with chrome.
      */}
      <Button
        type="button"
        variant="ghost"
        onClick={handleOpen}
        aria-label={`Open ${fileName}`}
        title={fileName}
        className={cn(
          'group flex h-auto w-64 max-w-full flex-col items-stretch gap-0 overflow-hidden rounded-xl border p-0 text-left shadow-none',
          ATTACHMENT_SURFACE[tone],
        )}
      >
        <span className="relative block aspect-4/3 w-full overflow-hidden bg-muted/30">
          <img
            src={objectUrl}
            alt={fileName}
            loading="lazy"
            decoding="async"
            className="h-full w-full object-cover transition-transform duration-500 group-hover:scale-[1.03] motion-reduce:transition-none motion-reduce:group-hover:scale-100"
            // Mirrors mobile's `errorBuilder`: bytes that arrive but will not decode fall back to
            // the chip rather than to a broken-image glyph.
            onError={() => setFailed(true)}
          />
          <span
            aria-hidden
            className="pointer-events-none absolute right-2 top-2 flex size-6 items-center justify-center rounded-full bg-background/80 text-foreground opacity-0 shadow-2xs backdrop-blur-xs transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100"
          >
            <Maximize2 className="size-3" />
          </span>
        </span>
        <span className="flex w-full min-w-0 items-center gap-2 px-2.5 py-2">
          <span
            className={cn(
              'shrink-0 text-[10px] font-semibold uppercase tracking-[0.14em]',
              ATTACHMENT_MUTED[tone],
            )}
          >
            {label}
          </span>
          <span className="min-w-0 flex-1 truncate text-xs font-medium">{displayName}</span>
          {sizeLabel && (
            <span className={cn('shrink-0 text-[10px] tabular-nums', ATTACHMENT_MUTED[tone])}>
              {sizeLabel}
            </span>
          )}
        </span>
      </Button>
      <AttachmentViewer
        open={viewerOpen}
        onOpenChange={setViewerOpen}
        fileName={fileName}
        label={label}
        sizeLabel={sizeLabel}
        contentType={contentType}
        objectUrl={objectUrl}
      />
    </>
  )
}

/** A file size the way a person reads it. */
function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}
