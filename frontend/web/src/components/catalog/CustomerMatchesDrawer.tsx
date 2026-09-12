import { useState } from 'react'
import {
  X,
  Sparkles,
  MessageSquare,
  CheckCircle2,
  Heart,
  User,
  ArrowRight,
} from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type { CustomerMatchMock, InventoryItemMock } from './mockData'

interface CustomerMatchesDrawerProps {
  item: InventoryItemMock | null
  matches: CustomerMatchMock[]
  open: boolean
  onClose: () => void
  onOpenSalon?: (customerId: string, clientName: string) => void
}

export function CustomerMatchesDrawer({
  item,
  matches,
  open,
  onClose,
  onOpenSalon,
}: CustomerMatchesDrawerProps) {
  const [actedMatches, setActedMatches] = useState<Record<string, boolean>>({})

  if (!open || !item) return null

  const itemMatches = matches.filter((m) => m.itemId === item.id)

  const handleAction = (matchId: string, clientName: string, customerId: string) => {
    setActedMatches((prev) => ({ ...prev, [matchId]: true }))
    toast.success(`Salon outreach prepared for ${clientName}`, {
      description: `Recommendation note for "${item.name}" drafted in customer salon.`,
    })
    if (onOpenSalon) {
      onOpenSalon(customerId, clientName)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex justify-end bg-black/40 backdrop-blur-xs transition-opacity animate-in fade-in">
      <div className="flex h-full w-full max-w-md flex-col border-l border-border bg-background shadow-2xl animate-in slide-in-from-right duration-200">
        {/* Drawer Header */}
        <div className="flex items-center justify-between border-b border-border px-5 py-4">
          <div className="flex items-center gap-2">
            <div className="flex size-8 items-center justify-center rounded-lg bg-primary/10 text-primary">
              <Sparkles className="size-4" />
            </div>
            <div>
              <h3 className="font-serif text-base font-semibold">VIP Client Matches</h3>
              <p className="text-xs text-muted-foreground">
                Affinity matches generated from visual tags & past taste profile
              </p>
            </div>
          </div>
          <Button
            variant="ghost"
            size="sm"
            onClick={onClose}
            className="size-8 p-0 text-muted-foreground hover:text-foreground"
          >
            <X className="size-4" />
          </Button>
        </div>

        {/* Selected Product Banner */}
        <div className="flex items-center gap-3 border-b border-border/70 bg-muted/20 p-4">
          <img
            src={item.imageUrl}
            alt={item.name}
            className="size-14 rounded-lg object-cover border border-border/80 shadow-xs"
          />
          <div className="min-w-0 flex-1">
            <Badge variant="outline" className="mb-1 text-[10px] text-muted-foreground">
              {item.category} • {item.sku}
            </Badge>
            <h4 className="truncate font-serif text-sm font-medium text-foreground">
              {item.name}
            </h4>
            <div className="mt-0.5 flex items-center justify-between text-xs">
              <span className="font-semibold text-primary">${item.price.toLocaleString()}</span>
              <span className="text-[11px] text-muted-foreground">{item.color}</span>
            </div>
          </div>
        </div>

        {/* Matches List */}
        <div className="flex-1 overflow-y-auto p-4 space-y-3">
          {itemMatches.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-center text-muted-foreground">
              <Heart className="size-10 stroke-1 text-muted-foreground/40 mb-2" />
              <p className="text-sm font-medium text-foreground">No VIP matches yet</p>
              <p className="text-xs text-muted-foreground max-w-xs mt-1">
                When clients save preferences or purchase history matching this color/fabric, Elle
                will surface recommendations here.
              </p>
            </div>
          ) : (
            itemMatches.map((match) => {
              const acted = actedMatches[match.id] ?? match.employeeActed
              const scorePct = Math.round(match.matchConfidence * 100)

              return (
                <Card
                  key={match.id}
                  className="border-border/80 bg-card p-4 transition-all hover:border-border hover:shadow-xs"
                >
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex items-center gap-2.5">
                      <div className="flex size-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-xs font-semibold text-primary">
                        {match.customerAvatar ?? <User className="size-4" />}
                      </div>
                      <div className="min-w-0">
                        <p className="truncate text-sm font-medium text-foreground">
                          {match.customerName}
                        </p>
                        <p className="truncate text-xs text-muted-foreground">
                          {match.customerEmail}
                        </p>
                      </div>
                    </div>

                    {/* Score pill */}
                    <Badge
                      variant="secondary"
                      className="shrink-0 bg-primary/10 text-primary border-primary/20 text-xs font-medium"
                    >
                      {scorePct}% Match
                    </Badge>
                  </div>

                  {/* Affinity match score progress bar */}
                  <div className="mt-3">
                    <div className="h-1.5 w-full rounded-full bg-muted overflow-hidden">
                      <div
                        className="h-full rounded-full bg-primary transition-all duration-500"
                        style={{ width: `${scorePct}%` }}
                      />
                    </div>
                  </div>

                  {/* AI Match Reason */}
                  <div className="mt-3 rounded-lg border border-border/60 bg-muted/30 p-2.5 text-xs text-foreground/90">
                    <div className="flex items-center gap-1 text-[11px] font-medium text-muted-foreground mb-1">
                      <Sparkles className="size-3 text-primary" />
                      <span>Affinity Reasoning</span>
                    </div>
                    <p className="leading-relaxed text-[11px] text-muted-foreground">
                      {match.matchReason}
                    </p>
                  </div>

                  {/* Customer Preferences tags */}
                  <div className="mt-2.5 flex flex-wrap gap-1 text-[10px]">
                    {match.preferredColor && (
                      <Badge variant="outline" className="text-[10px] text-muted-foreground py-0">
                        Color: {match.preferredColor}
                      </Badge>
                    )}
                    {match.preferredFabric && (
                      <Badge variant="outline" className="text-[10px] text-muted-foreground py-0">
                        Fabric: {match.preferredFabric}
                      </Badge>
                    )}
                    {match.preferredSize && (
                      <Badge variant="outline" className="text-[10px] text-muted-foreground py-0">
                        Size: {match.preferredSize}
                      </Badge>
                    )}
                  </div>

                  {/* Action row */}
                  <div className="mt-3.5 pt-3 border-t border-border/60 flex items-center justify-between">
                    {acted ? (
                      <span className="flex items-center gap-1.5 text-xs text-emerald-600 font-medium">
                        <CheckCircle2 className="size-3.5" />
                        Outreach Contacted
                      </span>
                    ) : (
                      <span className="text-[11px] text-muted-foreground">Pending outreach</span>
                    )}

                    <Button
                      size="sm"
                      variant={acted ? 'outline' : 'default'}
                      className="gap-1.5 text-xs h-7 rounded-lg"
                      onClick={() => handleAction(match.id, match.customerName, match.customerId)}
                    >
                      <MessageSquare className="size-3" />
                      <span>{acted ? 'Re-open Salon' : 'Start Outreach'}</span>
                      <ArrowRight className="size-3 ml-0.5" />
                    </Button>
                  </div>
                </Card>
              )
            })
          )}
        </div>
      </div>
    </div>
  )
}
