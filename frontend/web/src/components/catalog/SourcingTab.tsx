import { useState } from 'react'
import {
  Plus,
  Building2,
  X,
  Check,
} from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
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
  { id: 'pending', label: 'Pending Quote', color: 'bg-amber-500/10 text-amber-600 border-amber-500/20' },
  { id: 'quoted', label: 'Quoted by Atelier', color: 'bg-blue-500/10 text-blue-600 border-blue-500/20' },
  { id: 'approved', label: 'Approved', color: 'bg-purple-500/10 text-purple-600 border-purple-500/20' },
  { id: 'ordered', label: 'Ordered from Atelier', color: 'bg-indigo-500/10 text-indigo-600 border-indigo-500/20' },
  { id: 'fulfilled', label: 'Fulfilled', color: 'bg-emerald-500/10 text-emerald-600 border-emerald-500/20' },
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
    <div className="space-y-6">
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

        <Button
          size="sm"
          onClick={() => setModalOpen(true)}
          className="gap-1.5 rounded-xl text-xs h-9 px-4 shadow-sm"
        >
          <Plus className="size-4" />
          <span>New Sourcing Ticket</span>
        </Button>
      </div>

      {/* Kanban Stages Grid */}
      <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-5">
        {STAGES.map((stage) => {
          const stageTickets = sourcingRequests.filter((t) => t.status === stage.id)

          return (
            <div key={stage.id} className="flex flex-col rounded-xl border border-border/80 bg-muted/20 p-3">
              {/* Stage Header */}
              <div className="flex items-center justify-between mb-3 px-1">
                <Badge variant="outline" className={`text-[11px] font-medium ${stage.color}`}>
                  {stage.label}
                </Badge>
                <span className="text-xs font-semibold text-muted-foreground font-mono">
                  {stageTickets.length}
                </span>
              </div>

              {/* Tickets Column */}
              <div className="flex-1 space-y-3 min-h-[300px]">
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

                    return (
                      <Card
                        key={ticket.id}
                        className="flex flex-col overflow-hidden border-border/80 bg-card p-3.5 shadow-2xs transition-all hover:border-border hover:shadow-xs"
                      >
                        {/* Reference Image */}
                        {ticket.referenceImageUrl && (
                          <img
                            src={ticket.referenceImageUrl}
                            alt={ticket.category}
                            className="h-28 w-full rounded-lg object-cover border border-border mb-2.5"
                          />
                        )}

                        <div className="flex items-center justify-between text-[10px] text-muted-foreground mb-1">
                          <span className="font-semibold text-foreground">{ticket.clientName}</span>
                          <span className="font-mono">{ticket.category}</span>
                        </div>

                        <p className="line-clamp-2 text-xs text-muted-foreground leading-snug mb-2.5">
                          {ticket.itemDescription}
                        </p>

                        {/* Financials & Markup */}
                        <div className="rounded-lg bg-muted/40 p-2 text-[11px] space-y-1 mb-3">
                          <div className="flex justify-between">
                            <span className="text-muted-foreground">Target Retail:</span>
                            <span className="font-semibold text-primary">
                              ${ticket.targetPrice.toLocaleString()}
                            </span>
                          </div>
                          <div className="flex justify-between text-muted-foreground">
                            <span>Atelier Cost:</span>
                            <span>${ticket.estimatedCost.toLocaleString()}</span>
                          </div>
                          <div className="flex justify-between font-medium text-emerald-600 dark:text-emerald-400 pt-1 border-t border-border/50">
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
                          <select
                            value={ticket.status}
                            onChange={(e) =>
                              onUpdateStatus(ticket.id, e.target.value as SourcingRequestMock['status'])
                            }
                            className="w-full rounded-md border border-input bg-background py-1 px-2 text-[10px] text-foreground"
                          >
                            <option value="pending">Stage: Pending Quote</option>
                            <option value="quoted">Stage: Quoted</option>
                            <option value="approved">Stage: Approved</option>
                            <option value="ordered">Stage: Ordered</option>
                            <option value="fulfilled">Stage: Fulfilled</option>
                          </select>
                        </div>
                      </Card>
                    )
                  })
                )}
              </div>
            </div>
          )
        })}
      </div>

      {/* New Sourcing Ticket Modal */}
      {modalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-xs p-4 animate-in fade-in">
          <Card className="w-full max-w-lg border-border bg-background shadow-2xl p-6 space-y-4 animate-in zoom-in-95">
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

            <form onSubmit={handleCreateTicket} className="space-y-4">
              <div className="space-y-1">
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
                <div className="space-y-1">
                  <Label className="text-xs">Category</Label>
                  <Input
                    value={category}
                    onChange={(e) => setCategory(e.target.value)}
                    placeholder="Lehengas / Sarees"
                    className="text-xs"
                  />
                </div>
                <div className="space-y-1">
                  <Label className="text-xs">Target Color</Label>
                  <Input
                    value={color}
                    onChange={(e) => setColor(e.target.value)}
                    placeholder="e.g. Dusty Lilac & Silver"
                    className="text-xs"
                  />
                </div>
              </div>

              <div className="space-y-1">
                <Label className="text-xs">Bespoke Requirements / Description</Label>
                <textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Describe custom drape, embroidery style, and deadline..."
                  rows={3}
                  className="w-full rounded-md border border-input bg-background p-2.5 text-xs text-foreground"
                />
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label className="text-xs">Target Retail Price ($)</Label>
                  <Input
                    type="number"
                    value={targetPrice}
                    onChange={(e) => setTargetPrice(e.target.value)}
                    className="text-xs"
                  />
                </div>
                <div className="space-y-1">
                  <Label className="text-xs">Est. Atelier Cost ($)</Label>
                  <Input
                    type="number"
                    value={estimatedCost}
                    onChange={(e) => setEstimatedCost(e.target.value)}
                    className="text-xs"
                  />
                </div>
              </div>

              <div className="space-y-1">
                <Label className="text-xs">Assign Partner Atelier</Label>
                <select
                  value={supplierId}
                  onChange={(e) => setSupplierId(e.target.value)}
                  className="w-full rounded-md border border-input bg-background p-2 text-xs"
                >
                  {suppliers.map((s) => (
                    <option key={s.id} value={s.id}>
                      {s.name} ({s.location})
                    </option>
                  ))}
                </select>
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
