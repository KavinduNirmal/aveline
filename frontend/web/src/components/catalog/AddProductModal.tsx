import { useState } from 'react'
import {
  X,
  Sparkles,
  Loader2,
  Check,
} from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { Card } from '@/components/ui/card'
import type { InventoryItemMock } from './mockData'

interface AddProductModalProps {
  open: boolean
  onClose: () => void
  onSave: (item: InventoryItemMock) => void
  editingItem?: InventoryItemMock | null
}

const CATEGORIES = [
  'Sarees',
  'Lehengas',
  'Gowns',
  'Kurtas & Tunics',
  'Outerwear',
  'Drapes & Shawls',
  'Jewelry & Accessories',
]

const SAMPLE_IMAGES = [
  {
    url: 'https://images.unsplash.com/photo-1610030469983-98e550d6193c?auto=format&fit=crop&w=800&q=80',
    title: 'Emerald Kanjeevaram Saree',
    detectedColor: 'Emerald Green',
    detectedHex: '#0f5132',
    detectedFabric: 'Mulberry Silk',
    detectedStyle: 'Traditional Heirloom',
    detectedPattern: 'Gold Zari Brocade',
  },
  {
    url: 'https://images.unsplash.com/photo-1594938298603-c8148c4dae35?auto=format&fit=crop&w=800&q=80',
    title: 'Midnight Royal Sherwani',
    detectedColor: 'Midnight Blue',
    detectedHex: '#1e293b',
    detectedFabric: 'Micro Velvet',
    detectedStyle: 'Contemporary Royal',
    detectedPattern: 'French Knot Embroidery',
  },
  {
    url: 'https://images.unsplash.com/photo-1583391733956-3750e0ff4e8b?auto=format&fit=crop&w=800&q=80',
    title: 'Rose Gold Chanderi Lehenga',
    detectedColor: 'Rose Gold',
    detectedHex: '#b76e79',
    detectedFabric: 'Chanderi Silk',
    detectedStyle: 'Festive Romantic',
    detectedPattern: 'Gota Patti Sequin',
  },
]

export function AddProductModal({
  open,
  onClose,
  onSave,
  editingItem,
}: AddProductModalProps) {
  const [name, setName] = useState(editingItem?.name ?? '')
  const [sku, setSku] = useState(editingItem?.sku ?? `AVL-${Math.floor(100 + Math.random() * 900)}`)
  const [category, setCategory] = useState(editingItem?.category ?? 'Sarees')
  const [color, setColor] = useState(editingItem?.color ?? '')
  const [colorHex, setColorHex] = useState(editingItem?.colorHex ?? '#8b2e42')
  const [fabric, setFabric] = useState(editingItem?.fabric ?? '')
  const [style, setStyle] = useState(editingItem?.style ?? '')
  const [pattern, setPattern] = useState(editingItem?.pattern ?? '')
  const [price, setPrice] = useState(editingItem?.price ? String(editingItem.price) : '1250')
  const [cost, setCost] = useState(editingItem?.cost ? String(editingItem.cost) : '550')
  const [stockQuantity, setStockQuantity] = useState(
    editingItem?.stockQuantity ? String(editingItem.stockQuantity) : '4',
  )
  const [sizesInput, setSizesInput] = useState(editingItem?.sizes.join(', ') ?? '38, 40, 42')
  const [imageUrl, setImageUrl] = useState(
    editingItem?.imageUrl ?? SAMPLE_IMAGES[0].url,
  )
  const [description, setDescription] = useState(editingItem?.description ?? '')
  const [analyzing, setAnalyzing] = useState(false)
  const [aiConfidence, setAiConfidence] = useState<number | null>(
    editingItem?.confidenceScore ?? null,
  )

  if (!open) return null

  const handleAnalyzeVision = async () => {
    if (!imageUrl.trim()) {
      toast.error('Please enter an image URL first')
      return
    }

    setAnalyzing(true)
    // Simulate Vision API multimodal processing latency
    await new Promise((resolve) => setTimeout(resolve, 900))

    const matchedSample = SAMPLE_IMAGES.find((s) => s.url === imageUrl)
    if (matchedSample) {
      setColor(matchedSample.detectedColor)
      setColorHex(matchedSample.detectedHex)
      setFabric(matchedSample.detectedFabric)
      setStyle(matchedSample.detectedStyle)
      setPattern(matchedSample.detectedPattern)
      if (!name) setName(matchedSample.title)
    } else {
      setColor('Imperial Burgundy')
      setColorHex('#800020')
      setFabric('Pure Raw Silk')
      setStyle('Contemporary Luxe')
      setPattern('Hand-embroidered Motif')
    }

    setAiConfidence(0.96)
    setAnalyzing(false)
    toast.success('Visual attributes extracted via Vision AI', {
      description: 'Fabric, color palette, and styling attributes auto-populated.',
    })
  }

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) {
      toast.error('Item name is required')
      return
    }

    const sizes = sizesInput
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean)

    const parsedPrice = parseFloat(price) || 0
    const parsedCost = parseFloat(cost) || 0
    const parsedStock = parseInt(stockQuantity, 10) || 0

    const newItem: InventoryItemMock = {
      id: editingItem?.id ?? `item-${Date.now()}`,
      name: name.trim(),
      sku: sku.trim(),
      category,
      color: color.trim() || 'Multicolor',
      colorHex,
      fabric: fabric.trim() || 'Silk Blend',
      style: style.trim() || 'Classic Luxury',
      pattern: pattern.trim() || undefined,
      sizes: sizes.length > 0 ? sizes : ['Standard'],
      price: parsedPrice,
      cost: parsedCost,
      stockQuantity: parsedStock,
      status: parsedStock === 0 ? 'reserved' : parsedStock <= 2 ? 'low_stock' : 'available',
      imageUrl: imageUrl.trim() || SAMPLE_IMAGES[0].url,
      confidenceScore: aiConfidence ?? 0.92,
      description: description.trim() || undefined,
      createdAt: editingItem?.createdAt ?? new Date().toISOString(),
    }

    onSave(newItem)
    toast.success(editingItem ? 'Piece updated in catalog' : 'New piece added to catalog')
    onClose()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-xs p-4 overflow-y-auto animate-in fade-in">
      <Card className="w-full max-w-2xl overflow-hidden border-border bg-background shadow-2xl animate-in zoom-in-95 duration-150">
        {/* Modal Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4">
          <div>
            <h3 className="font-serif text-lg font-semibold">
              {editingItem ? 'Edit Boutique Piece' : 'Add New Boutique Piece'}
            </h3>
            <p className="text-xs text-muted-foreground">
              Configure inventory attributes with automatic Vision AI feature extraction
            </p>
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

        <form onSubmit={handleSave} className="p-6 space-y-5">
          {/* Image URL & Vision Analysis Bar */}
          <div className="space-y-2">
            <Label className="text-xs font-medium">Piece Image & Vision AI Analysis</Label>
            <div className="flex gap-2">
              <Input
                value={imageUrl}
                onChange={(e) => setImageUrl(e.target.value)}
                placeholder="https://... image URL"
                className="text-xs"
              />
              <Button
                type="button"
                variant="secondary"
                onClick={handleAnalyzeVision}
                disabled={analyzing}
                className="gap-1.5 shrink-0 text-xs border border-primary/20 bg-primary/10 text-primary hover:bg-primary/20"
              >
                {analyzing ? (
                  <Loader2 className="size-3.5 animate-spin" />
                ) : (
                  <Sparkles className="size-3.5" />
                )}
                <span>{analyzing ? 'Analyzing...' : 'Extract with Vision AI'}</span>
              </Button>
            </div>

            {/* Sample Image Presets */}
            <div className="flex items-center gap-2 pt-1 text-[11px] text-muted-foreground">
              <span>Quick Presets:</span>
              {SAMPLE_IMAGES.map((sample, idx) => (
                <button
                  key={idx}
                  type="button"
                  onClick={() => {
                    setImageUrl(sample.url)
                    setName(sample.title)
                    setColor(sample.detectedColor)
                    setColorHex(sample.detectedHex)
                    setFabric(sample.detectedFabric)
                    setStyle(sample.detectedStyle)
                    setPattern(sample.detectedPattern)
                    setAiConfidence(0.96)
                  }}
                  className="rounded border border-border px-2 py-0.5 text-[10px] hover:bg-muted transition-colors"
                >
                  {sample.detectedColor}
                </button>
              ))}
            </div>
          </div>

          {/* Core Info Grid */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label className="text-xs">Item Name</Label>
              <Input
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. Royal Emerald Silk Saree"
                className="text-xs"
                required
              />
            </div>
            <div className="space-y-1.5">
              <Label className="text-xs">SKU Code</Label>
              <Input
                value={sku}
                onChange={(e) => setSku(e.target.value)}
                placeholder="AVL-SAR-001"
                className="text-xs font-mono"
                required
              />
            </div>
          </div>

          <div className="grid grid-cols-3 gap-4">
            <div className="space-y-1.5">
              <Label className="text-xs">Category</Label>
              <select
                value={category}
                onChange={(e) => setCategory(e.target.value)}
                className="w-full rounded-md border border-input bg-background px-2.5 py-1.5 text-xs text-foreground"
              >
                {CATEGORIES.map((cat) => (
                  <option key={cat} value={cat}>
                    {cat}
                  </option>
                ))}
              </select>
            </div>

            <div className="space-y-1.5">
              <Label className="text-xs">Retail Price ($)</Label>
              <Input
                type="number"
                value={price}
                onChange={(e) => setPrice(e.target.value)}
                placeholder="1450"
                className="text-xs"
                required
              />
            </div>

            <div className="space-y-1.5">
              <Label className="text-xs">Atelier Cost ($)</Label>
              <Input
                type="number"
                value={cost}
                onChange={(e) => setCost(e.target.value)}
                placeholder="650"
                className="text-xs"
              />
            </div>
          </div>

          {/* AI Extracted Visual Attributes Panel */}
          <div className="rounded-xl border border-primary/20 bg-primary/5 p-3.5 space-y-3">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-1.5 text-xs font-semibold text-primary">
                <Sparkles className="size-3.5" />
                <span>Visual AI Extracted Attributes</span>
              </div>
              {aiConfidence && (
                <Badge variant="outline" className="text-[10px] border-primary/30 text-primary">
                  {Math.round(aiConfidence * 100)}% Confidence
                </Badge>
              )}
            </div>

            <div className="grid grid-cols-3 gap-3">
              <div className="space-y-1">
                <Label className="text-[11px] text-muted-foreground">Dominant Color</Label>
                <div className="flex items-center gap-1.5">
                  <input
                    type="color"
                    value={colorHex}
                    onChange={(e) => setColorHex(e.target.value)}
                    className="size-6 rounded border border-border cursor-pointer"
                  />
                  <Input
                    value={color}
                    onChange={(e) => setColor(e.target.value)}
                    placeholder="Emerald Green"
                    className="h-7 text-xs"
                  />
                </div>
              </div>

              <div className="space-y-1">
                <Label className="text-[11px] text-muted-foreground">Detected Fabric</Label>
                <Input
                  value={fabric}
                  onChange={(e) => setFabric(e.target.value)}
                  placeholder="Pure Mulberry Silk"
                  className="h-7 text-xs"
                />
              </div>

              <div className="space-y-1">
                <Label className="text-[11px] text-muted-foreground">Style / Pattern</Label>
                <Input
                  value={pattern || style}
                  onChange={(e) => {
                    setPattern(e.target.value)
                    setStyle(e.target.value)
                  }}
                  placeholder="Zari Brocade"
                  className="h-7 text-xs"
                />
              </div>
            </div>
          </div>

          {/* Stock and Sizing */}
          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <Label className="text-xs">Initial Stock Quantity</Label>
              <Input
                type="number"
                value={stockQuantity}
                onChange={(e) => setStockQuantity(e.target.value)}
                placeholder="4"
                className="text-xs"
                required
              />
            </div>

            <div className="space-y-1.5">
              <Label className="text-xs">Available Sizes (comma-separated)</Label>
              <Input
                value={sizesInput}
                onChange={(e) => setSizesInput(e.target.value)}
                placeholder="36, 38, 40, Free Size"
                className="text-xs"
              />
            </div>
          </div>

          {/* Description */}
          <div className="space-y-1.5">
            <Label className="text-xs">Description / Styling Notes</Label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="Detailed description, weave information, styling recommendations..."
              rows={2}
              className="w-full rounded-md border border-input bg-background p-2 text-xs text-foreground"
            />
          </div>

          {/* Actions */}
          <div className="flex items-center justify-end gap-2.5 pt-3 border-t border-border">
            <Button type="button" variant="outline" size="sm" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" size="sm" className="gap-1.5">
              <Check className="size-4" />
              <span>{editingItem ? 'Save Changes' : 'Add to Catalog'}</span>
            </Button>
          </div>
        </form>
      </Card>
    </div>
  )
}
