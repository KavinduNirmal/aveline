import { useEffect, useState } from 'react'
import {
  Sparkles,
  Layers,
  MoreHorizontal,
  Pencil,
  Trash2,
  Loader2,
  X,
  AlertTriangle,
} from 'lucide-react'

import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { formatMoney } from '@/lib/format-money'
import type { UpdateLookbookPayload } from '@/types/catalog'
import type { OutfitCompositionMock } from './mockData'

interface LookbooksTabProps {
  outfits: OutfitCompositionMock[]
  onComposeLook: () => void
  /** Renames or re-occasions a look. Resolves `true` when the server accepted it. */
  onUpdateLookbook: (id: string, payload: UpdateLookbookPayload) => Promise<boolean>
  /** Removes a look. Resolves `true` when the server accepted it. */
  onDeleteLookbook: (id: string) => Promise<boolean>
}

const OCCASION_FILTERS = [
  'Sangeet & Reception',
  'Groom Royal Wedding',
  'Bridal Heirloom',
  'Cocktail Reception & Gala',
]

const OCCASION_FILTER_PILLS = ['All', ...OCCASION_FILTERS]

export function LookbooksTab({
  outfits,
  onComposeLook,
  onUpdateLookbook,
  onDeleteLookbook,
}: LookbooksTabProps) {
  const [selectedOccasion, setSelectedOccasion] = useState('All')
  const [editTarget, setEditTarget] = useState<OutfitCompositionMock | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<OutfitCompositionMock | null>(null)

  const filteredOutfits = outfits.filter((outfit) => {
    if (selectedOccasion === 'All') return true
    return outfit.occasion.toLowerCase().includes(selectedOccasion.toLowerCase())
  })

  return (
    <div className="flex flex-col gap-6">
      {/* Top Bar with Trigger and Filters */}
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-1.5 overflow-x-auto pb-1 text-xs">
          {OCCASION_FILTER_PILLS.map((occ) => (
            <Button
              key={occ}
              type="button"
              variant={selectedOccasion === occ ? 'default' : 'secondary'}
              size="sm"
              onClick={() => setSelectedOccasion(occ)}
              className="h-auto shrink-0 rounded-full px-3.5 py-1.5 text-xs font-medium"
            >
              {occ}
            </Button>
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
                <div className="min-w-0">
                  <Badge variant="outline" className="text-[10px] text-primary border-primary/20 mb-1.5">
                    {outfit.occasion}
                  </Badge>
                  <h3 className="font-serif text-base font-semibold text-foreground">
                    {outfit.name}
                  </h3>
                </div>

                <div className="flex shrink-0 items-start gap-2">
                  <div className="text-right">
                    <span className="text-[10px] uppercase tracking-wider text-muted-foreground block">
                      Total Ensemble
                    </span>
                    <span className="font-serif text-lg font-bold text-primary">
                      {formatMoney(outfit.totalPrice)}
                    </span>
                  </div>

                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        type="button"
                        size="sm"
                        variant="ghost"
                        aria-label={`Actions for ${outfit.name}`}
                        className="size-7 rounded-full p-0 text-muted-foreground hover:bg-muted hover:text-foreground"
                      >
                        <MoreHorizontal className="size-4" aria-hidden />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem onSelect={() => setEditTarget(outfit)}>
                        <Pencil className="size-3.5" aria-hidden /> Edit lookbook
                      </DropdownMenuItem>
                      <DropdownMenuItem variant="destructive" onSelect={() => setDeleteTarget(outfit)}>
                        <Trash2 className="size-3.5" aria-hidden /> Delete lookbook
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </div>

              {/* Items Composition Preview */}
              <div className="flex flex-col p-5 gap-4">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  {outfit.items.map((item) => (
                    <div
                      key={item.id}
                      className="flex items-center gap-3 rounded-xl border border-border/60 bg-muted/20 p-2.5"
                    >
                      {item.imageUrl ? (
                        <img
                          src={item.imageUrl}
                          alt={item.name}
                          className="size-14 rounded-lg object-cover border border-border shrink-0"
                        />
                      ) : (
                        // An absent photograph is a state, not an empty `src` that re-requests the page.
                        <div className="flex size-14 shrink-0 items-center justify-center rounded-lg border border-border bg-muted text-[9px] text-muted-foreground">
                          No photo
                        </div>
                      )}
                      <div className="min-w-0 flex-1">
                        <Badge variant="outline" className="text-[9px] uppercase tracking-wider py-0">
                          {item.position}
                        </Badge>
                        <p className="truncate text-xs font-medium text-foreground mt-0.5">
                          {item.name}
                        </p>
                        <p className="text-xs font-semibold text-primary">
                          {formatMoney(item.price)}
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

      <EditLookbookModal
        outfit={editTarget}
        onClose={() => setEditTarget(null)}
        onSave={async (id, payload) => {
          const saved = await onUpdateLookbook(id, payload)
          if (saved) setEditTarget(null)
          return saved
        }}
      />

      <DeleteLookbookModal
        outfit={deleteTarget}
        onClose={() => setDeleteTarget(null)}
        onConfirm={async (id) => {
          const deleted = await onDeleteLookbook(id)
          if (deleted) setDeleteTarget(null)
          return deleted
        }}
      />
    </div>
  )
}

/** Rename a look, change its occasion, or rewrite Elle's notes. The item composition is not editable here. */
function EditLookbookModal({
  outfit,
  onClose,
  onSave,
}: {
  outfit: OutfitCompositionMock | null
  onClose: () => void
  onSave: (id: string, payload: UpdateLookbookPayload) => Promise<boolean>
}) {
  const [name, setName] = useState('')
  const [occasion, setOccasion] = useState('')
  const [styleNotes, setStyleNotes] = useState('')
  const [isSaving, setIsSaving] = useState(false)

  useEffect(() => {
    if (outfit) {
      setName(outfit.name)
      setOccasion(outfit.occasion)
      setStyleNotes(outfit.styleNotes ?? '')
      setIsSaving(false)
    }
  }, [outfit])

  if (!outfit) return null

  // A look may carry an occasion the preset list does not name; it stays selectable rather than
  // being silently rewritten to the first preset.
  const occasionOptions = OCCASION_FILTERS.includes(occasion)
    ? OCCASION_FILTERS
    : [occasion, ...OCCASION_FILTERS].filter(Boolean)

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    setIsSaving(true)
    try {
      await onSave(outfit.id, {
        name: name.trim() || undefined,
        occasion: occasion.trim() || undefined,
        // Sent even when empty: an empty note is how the operator clears it, and the server applies
        // the field whenever it is present.
        styleNotes: styleNotes.trim(),
      })
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Edit lookbook"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs animate-in fade-in duration-200"
    >
      <Card className="w-full max-w-lg overflow-hidden border-border bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div>
            <h3 className="font-serif text-base font-semibold text-foreground">Edit lookbook</h3>
            <p className="text-xs text-muted-foreground">
              Rename the look and set the occasion it is styled for.
            </p>
          </div>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={isSaving}
            onClick={onClose}
            aria-label="Close"
            className="size-8 rounded-full p-0 hover:bg-muted"
          >
            <X className="size-4" />
          </Button>
        </div>

        <form onSubmit={handleSubmit} className="flex flex-col gap-4 p-6">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="lookbook-name" className="text-xs font-medium">
              Look name
            </Label>
            <Input
              id="lookbook-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Royal Gala Ensemble"
              required
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="lookbook-occasion" className="text-xs font-medium">
              Occasion
            </Label>
            <Select value={occasion} onValueChange={setOccasion}>
              <SelectTrigger id="lookbook-occasion" aria-label="Occasion" className="w-full">
                <SelectValue placeholder="Choose an occasion" />
              </SelectTrigger>
              <SelectContent>
                {occasionOptions.map((occ) => (
                  <SelectItem key={occ} value={occ}>
                    {occ}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="lookbook-notes" className="text-xs font-medium">
              Stylist notes
            </Label>
            <Textarea
              id="lookbook-notes"
              value={styleNotes}
              onChange={(e) => setStyleNotes(e.target.value)}
              placeholder="Drape advice and pairing notes"
              rows={4}
              className="text-[13px] leading-relaxed"
            />
          </div>

          <div className="flex items-center justify-end gap-2.5 border-t border-border pt-4">
            <Button type="button" variant="outline" size="sm" disabled={isSaving} onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" size="sm" disabled={isSaving} className="gap-1.5">
              {isSaving ? <Loader2 className="size-3.5 animate-spin" /> : null}
              <span>{isSaving ? 'Saving...' : 'Save changes'}</span>
            </Button>
          </div>
        </form>
      </Card>
    </div>
  )
}

/** Removing a look deletes its composition and, with it, its item rows. */
function DeleteLookbookModal({
  outfit,
  onClose,
  onConfirm,
}: {
  outfit: OutfitCompositionMock | null
  onClose: () => void
  onConfirm: (id: string) => Promise<boolean>
}) {
  const [isDeleting, setIsDeleting] = useState(false)

  useEffect(() => {
    if (outfit) setIsDeleting(false)
  }, [outfit])

  if (!outfit) return null

  const handleConfirm = async () => {
    setIsDeleting(true)
    try {
      await onConfirm(outfit.id)
    } finally {
      setIsDeleting(false)
    }
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label="Delete lookbook"
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 p-4 backdrop-blur-xs animate-in fade-in duration-200"
    >
      <Card className="w-full max-w-md overflow-hidden border-border bg-card shadow-2xl">
        <div className="flex items-center justify-between border-b border-border bg-destructive/10 px-6 py-4">
          <div className="flex items-center gap-2.5">
            <div className="flex size-8 items-center justify-center rounded-lg border border-destructive/30 bg-destructive/20 text-destructive">
              <Trash2 className="size-4" />
            </div>
            <div>
              <h3 className="font-serif text-base font-semibold text-foreground">
                Delete lookbook
              </h3>
              <p className="text-xs text-muted-foreground">This action cannot be undone</p>
            </div>
          </div>
          <Button
            type="button"
            size="sm"
            variant="ghost"
            disabled={isDeleting}
            onClick={onClose}
            aria-label="Close"
            className="size-8 rounded-full p-0 hover:bg-muted"
          >
            <X className="size-4" />
          </Button>
        </div>

        <div className="flex flex-col gap-4 p-6">
          <div className="rounded-xl border border-border/80 bg-muted/20 p-3.5">
            <p className="text-sm font-medium text-foreground">{outfit.name}</p>
            <p className="mt-0.5 text-xs text-muted-foreground">
              {outfit.occasion} · {outfit.items.length} piece(s) · {formatMoney(outfit.totalPrice)}
            </p>
          </div>
          <div className="flex items-start gap-2 rounded-lg border border-destructive/20 bg-destructive/10 p-3 text-xs text-destructive">
            <AlertTriangle className="mt-0.5 size-4 shrink-0" />
            <p>
              Removing this lookbook takes it out of the boutique&apos;s styled looks. The catalog
              pieces it names are not deleted.
            </p>
          </div>
        </div>

        <div className="flex items-center justify-end gap-2.5 border-t border-border px-6 py-3.5">
          <Button type="button" variant="outline" size="sm" disabled={isDeleting} onClick={onClose}>
            Cancel
          </Button>
          <Button
            type="button"
            variant="destructive"
            size="sm"
            disabled={isDeleting}
            onClick={handleConfirm}
            className="gap-1.5"
          >
            {isDeleting ? <Loader2 className="size-3.5 animate-spin" /> : <Trash2 className="size-3.5" />}
            <span>{isDeleting ? 'Deleting...' : 'Delete lookbook'}</span>
          </Button>
        </div>
      </Card>
    </div>
  )
}
