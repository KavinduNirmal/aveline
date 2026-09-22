import { useEffect, useState } from 'react'

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
import { apiClient } from '@/lib/api'
import { cn } from '@/lib/utils'

/** An option in a customer-resolution `choice` block. */
export interface ChoiceOption {
  customerId: string
  fullName?: string | null
  status?: string | null
  lastVisitAt?: string | null
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

interface BlockRendererProps {
  block: ContentBlock
  onSignOff?: (approved: boolean) => void
  onSelectCustomer?: (customerId: string) => void
  /** Called with the attachment id when a thread attachment is opened for viewing. */
  onOpenAttachment?: (attachmentId: string) => void
  persona?: Persona | null
}

/** Renders a single content block by type. */
export function BlockRenderer({
  block,
  onSignOff,
  onSelectCustomer,
  onOpenAttachment,
  persona,
}: BlockRendererProps) {
  switch (block.type) {
    case 'text':
      return <p className="whitespace-pre-wrap text-sm leading-relaxed">{block.text}</p>
    case 'piece':
      return <PieceBlock block={block} onSignOff={onSignOff} persona={persona} />
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
      return <SuggestionBlock block={block} onSignOff={onSignOff} persona={persona} />
    case 'look':
      return <LookBlock block={block} onSignOff={onSignOff} persona={persona} />
    case 'choice':
      return <ChoiceBlock block={block} onSelectCustomer={onSelectCustomer} />
    case 'attachment':
      return (
        <AttachmentBlock
          key={block.attachmentId}
          block={block}
          onOpenAttachment={onOpenAttachment}
        />
      )
    default:
      return null
  }
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

function PieceBlock({ block }: BlockRendererProps) {
  return (
    <Card className="overflow-hidden">
      {block.imageUrl && (
        <img src={block.imageUrl} alt={block.name ?? 'Piece'} className="h-40 w-full object-cover" />
      )}
      <CardContent className="p-3">
        <div className="flex items-start justify-between gap-2">
          <div className="min-w-0">
            <p className="truncate font-serif text-sm font-medium">{block.name ?? 'Piece'}</p>
            {block.size && (
              <p className="text-xs text-muted-foreground">Size {block.size}</p>
            )}
          </div>
          {typeof block.price === 'number' && (
            <p className="shrink-0 text-sm font-semibold text-primary">
              LKR {block.price.toLocaleString()}
            </p>
          )}
        </div>
        {typeof block.stock === 'number' && (
          <Badge variant="secondary" className="mt-2">
            {block.stock} in stock
          </Badge>
        )}
      </CardContent>
    </Card>
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

function ClientMessageBlock({ block }: BlockRendererProps) {
  return (
    <div className="rounded-lg border border-border bg-muted/40 p-3">
      <p className="mb-1 text-[11px] font-medium uppercase tracking-wide text-muted-foreground">
        Customer · {block.from ?? 'WhatsApp'}
      </p>
      <p className="whitespace-pre-wrap text-sm">{block.text}</p>
    </div>
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

function SuggestionBlock({ block, persona }: BlockRendererProps) {
  const borderClass =
    persona?.key === 'ava'
      ? 'border-memory/20'
      : persona?.key === 'elle'
        ? 'border-visual/20'
        : persona?.key === 'lina'
          ? 'border-commerce/20'
          : 'border-primary/20'

  const bgClass =
    persona?.key === 'ava'
      ? 'bg-memory/5'
      : persona?.key === 'elle'
        ? 'bg-visual/5'
        : persona?.key === 'lina'
          ? 'bg-commerce/5'
          : 'bg-primary/5'

  const textClass = persona?.text ?? 'text-primary'

  return (
    <div className={cn('rounded-lg border p-3', borderClass, bgClass)}>
      <p className={cn('text-xs font-medium', textClass)}>Suggestion</p>
      {block.text && <p className="mt-1 text-sm">{block.text}</p>}
    </div>
  )
}

function LookBlock({ block, persona }: BlockRendererProps) {
  const bgSoftClass = persona?.bgSoft ?? 'bg-visual/10'
  const textClass = persona?.text ?? 'text-visual'

  return (
    <div className="overflow-hidden rounded-lg border">
      {block.imageUrl ? (
        <img src={block.imageUrl} alt={block.name ?? 'Look'} className="h-44 w-full object-cover" />
      ) : (
        <div className={cn('flex h-24 items-center justify-center', bgSoftClass, textClass)}>
          <span className="text-xs font-medium">Look</span>
        </div>
      )}
      {block.text && (
        <p className="border-t p-2 text-xs text-muted-foreground">{block.text}</p>
      )}
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
}: {
  blocks: unknown[]
  onSignOff?: (approved: boolean) => void
  onSelectCustomer?: (customerId: string) => void
  onOpenAttachment?: (attachmentId: string) => void
  persona?: Persona | null
}) {
  const parsed = (blocks ?? []) as ContentBlock[]
  if (parsed.length === 0) return null
  return (
    <div className="space-y-2">
      {parsed.map((block, i) => (
        <div key={i}>
          {i > 0 && <Separator className="my-2" />}
          <BlockRenderer
            block={block}
            onSignOff={onSignOff}
            onSelectCustomer={onSelectCustomer}
            onOpenAttachment={onOpenAttachment}
            persona={persona}
          />
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

/** A thread attachment: the file's name and size. */
function AttachmentChip({
  label,
  fileName,
  sizeLabel,
  state,
}: {
  label: string
  fileName: string
  sizeLabel: string | null
  state: 'loading' | 'unavailable' | 'static'
}) {
  return (
    <div
      data-state={state}
      aria-busy={state === 'loading' || undefined}
      className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 px-3 py-2"
    >
      <span className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </span>
      <span className="truncate text-sm">{fileName}</span>
      {sizeLabel && <span className="text-xs text-muted-foreground">{sizeLabel}</span>}
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
            <img src={objectUrl} alt={fileName} className="mx-auto max-w-full" />
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
function AttachmentBlock({ block, onOpenAttachment }: BlockRendererProps) {
  const attachmentId = block.attachmentId
  const route = block.url
  const contentType = block.contentType ?? ''
  const fileName = block.fileName ?? 'Attachment'
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
      />
    )
  }

  if (canFetch && objectUrl === null) {
    return (
      <AttachmentChip label={label} fileName={fileName} sizeLabel={sizeLabel} state="loading" />
    )
  }

  if (objectUrl === null) {
    return (
      <AttachmentChip label={label} fileName={fileName} sizeLabel={sizeLabel} state="static" />
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
      <Button
        type="button"
        variant="outline"
        size="sm"
        className="h-auto max-w-full gap-2 p-1 pr-3"
        onClick={handleOpen}
      >
        <img
          src={objectUrl}
          alt={fileName}
          className="size-16 rounded-md object-cover"
          // Mirrors mobile's `errorBuilder`: bytes that arrive but will not decode fall back to
          // the chip rather than to a broken-image glyph.
          onError={() => setFailed(true)}
        />
        <span className="flex min-w-0 flex-col items-start">
          <span className="max-w-56 truncate text-sm">{fileName}</span>
          {sizeLabel && <span className="text-xs text-muted-foreground">{sizeLabel}</span>}
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
