import { useState } from 'react'
import {
  X,
  Sparkles,
  Loader2,
  Check,
  Info,
} from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type {
  InventoryItemMock,
  OutfitCompositionMock,
  OutfitItemMock,
} from './mockData'

interface ComposeOutfitModalProps {
  open: boolean
  heroItem: InventoryItemMock | null
  inventory: InventoryItemMock[]
  onClose: () => void
  onSaveOutfit: (outfit: OutfitCompositionMock) => void
}

const OCCASIONS = [
  'Sangeet & Reception',
  'Groom Royal Wedding',
  'Bridal Heirloom Ceremonial',
  'Cocktail Reception & Gala',
  'Festive Evening Soirée',
]

export function ComposeOutfitModal({
  open,
  heroItem,
  inventory,
  onClose,
  onSaveOutfit,
}: ComposeOutfitModalProps) {
  const [selectedHeroId, setSelectedHeroId] = useState<string>(
    heroItem?.id ?? inventory[0]?.id ?? '',
  )
  const [occasion, setOccasion] = useState<string>(OCCASIONS[0])
  const [composing, setComposing] = useState(false)
  const [composedItems, setComposedItems] = useState<OutfitItemMock[]>([])
  const [styleNotes, setStyleNotes] = useState<string>('')
  const [lookName, setLookName] = useState<string>('')

  if (!open) return null

  const activeHero = inventory.find((i) => i.id === selectedHeroId) ?? heroItem

  const handleComposeWithElle = async () => {
    if (!activeHero) return

    setComposing(true)
    await new Promise((resolve) => setTimeout(resolve, 1000))

    // Complementary items from inventory
    const otherItems = inventory.filter((i) => i.id !== activeHero.id)
    const complement = otherItems[0] ?? activeHero

    const items: OutfitItemMock[] = [
      {
        id: `oi-${Date.now()}-1`,
        itemId: activeHero.id,
        name: activeHero.name,
        category: activeHero.category,
        price: activeHero.price,
        imageUrl: activeHero.imageUrl,
        position: 'top',
        notes: 'Primary statement piece',
      },
    ]

    if (complement) {
      items.push({
        id: `oi-${Date.now()}-2`,
        itemId: complement.id,
        name: complement.name,
        category: complement.category,
        price: complement.price,
        imageUrl: complement.imageUrl,
        position: 'drape',
        notes: 'Complementary color & fabric pairing',
      })
    }

    setComposedItems(items)
    setLookName(`${occasion} - ${activeHero.color} Edition`)
    setStyleNotes(
      `Elle suggests pairing "${activeHero.name}" (${activeHero.fabric}) with accent drapes to accentuate the silhouette for a ${occasion}. Gold jewelry and minimalist footwear balance the visual harmony.`,
    )
    setComposing(false)
    toast.success('Outfit look composed by Elle', {
      description: 'Styling recommendations and combined pricing calculated.',
    })
  }

  const totalPrice = composedItems.reduce((sum, item) => sum + item.price, 0)

  const handleSave = () => {
    if (composedItems.length === 0 || !activeHero) {
      toast.error('Please generate an outfit look first')
      return
    }

    const newOutfit: OutfitCompositionMock = {
      id: `outfit-${Date.now()}`,
      name: lookName || `${occasion} Ensemble`,
      occasion,
      totalPrice,
      styleNotes,
      heroImageUrl: activeHero.imageUrl,
      createdAt: new Date().toISOString(),
      items: composedItems,
    }

    onSaveOutfit(newOutfit)
    toast.success('Lookbook ensemble saved')
    onClose()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-xs p-4 overflow-y-auto animate-in fade-in">
      <Card className="w-full max-w-2xl overflow-hidden border-border bg-background shadow-2xl animate-in zoom-in-95 duration-150">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div className="flex items-center gap-2">
            <div className="flex size-8 items-center justify-center rounded-lg bg-primary/10 text-primary">
              <Sparkles className="size-4" />
            </div>
            <div>
              <h3 className="font-serif text-lg font-semibold">Compose Outfit Look</h3>
              <p className="text-xs text-muted-foreground">
                Elle (Visual Stylist Agent) assembles complementary boutique catalogue pieces
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

        <div className="p-6 space-y-5">
          {/* Hero Item & Occasion Selection */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label className="text-xs">Primary Hero Piece</Label>
              <select
                value={selectedHeroId}
                onChange={(e) => setSelectedHeroId(e.target.value)}
                className="w-full rounded-md border border-input bg-background px-3 py-1.5 text-xs text-foreground"
              >
                {inventory.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.name} (${item.price})
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1.5">
              <Label className="text-xs">Target Occasion</Label>
              <select
                value={occasion}
                onChange={(e) => setOccasion(e.target.value)}
                className="w-full rounded-md border border-input bg-background px-3 py-1.5 text-xs text-foreground"
              >
                {OCCASIONS.map((occ) => (
                  <option key={occ} value={occ}>
                    {occ}
                  </option>
                ))}
              </select>
            </div>
          </div>

          {/* Trigger Elle Agent */}
          <div className="flex items-center justify-between rounded-xl border border-primary/20 bg-primary/5 p-4">
            <div>
              <p className="text-xs font-semibold text-primary flex items-center gap-1.5">
                <Sparkles className="size-3.5" />
                Elle Visual Intelligence Engine
              </p>
              <p className="text-[11px] text-muted-foreground mt-0.5">
                Analyzes fabric contrasts, color harmony, and occasion etiquette.
              </p>
            </div>
            <Button
              size="sm"
              onClick={handleComposeWithElle}
              disabled={composing}
              className="gap-1.5 text-xs"
            >
              {composing ? (
                <Loader2 className="size-3.5 animate-spin" />
              ) : (
                <Sparkles className="size-3.5" />
              )}
              <span>{composing ? 'Elle is styling...' : 'Generate Look'}</span>
            </Button>
          </div>

          {/* Composed Look Preview */}
          {composedItems.length > 0 && (
            <div className="space-y-4 rounded-xl border border-border p-4 bg-muted/20">
              <div className="flex items-center justify-between border-b border-border pb-2.5">
                <h4 className="font-serif text-sm font-semibold">{lookName}</h4>
                <div className="text-right">
                  <span className="text-[11px] text-muted-foreground mr-1.5">Total Look:</span>
                  <span className="font-serif text-base font-bold text-primary">
                    ${totalPrice.toLocaleString()}
                  </span>
                </div>
              </div>

              {/* Items in Ensemble */}
              <div className="grid grid-cols-2 gap-3">
                {composedItems.map((item) => (
                  <div
                    key={item.id}
                    className="flex items-center gap-2.5 rounded-lg border border-border/70 bg-card p-2.5 shadow-2xs"
                  >
                    <img
                      src={item.imageUrl}
                      alt={item.name}
                      className="size-12 rounded object-cover border border-border"
                    />
                    <div className="min-w-0 flex-1">
                      <Badge variant="outline" className="text-[9px] uppercase tracking-wider py-0">
                        {item.position}
                      </Badge>
                      <p className="truncate text-xs font-medium text-foreground mt-0.5">
                        {item.name}
                      </p>
                      <p className="text-[11px] font-semibold text-primary">
                        ${item.price.toLocaleString()}
                      </p>
                    </div>
                  </div>
                ))}
              </div>

              {/* Elle Stylist Commentary */}
              {styleNotes && (
                <div className="rounded-lg bg-card border border-border/80 p-3 text-xs leading-relaxed text-muted-foreground">
                  <div className="flex items-center gap-1.5 font-medium text-foreground text-[11px] mb-1">
                    <Info className="size-3.5 text-primary" />
                    <span>Stylist Notes by Elle</span>
                  </div>
                  <p className="text-[11px] text-muted-foreground">{styleNotes}</p>
                </div>
              )}
            </div>
          )}

          {/* Footer Actions */}
          <div className="flex items-center justify-end gap-2.5 pt-3 border-t border-border">
            <Button type="button" variant="outline" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button
              type="button"
              size="sm"
              onClick={handleSave}
              disabled={composedItems.length === 0}
              className="gap-1.5"
            >
              <Check className="size-4" />
              <span>Save to Lookbook</span>
            </Button>
          </div>
        </div>
      </Card>
    </div>
  )
}
