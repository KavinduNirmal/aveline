import { useState } from 'react'
import { Sparkles, Layers } from 'lucide-react'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import type { OutfitCompositionMock } from './mockData'

interface LookbooksTabProps {
  outfits: OutfitCompositionMock[]
  onComposeLook: () => void
}

const OCCASION_FILTERS = [
  'All',
  'Sangeet & Reception',
  'Groom Royal Wedding',
  'Bridal Heirloom',
  'Cocktail Reception & Gala',
]

export function LookbooksTab({ outfits, onComposeLook }: LookbooksTabProps) {
  const [selectedOccasion, setSelectedOccasion] = useState('All')

  const filteredOutfits = outfits.filter((outfit) => {
    if (selectedOccasion === 'All') return true
    return outfit.occasion.toLowerCase().includes(selectedOccasion.toLowerCase())
  })

  return (
    <div className="space-y-6">
      {/* Top Bar with Trigger and Filters */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-1.5 overflow-x-auto pb-1 text-xs">
          {OCCASION_FILTERS.map((occ) => (
            <button
              key={occ}
              type="button"
              onClick={() => setSelectedOccasion(occ)}
              className={`rounded-full px-3.5 py-1.5 text-xs font-medium transition-colors shrink-0 ${
                selectedOccasion === occ
                  ? 'bg-primary text-white shadow-xs'
                  : 'bg-muted/60 text-muted-foreground hover:bg-muted hover:text-foreground'
              }`}
            >
              {occ}
            </button>
          ))}
        </div>

        <Button
          size="sm"
          onClick={onComposeLook}
          className="gap-1.5 rounded-xl text-xs h-9 px-4 shrink-0 shadow-sm"
        >
          <Sparkles className="size-3.5" />
          <span>Compose Look with Elle</span>
        </Button>
      </div>

      {/* Lookbooks Grid */}
      {filteredOutfits.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center border-dashed border-border/80 bg-card/50">
          <Layers className="size-12 text-muted-foreground/40 mb-3" />
          <h4 className="font-serif text-base font-medium">No Lookbooks Found</h4>
          <p className="text-xs text-muted-foreground max-w-sm mt-1 mb-4">
            Curate your first styled ensemble with Elle to present complete looks to your clients.
          </p>
          <Button size="sm" onClick={onComposeLook} className="gap-1.5">
            <Sparkles className="size-3.5" />
            <span>Compose First Look</span>
          </Button>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-2">
          {filteredOutfits.map((outfit) => (
            <Card
              key={outfit.id}
              className="overflow-hidden border-border/80 bg-card transition-all hover:border-border hover:shadow-md"
            >
              {/* Card Header & Occasion */}
              <div className="p-5 border-b border-border/70 flex items-start justify-between gap-3">
                <div>
                  <Badge variant="outline" className="text-[10px] text-primary border-primary/20 mb-1.5">
                    {outfit.occasion}
                  </Badge>
                  <h3 className="font-serif text-base font-semibold text-foreground">
                    {outfit.name}
                  </h3>
                </div>

                <div className="text-right shrink-0">
                  <span className="text-[10px] uppercase tracking-wider text-muted-foreground block">
                    Total Ensemble
                  </span>
                  <span className="font-serif text-lg font-bold text-primary">
                    ${outfit.totalPrice.toLocaleString()}
                  </span>
                </div>
              </div>

              {/* Items Composition Preview */}
              <div className="p-5 space-y-4">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  {outfit.items.map((item) => (
                    <div
                      key={item.id}
                      className="flex items-center gap-3 rounded-xl border border-border/60 bg-muted/20 p-2.5"
                    >
                      <img
                        src={item.imageUrl}
                        alt={item.name}
                        className="size-14 rounded-lg object-cover border border-border shrink-0"
                      />
                      <div className="min-w-0 flex-1">
                        <Badge variant="outline" className="text-[9px] uppercase tracking-wider py-0">
                          {item.position}
                        </Badge>
                        <p className="truncate text-xs font-medium text-foreground mt-0.5">
                          {item.name}
                        </p>
                        <p className="text-xs font-semibold text-primary">
                          ${item.price.toLocaleString()}
                        </p>
                      </div>
                    </div>
                  ))}
                </div>

                {/* Elle Stylist Commentary */}
                {outfit.styleNotes && (
                  <div className="rounded-xl border border-primary/20 bg-primary/5 p-3.5 text-xs text-foreground/90 leading-relaxed">
                    <div className="flex items-center gap-1.5 text-[11px] font-semibold text-primary mb-1">
                      <Sparkles className="size-3.5" />
                      <span>Stylist Drape Notes by Elle</span>
                    </div>
                    <p className="text-xs text-muted-foreground leading-relaxed">
                      {outfit.styleNotes}
                    </p>
                  </div>
                )}
              </div>
            </Card>
          ))}
        </div>
      )}
    </div>
  )
}
