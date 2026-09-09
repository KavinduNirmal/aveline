import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'
import { cn } from '@/lib/utils'

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
  [key: string]: unknown
}

interface BlockRendererProps {
  block: ContentBlock
  onSignOff?: (approved: boolean) => void
}

/** Renders a single content block by type. */
export function BlockRenderer({ block, onSignOff }: BlockRendererProps) {
  switch (block.type) {
    case 'text':
      return <p className="whitespace-pre-wrap text-sm leading-relaxed">{block.text}</p>
    case 'piece':
      return <PieceBlock block={block} />
    case 'at_a_glance':
      return <AtAGlanceBlock block={block} />
    case 'sign_off':
      return <SignOffBlock block={block} onSignOff={onSignOff} />
    case 'client_message':
      return <ClientMessageBlock block={block} />
    case 'payment':
      return <PaymentBlock block={block} />
    case 'courier':
      return <CourierBlock block={block} />
    case 'suggestion':
      return <SuggestionBlock block={block} />
    case 'look':
      return <LookBlock block={block} />
    default:
      return null
  }
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

function SuggestionBlock({ block }: BlockRendererProps) {
  return (
    <div className="rounded-lg border border-visual/20 bg-visual/5 p-3">
      <p className="text-xs font-medium text-visual">Suggestion</p>
      {block.text && <p className="mt-1 text-sm">{block.text}</p>}
    </div>
  )
}

function LookBlock({ block }: BlockRendererProps) {
  return (
    <div className="overflow-hidden rounded-lg border">
      {block.imageUrl ? (
        <img src={block.imageUrl} alt={block.name ?? 'Look'} className="h-44 w-full object-cover" />
      ) : (
        <div className={cn('flex h-24 items-center justify-center bg-visual/10 text-visual')}>
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
}: {
  blocks: unknown[]
  onSignOff?: (approved: boolean) => void
}) {
  const parsed = (blocks ?? []) as ContentBlock[]
  if (parsed.length === 0) return null
  return (
    <div className="space-y-2">
      {parsed.map((block, i) => (
        <div key={i}>
          {i > 0 && <Separator className="my-2" />}
          <BlockRenderer block={block} onSignOff={onSignOff} />
        </div>
      ))}
    </div>
  )
}
