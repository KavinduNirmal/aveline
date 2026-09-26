import { useState } from 'react'
import {
  Plus,
  Building2,
  X,
  Check,
  Archive,
  ChevronDown,
  ChevronRight,
  ChevronsDownUp,
  ChevronsUpDown,
  RotateCcw,
} from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { formatMoney } from '@/lib/format-money'
import { cn } from '@/lib/utils'
import type { SourcingRequestMock, SupplierMock } from './mockData'

interface SourcingTabProps {
  sourcingRequests: SourcingRequestMock[]
  suppliers: SupplierMock[]
  onUpdateStatus: (id: string, newStatus: SourcingRequestMock['status']) => void
  onAddRequest: (request: SourcingRequestMock) => void
}

const STAGES: {
  id: SourcingRequestMock['status']
  label: string
  color: string
}[] = [
  { id: 'pending', label: 'Pending Quote', color: 'bg-warning/10 text-warning border-warning/20' },
  { id: 'quoted', label: 'Quoted by Atelier', color: 'bg-chart-1/10 text-chart-1 border-chart-1/20' },
  { id: 'approved', label: 'Approved', color: 'bg-chart-2/10 text-chart-2 border-chart-2/20' },
  { id: 'ordered', label: 'Ordered from Atelier', color: 'bg-chart-3/10 text-chart-3 border-chart-3/20' },
  { id: 'fulfilled', label: 'Fulfilled', color: 'bg-success/10 text-success border-success/20' },
]

export function SourcingTab({
  sourcingRequests,
  suppliers,
  onUpdateStatus,
  onAddRequest,
}: SourcingTabProps) {
  const [modalOpen, setModalOpen] = useState(false)
  const [clientName, setClientName] = useState('')
  const [category, setCategory] = useState('Lehengas')
  const [color, setColor] = useState('')
  const [description, setDescription] = useState('')
  const [targetPrice, setTargetPrice] = useState('2000')
  const [estimatedCost, setEstimatedCost] = useState('950')
  const [supplierId, setSupplierId] = useState(suppliers[0]?.id ?? '')
  const [referenceImageUrl] = useState(
    'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
  )

  /**
   * A board of heavy cards grows past the screen fast, so each one folds.
   *
   * Absent means open: a ticket is whole until someone folds it, and a column folds all of its own
   * with one control, which is the difference between a readable board and a scrolling minigame.
   */
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({})
  const [showArchived, setShowArchived] = useState(false)

  const archiveTicket = (ticket: SourcingRequestMock) => {
    onUpdateStatus(ticket.id, 'archived')
    // Undo is the confirmation: it restores the stage the ticket actually came from, which the
    // board would otherwise have no way to know.
    toast.success(`Archived the ${ticket.category} ticket for ${ticket.clientName}`, {
      description: 'It is off the pipeline, waiting under Archived.',
      action: { label: 'Undo', onClick: () => onUpdateStatus(ticket.id, ticket.status) },
    })
  }

  const restoreTicket = (ticket: SourcingRequestMock) => {
    // The old stage is not recorded, so a restore re-enters at the top of the pipeline rather than
    // guessing one. The Undo on archive is the path that keeps the exact stage.
    onUpdateStatus(ticket.id, 'pending')
    toast.success(`Restored the ${ticket.category} ticket for ${ticket.clientName}`, {
      description: 'Back on the board at Pending Quote.',
    })
  }

  const setCollapsedFor = (ids: string[], value: boolean) => {
    setCollapsed((prev) => {
      const next = { ...prev }
      for (const id of ids) next[id] = value
      return next
    })
  }

  // An archived ticket is off the pipeline: it keeps its record without holding a column open.
  const activeTickets = sourcingRequests.filter((ticket) => ticket.status !== 'archived')
  const archivedTickets = sourcingRequests.filter((ticket) => ticket.status === 'archived')

  const handleCreateTicket = (e: React.FormEvent) => {
    e.preventDefault()
    if (!clientName.trim()) {
      toast.error('Client name is required')
      return
    }

    const selectedSupplier = suppliers.find((s) => s.id === supplierId) ?? suppliers[0]
    const cost = parseFloat(estimatedCost) || 0
    const target = parseFloat(targetPrice) || 0
    const markup = cost > 0 ? (target - cost) / cost : 1.0

    const newTicket: SourcingRequestMock = {
      id: `src-${Date.now()}`,
      clientName: clientName.trim(),
      category,
      color: color.trim() || 'Custom Hue',
      itemDescription: description.trim() || 'Custom bespoke design request.',
      referenceImageUrl,
      supplierId: selectedSupplier?.id ?? 'sup-1',
      supplierName: selectedSupplier?.name ?? 'Partner Atelier',
      estimatedCost: cost,
      proposedMarkup: Number(markup.toFixed(2)),
      targetPrice: target,
      status: 'pending',
      createdAt: new Date().toISOString(),
    }

    onAddRequest(newTicket)
    toast.success('Sourcing request ticket created', {
      description: `Assigned to ${selectedSupplier?.name}.`,
    })
    setModalOpen(false)
    setClientName('')
    setColor('')
    setDescription('')
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Sourcing Header Action */}
      <div className="flex items-center justify-between">
        <div>
          <h3 className="font-serif text-base font-semibold text-foreground">
            Atelier Sourcing Pipeline
          </h3>
          <p className="text-xs text-muted-foreground">
            Track bespoke client commissions, atelier quotations, and fulfillment margins
          </p>
        </div>

        <div className="flex items-center gap-2">
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => setShowArchived((shown) => !shown)}
            aria-pressed={showArchived}
            aria-controls="archived-tickets"
            className="gap-1.5 rounded-xl text-xs h-9 px-3 aria-pressed:bg-accent aria-pressed:text-accent-foreground"
          >
            <Archive className="size-4" />
            <span>Archived ({archivedTickets.length})</span>
          </Button>

          <Button
            size="sm"
            onClick={() => setModalOpen(true)}
            className="gap-1.5 rounded-xl text-xs h-9 px-4 shadow-sm"
          >
            <Plus className="size-4" />
            <span>New Sourcing Ticket</span>
          </Button>
        </div>
      </div>

      {/* Kanban Stages Grid. Columns take their own height, so folding a column actually shortens
          the board instead of leaving an equally tall empty track behind it. */}
      <div className="grid grid-cols-1 items-start gap-4 md:grid-cols-2 lg:grid-cols-5">
        {STAGES.map((stage) => {
          const stageTickets = activeTickets.filter((t) => t.status === stage.id)
          const allFolded =
            stageTickets.length > 0 && stageTickets.every((ticket) => collapsed[ticket.id])

          return (
            <div key={stage.id} className="flex flex-col rounded-xl border border-border/80 bg-muted/20 p-3">
              {/* Stage Header */}
              <div className="flex items-center justify-between gap-1 mb-3 px-1">
                <Badge variant="outline" className={`text-[11px] font-medium ${stage.color}`}>
                  {stage.label}
                </Badge>
                <span className="flex shrink-0 items-center gap-0.5">
                  <span className="text-xs font-semibold text-muted-foreground font-mono">
                    {stageTickets.length}
                  </span>
                  {stageTickets.length > 0 && (
                    <Button
                      type="button"
                      variant="ghost"
                      size="icon-xs"
                      onClick={() =>
                        setCollapsedFor(
                          stageTickets.map((ticket) => ticket.id),
                          !allFolded,
                        )
                      }
                      aria-label={
                        allFolded
                          ? `Expand every ticket in ${stage.label}`
                          : `Fold every ticket in ${stage.label}`
                      }
                      title={allFolded ? 'Expand all' : 'Fold all'}
                    >
                      {allFolded ? (
                        <ChevronsUpDown className="size-3" />
                      ) : (
                        <ChevronsDownUp className="size-3" />
                      )}
                    </Button>
                  )}
                </span>
              </div>

              {/* Tickets Column */}
              <div className="flex flex-1 flex-col gap-3 min-h-[300px]">
                {stageTickets.length === 0 ? (
                  <div className="flex h-32 items-center justify-center rounded-lg border border-dashed border-border/60 text-center p-3 text-[11px] text-muted-foreground">
                    No tickets in this stage
                  </div>
                ) : (
                  stageTickets.map((ticket) => {
                    const marginPct =
                      ticket.estimatedCost > 0
                        ? Math.round(
                            ((ticket.targetPrice - ticket.estimatedCost) / ticket.estimatedCost) * 100,
                          )
                        : 0
                    const open = !collapsed[ticket.id]

                    return (
                      <Card
                        key={ticket.id}
                        className="flex flex-col overflow-hidden border-border/80 bg-card p-3.5 shadow-2xs transition-all hover:border-border hover:shadow-xs"
                      >
                        {/* Reference Image */}
                        {open && ticket.referenceImageUrl && (
                          <img
                            loading="lazy"
                            decoding="async"
                            src={ticket.referenceImageUrl}
                            alt={ticket.category}
                            className="h-28 w-full rounded-lg object-cover border border-border mb-2.5"
                          />
                        )}

                        {/* Identity, and the two controls that keep the board readable */}
                        <div className="flex items-start justify-between gap-1.5">
                          <span className="min-w-0 flex-1">
                            <span className="block truncate text-[10px] font-semibold text-foreground">
                              {ticket.clientName}
                            </span>
                            <span className="block truncate font-mono text-[10px] text-muted-foreground">
                              {ticket.category}
                            </span>
                          </span>
                          <span className="flex shrink-0 items-center gap-0.5">
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon-xs"
                              onClick={() => archiveTicket(ticket)}
                              aria-label={`Archive the ${ticket.category} ticket for ${ticket.clientName}`}
                              title="Archive ticket"
                            >
                              <Archive className="size-3" />
                            </Button>
                            <Button
                              type="button"
                              variant="ghost"
                              size="icon-xs"
                              onClick={() =>
                                setCollapsed((prev) => ({ ...prev, [ticket.id]: open }))
                              }
                              aria-expanded={open}
                              aria-label={`${open ? 'Fold' : 'Open'} the ${ticket.category} ticket for ${ticket.clientName}`}
                              title={open ? 'Fold' : 'Open'}
                            >
                              {open ? (
                                <ChevronDown className="size-3.5" />
                              ) : (
                                <ChevronRight className="size-3.5" />
                              )}
                            </Button>
                          </span>
                        </div>

                        <p
                          className={cn(
                            'text-xs text-muted-foreground leading-snug mt-1.5 mb-2.5',
                            open ? 'line-clamp-2' : 'line-clamp-1',
                          )}
                        >
                          {ticket.itemDescription}
                        </p>

                        {open ? (
                          <>
                            {/* Financials & Markup */}
                            <div className="flex flex-col rounded-lg bg-muted/40 p-2 text-[11px] gap-1 mb-3">
                              <div className="flex justify-between">
                                <span className="text-muted-foreground">Target Retail:</span>
                                <span className="font-semibold text-primary">
                                  {formatMoney(ticket.targetPrice)}
                                </span>
                              </div>
                              <div className="flex justify-between text-muted-foreground">
                                <span>Atelier Cost:</span>
                                <span>{formatMoney(ticket.estimatedCost)}</span>
                              </div>
                              <div className="flex justify-between font-medium text-success dark:text-success pt-1 border-t border-border/50">
                                <span>Margin:</span>
                                <span>+{marginPct}%</span>
                              </div>
                            </div>

                            {/* Partner Supplier */}
                            <div className="flex items-center gap-1.5 text-[10px] text-muted-foreground mb-3">
                              <Building2 className="size-3 text-primary shrink-0" />
                              <span className="truncate">{ticket.supplierName}</span>
                            </div>

                            {/* Stage transition buttons */}
                            <div className="pt-2 border-t border-border/60">
                              <Select
                                value={ticket.status}
                                onValueChange={(value) =>
                                  onUpdateStatus(ticket.id, value as SourcingRequestMock['status'])
                                }
                              >
                                <SelectTrigger
                                  aria-label={`Stage for ${ticket.itemDescription}`}
                                  className="h-8 w-full text-[10px]"
                                >
                                  <SelectValue />
                                </SelectTrigger>
                                <SelectContent>
                                  <SelectItem value="pending">Stage: Pending Quote</SelectItem>
                                  <SelectItem value="quoted">Stage: Quoted</SelectItem>
                                  <SelectItem value="approved">Stage: Approved</SelectItem>
                                  <SelectItem value="ordered">Stage: Ordered</SelectItem>
                                  <SelectItem value="fulfilled">Stage: Fulfilled</SelectItem>
                                </SelectContent>
                              </Select>
                            </div>
                          </>
                        ) : (
                          // Folded: the two figures that decide whether the ticket is worth opening.
                          <div className="flex items-center justify-between text-[10px]">
                            <span className="font-semibold text-primary">
                              {formatMoney(ticket.targetPrice)}
                            </span>
                            <span className="font-medium text-success">+{marginPct}% margin</span>
                          </div>
                        )}
                      </Card>
                    )
                  })
                )}
              </div>
            </div>
          )
        })}
      </div>

      {/* Archived tickets: off the pipeline, still on record */}
      {showArchived && (
        <Card
          id="archived-tickets"
          className="flex flex-col gap-3 border-border/80 bg-muted/20 p-4 shadow-none"
        >
          <div className="flex items-center justify-between">
            <h4 className="font-serif text-sm font-medium">Archived tickets</h4>
            <span className="text-[11px] text-muted-foreground">
              {archivedTickets.length === 0
                ? 'Nothing archived'
                : `${archivedTickets.length} off the pipeline`}
            </span>
          </div>

          {archivedTickets.length === 0 ? (
            <p className="text-[11px] text-muted-foreground">
              A ticket you archive leaves the board and waits here, out of the way of the work in
              progress.
            </p>
          ) : (
            <ul className="flex flex-col divide-y divide-border/60">
              {archivedTickets.map((ticket) => (
                <li key={ticket.id} className="flex items-center justify-between gap-3 py-2">
                  <span className="min-w-0">
                    <span className="block truncate text-xs font-semibold">
                      {ticket.clientName}
                    </span>
                    <span className="block truncate text-[10px] text-muted-foreground">
                      {ticket.category} · {formatMoney(ticket.targetPrice)} · {ticket.supplierName}
                    </span>
                  </span>
                  <Button
                    type="button"
                    variant="outline"
                    size="xs"
                    onClick={() => restoreTicket(ticket)}
                    className="shrink-0"
                  >
                    <RotateCcw className="size-3" />
                    <span>Restore</span>
                  </Button>
                </li>
              ))}
            </ul>
          )}
        </Card>
      )}

      {/* New Sourcing Ticket Modal */}
      {modalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-xs p-4 animate-in fade-in">
          <Card className="flex flex-col w-full max-w-lg border-border bg-background shadow-2xl p-6 gap-4 animate-in zoom-in-95">
            <div className="flex items-center justify-between border-b border-border pb-3">
              <h3 className="font-serif text-base font-semibold">New Sourcing Request Ticket</h3>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setModalOpen(false)}
                className="size-8 p-0"
              >
                <X className="size-4" />
              </Button>
            </div>

            <form onSubmit={handleCreateTicket} className="flex flex-col gap-4">
              <div className="flex flex-col gap-1">
                <Label className="text-xs">Client Name</Label>
                <Input
                  value={clientName}
                  onChange={(e) => setClientName(e.target.value)}
                  placeholder="e.g. Sanjana Patel"
                  className="text-xs"
                  required
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-1">
                  <Label className="text-xs">Category</Label>
                  <Input
                    value={category}
                    onChange={(e) => setCategory(e.target.value)}
                    placeholder="Lehengas / Sarees"
                    className="text-xs"
                  />
                </div>
                <div className="flex flex-col gap-1">
                  <Label className="text-xs">Target Color</Label>
                  <Input
                    value={color}
                    onChange={(e) => setColor(e.target.value)}
                    placeholder="e.g. Dusty Lilac & Silver"
                    className="text-xs"
                  />
                </div>
              </div>

              <div className="flex flex-col gap-1">
                <Label className="text-xs">Bespoke Requirements / Description</Label>
                <Textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Describe custom drape, embroidery style, and deadline..."
                  rows={3}
                  className="text-xs"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="flex flex-col gap-1">
                  <Label className="text-xs">Target retail price (LKR)</Label>
                  <Input
                    type="number"
                    value={targetPrice}
                    onChange={(e) => setTargetPrice(e.target.value)}
                    className="text-xs"
                  />
                </div>
                <div className="flex flex-col gap-1">
                  <Label className="text-xs">Est. atelier cost (LKR)</Label>
                  <Input
                    type="number"
                    value={estimatedCost}
                    onChange={(e) => setEstimatedCost(e.target.value)}
                    className="text-xs"
                  />
                </div>
              </div>

              <div className="flex flex-col gap-1">
                <Label className="text-xs">Assign Partner Atelier</Label>
                <Select value={supplierId} onValueChange={setSupplierId}>
                  <SelectTrigger aria-label="Assign partner atelier" className="w-full text-xs">
                    <SelectValue placeholder="Choose an atelier" />
                  </SelectTrigger>
                  <SelectContent>
                    {suppliers.map((s) => (
                      <SelectItem key={s.id} value={s.id}>
                        {s.name} ({s.location})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              <div className="flex justify-end gap-2 pt-3 border-t border-border">
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={() => setModalOpen(false)}
                >
                  Cancel
                </Button>
                <Button type="submit" size="sm" className="gap-1.5">
                  <Check className="size-4" />
                  <span>Create Ticket</span>
                </Button>
              </div>
            </form>
          </Card>
        </div>
      )}
    </div>
  )
}
