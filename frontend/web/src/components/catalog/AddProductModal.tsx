import { useEffect, useRef, useState } from 'react'
import {
  Sparkles,
  Loader2,
  Check,
  Upload,
  Image as ImageIcon,
  Link as LinkIcon,
  Trash2,
  RotateCw,
} from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
} from '@/components/ui/sheet'
import { Input } from '@/components/ui/input'
import { Textarea } from '@/components/ui/textarea'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Label } from '@/components/ui/label'
import { Badge } from '@/components/ui/badge'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import {
  analyzeProductImage,
  getColorHex,
  normalizeCategory,
  uploadBase64Image,
  CATALOG_CATEGORIES,
  DEFAULT_COLOR_HEX,
  type CatalogCategory,
} from '@/lib/catalog-api'
import { extractVisualAttributesAndColor } from '@/lib/color-extractor'
import {
  compressAndResizeImage,
  formatFileSize,
  isValidImageFile,
} from '@/lib/image-optimizer'
import type { InventoryItemMock } from './mockData'
import { FloorTagStudio } from './FloorTagStudio'

interface AddProductModalProps {
  open: boolean
  organizationId?: string
  /** The boutique's slug, so a floor tag's encoded URL names the shop. */
  organizationSlug?: string
  onClose: () => void
  onSave: (item: InventoryItemMock) => void
  onDelete?: (item: InventoryItemMock) => void
  editingItem?: InventoryItemMock | null
}

const CATEGORIES = [...CATALOG_CATEGORIES]

/**
 * One task in the piece form. The drawer is long by nature - a photograph, its extracted
 * attributes, pricing and stock - so it is grouped into labelled steps instead of a single column
 * of fields, which is what made the previous layout read as a wall.
 */
function FormSection({
  step,
  title,
  description,
  children,
}: {
  step: number
  title: string
  description?: string
  children: React.ReactNode
}) {
  return (
    <section className="flex flex-col gap-3.5 border-t pt-5 first:border-t-0 first:pt-0">
      <div className="flex items-baseline gap-2.5">
        <span className="flex size-5 shrink-0 items-center justify-center rounded-full bg-primary/10 text-[11px] font-semibold text-primary">
          {step}
        </span>
        <div>
          <h3 className="text-sm font-medium">{title}</h3>
          {description ? (
            <p className="mt-0.5 text-xs text-muted-foreground">{description}</p>
          ) : null}
        </div>
      </div>
      <div className="flex flex-col gap-3.5 pl-0 sm:pl-7.5">{children}</div>
    </section>
  )
}

/** A labelled control with its optional hint, so every field states what it wants the same way. */
function Field({
  label,
  htmlFor,
  hint,
  children,
}: {
  label: string
  htmlFor?: string
  hint?: string
  children: React.ReactNode
}) {
  return (
    <div className="flex flex-col gap-1.5">
      <Label htmlFor={htmlFor} className="text-xs font-medium">
        {label}
      </Label>
      {children}
      {hint ? <p className="text-[11px] text-muted-foreground">{hint}</p> : null}
    </div>
  )
}

export function AddProductModal({
  open,
  organizationId,
  organizationSlug,
  onClose,
  onSave,
  onDelete,
  editingItem,
}: AddProductModalProps) {
  const [name, setName] = useState(editingItem?.name ?? '')
  const [sku, setSku] = useState(editingItem?.sku ?? `AVL-${Math.floor(100 + Math.random() * 900)}`)
  const [category, setCategory] = useState(editingItem?.category ?? 'Sarees')
  const [garmentType, setGarmentType] = useState<string | null>(null)
  const [color, setColor] = useState(editingItem?.color ?? '')
  // The state holds only a real measurement. An unmeasured colour stays empty and the control
  // below supplies its own default at render time; a default in the state would be saved as
  // though it had been measured.
  const [colorHex, setColorHex] = useState(editingItem?.colorHex ?? '')
  const [fabric, setFabric] = useState(editingItem?.fabric ?? '')
  const [style, setStyle] = useState(editingItem?.style ?? '')
  const [pattern, setPattern] = useState(editingItem?.pattern ?? '')
  const [price, setPrice] = useState(editingItem?.price ? String(editingItem.price) : '1250')
  const [cost, setCost] = useState(editingItem?.cost ? String(editingItem.cost) : '550')
  const [stockQuantity, setStockQuantity] = useState(
    editingItem?.stockQuantity ? String(editingItem.stockQuantity) : '4',
  )
  const [sizesInput, setSizesInput] = useState(
    Array.isArray(editingItem?.sizes)
      ? editingItem.sizes.join(', ')
      : typeof editingItem?.sizes === 'string'
        ? (editingItem?.sizes as string)
        : '38, 40, 42',
  )
  const [imageUrl, setImageUrl] = useState(
    editingItem?.imageUrl ?? '',
  )
  // The stored row id of the current photograph, when it has been uploaded. The saved item needs
  // the relative `url` in `imageUrl`, but the vision provider cannot read a relative path, so the
  // id is what the manual re-analysis addresses. Kept in lockstep with `imageUrl`: every path that
  // changes or clears the photograph clears this too.
  const [uploadedImageId, setUploadedImageId] = useState<string | null>(null)
  const [description, setDescription] = useState(editingItem?.description ?? '')
  const [analyzing, setAnalyzing] = useState(false)
  const [generatingDescription, setGeneratingDescription] = useState(false)
  const [aiConfidence, setAiConfidence] = useState<number | null>(
    editingItem?.confidenceScore ?? null,
  )

  // File import and dual-mode states
  const [inputMode, setInputMode] = useState<'upload' | 'url'>('upload')
  const [isDragging, setIsDragging] = useState(false)
  const [selectedFileName, setSelectedFileName] = useState<string | null>(null)
  const [selectedFileSize, setSelectedFileSize] = useState<number | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [isSaving, setIsSaving] = useState(false)

  // The floor-tag studio owns its own encoding, copy, download and print state; it was extracted
  // into `FloorTagStudio`, so this drawer keeps only the identity that tag needs.
  const effectiveItemId = editingItem?.id || 'prospective-piece'

  useEffect(() => {
    if (open) {
      if (editingItem) {
        setName(editingItem.name || '')
        setSku(editingItem.sku || `AVL-${Math.floor(100 + Math.random() * 900)}`)
        setCategory(editingItem.category || 'Sarees')
        setColor(editingItem.color || '')
        setColorHex(editingItem.colorHex || '')
        setFabric(editingItem.fabric || '')
        setStyle(editingItem.style || '')
        setPattern(editingItem.pattern || '')
        setPrice(editingItem.price ? String(editingItem.price) : '1250')
        setCost(editingItem.cost ? String(editingItem.cost) : '550')
        setStockQuantity(editingItem.stockQuantity ? String(editingItem.stockQuantity) : '4')
        setSizesInput(
          Array.isArray(editingItem.sizes)
            ? editingItem.sizes.join(', ')
            : typeof editingItem.sizes === 'string'
              ? (editingItem.sizes as string)
              : '38, 40, 42',
        )
        setImageUrl(editingItem.imageUrl || '')
        setUploadedImageId(null)
        setDescription(editingItem.description || '')
        setAiConfidence(editingItem.confidenceScore ?? null)
        setSelectedFileName(null)
        setSelectedFileSize(null)
      } else {
        setName('')
        setSku(`AVL-${Math.floor(100 + Math.random() * 900)}`)
        setCategory('Sarees')
        setGarmentType(null)
        setColor('')
        // No measured colour yet, so no hex is held. The colour control still renders a usable
        // swatch by falling back at the input itself (see the `value={colorHex || …}` binding);
        // seeding the state with a literal here would persist a colour nobody measured.
        setColorHex('')
        setFabric('')
        setStyle('')
        setPattern('')
        setPrice('1250')
        setCost('550')
        setStockQuantity('4')
        setSizesInput('38, 40, 42')
        setImageUrl('')
        setUploadedImageId(null)
        setDescription('')
        setAiConfidence(null)
        setSelectedFileName(null)
        setSelectedFileSize(null)
      }
    }
  }, [open, editingItem])

  if (!open) return null

  const handleGenerateDescriptionWithAi = () => {
    setGeneratingDescription(true)
    try {
      const activeCategory = category || 'Sarees'
      const activeColor = (color && color.trim()) || 'Crimson Red'
      const activeFabric = (fabric && fabric.trim()) || 'Pure Mulberry Silk'
      const activeGarment = (garmentType && garmentType.trim()) ||
        (name && name.trim()) ||
        (activeCategory === 'Sarees' ? 'Silk Kanjeevaram Saree' : `${activeColor} ${activeCategory}`)
      const activePattern = (pattern && pattern.trim()) || 'Gold Zari Brocade'

      let narrative = ''
      let styling = ''

      switch (activeCategory) {
        case 'Sarees':
          narrative = `Exquisite ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} woven from authentic ${activeFabric.toLowerCase()}, featuring an opulent ${activePattern.toLowerCase()} with a lustrous heirloom drape. Tailored with meticulous craftsmanship, making it a centerpiece for weddings, celebratory galas, and festive receptions.`
          styling = 'Accentuate with handcrafted polki or antique gold jewelry, an embellished clutch, and sleek stilettos for a timeless boutique statement.'
          break
        case 'Lehengas':
          narrative = `Regal ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} tailored in rich ${activeFabric.toLowerCase()}, accented with intricate ${activePattern.toLowerCase()} and a voluminous bridal flare. Designed for high-octane celebrations and modern royal occasions.`
          styling = 'Pair with a statement kundan choker set, embellished juttis, and an artisan potli bag.'
          break
        case 'Gowns':
          narrative = `Sculpted ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} in luminous ${activeFabric.toLowerCase()}, showcasing refined ${activePattern.toLowerCase()} detailing and modern red-carpet allure. Crafted for black-tie galas and luxury evening receptions.`
          styling = 'Complement with diamond drop earrings, minimalist strappy heels, and an elegant satin minaudière.'
          break
        case 'Kurtas & Tunics':
          narrative = `Sophisticated ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} crafted from breathable ${activeFabric.toLowerCase()}, highlighted by subtle ${activePattern.toLowerCase()} accents and tailored comfort. Ideal for intimate festive gatherings and curated daytime luxury.`
          styling = 'Pair with tapered silk trousers, kolhapuri wedges, and understated pearl studs.'
          break
        case 'Outerwear':
          narrative = `Distinguished ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} crafted from structured ${activeFabric.toLowerCase()}, showcasing artisanal ${activePattern.toLowerCase()} finishes. Designed for regal winter ceremonies and formal receptions.`
          styling = 'Layer over monochromatic silk ensembles with polished leather mojaris or dress shoes.'
          break
        case 'Drapes & Shawls':
          narrative = `Heirloom-grade ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} spun from ultra-fine ${activeFabric.toLowerCase()}, detailed with traditional ${activePattern.toLowerCase()} motifs. Perfect for adding warmth and regal distinction.`
          styling = 'Drape gracefully over tailored sherwanis, classic silk sarees, or sleeveless evening gowns.'
          break
        case 'Jewelry & Accessories':
          narrative = `Bespoke ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} fashioned in ${activeFabric.toLowerCase()}, adorned with brilliant ${activePattern.toLowerCase()} craftsmanship. Designed to elevate luxury evening ensembles with radiant elegance.`
          styling = 'Pair as the focal statement piece with deep neckline silks or classic monochromatic silhouettes.'
          break
        default:
          narrative = `Exquisite ${activeColor.toLowerCase()} ${activeGarment.toLowerCase()} crafted from premium ${activeFabric.toLowerCase()} featuring a refined ${activePattern.toLowerCase()} aesthetic with fluid drape. Designed with timeless boutique elegance, ideal for celebratory soirees.`
          styling = 'Pair with fine artisan jewelry, tonal evening accessories, and structured footwear for a polished boutique statement.'
      }

      const generated = `${narrative} Styling: ${styling}`
      setDescription(generated)
      toast.success('Bespoke description generated with AI', {
        description: `${activeGarment} (${activeColor} · ${activeFabric})`,
      })
    } catch {
      toast.error('Failed to generate description')
    } finally {
      setGeneratingDescription(false)
    }
  }

  const runVisionAnalysis = async (targetImage: string, fileNameHint?: string) => {
    if (!targetImage || !targetImage.trim()) {
      toast.error('Please upload an image or enter an image URL first')
      return
    }

    const payload = targetImage.trim()
    const activeFileName = fileNameHint || selectedFileName || undefined
    setAnalyzing(true)
    // F-9: the backend call needs a real organisation id. Without one the analysis falls back to
    // the client-side extractor below; it never addresses a placeholder tenant.
    const targetOrgId = organizationId

    try {
      // 1. High-precision client-side canvas silhouette & authentic pixel matrix extraction
      const visualClientAnalysis = await extractVisualAttributesAndColor(payload, activeFileName).catch(() => null)

      // 2. Call backend Vision AI (Google Gemini / OpenAI / Backend Cloth Engine)
      let backendResult = null
      if (targetOrgId) {
        // Address the analysis at something the provider can actually read, in this order:
        //   1. a stored upload, by row id - the backend resolves the row and inlines its bytes;
        //   2. a `data:` URL, which carries its own bytes (the pre-upload pass);
        //   3. an absolute `http(s)` URL.
        // A relative path is none of those. The provider rejects it with "Unsupported image_url
        // format" and the backend silently degrades to a filename-derived guess, so the operator
        // would see a fabricated colour. Posting one is therefore never an option: the client-side
        // extraction below is the honest answer instead.
        const isDataUrl = payload.startsWith('data:')
        const isAbsoluteHttpUrl = /^https?:\/\//i.test(payload)
        if (uploadedImageId) {
          try {
            backendResult = await analyzeProductImage(targetOrgId, '', activeFileName, undefined, uploadedImageId)
          } catch {
            // Backend offline or unreachable fallback
          }
        } else if (isDataUrl || isAbsoluteHttpUrl) {
          try {
            backendResult = await analyzeProductImage(targetOrgId, payload, activeFileName)
          } catch {
            // Backend offline or unreachable fallback
          }
        }
      }

      // Check whether the backend returned a real live multimodal result or its deterministic
      // fallback. `isFallback` is the honest signal: the fallback never sampled a pixel, it
      // pattern-matched the filename and hint text, so its answer must lose to the client's actual
      // pixel segmentation. A genuine answer is trusted regardless of the confidence it reports —
      // gating on `> 0.85` used to discard a real reading of 0.7 and hand the form to the client
      // heuristic instead, which is the wrong direction for a low-confidence measurement to fail in.
      const isLiveAiResult = Boolean(backendResult && !backendResult.isFallback)

      let resolvedColor: string
      let resolvedHex: string
      let resolvedCategory: CatalogCategory
      let resolvedGarment: string
      let resolvedFabric: string
      let resolvedPattern: string
      let resolvedStyle: string
      let resolvedConfidence: number | null
      let resolvedName: string
      let resolvedDesc: string

      // 3. Precedence. A real multimodal analysis of this image beats the client-side heuristic,
      //    so it is checked first. The client result is used whenever the backend is unreachable,
      //    declined the reference, or fell back to its text-only deterministic guess.
      if (isLiveAiResult && backendResult) {
        // The name and the swatch come from the SAME source, so they cannot disagree. Preferring the
        // model's name and the client's independently-sampled hex produced a chip reading
        // "Fuchsia Magenta" beside an emerald dot whenever the two heuristics disagreed. The model
        // answered here, so its pair wins; the client's pair is used only when the model named
        // nothing. When the chosen source supplies a name but no hex, no swatch is drawn rather than
        // borrowing the other source's.
        const modelNamedColour = Boolean(backendResult.detectedColor)
        resolvedColor = backendResult.detectedColor || visualClientAnalysis?.colorName || ''
        resolvedHex = modelNamedColour
          ? backendResult.colorHex || getColorHex(backendResult.detectedColor, '') || visualClientAnalysis?.hex || ''
          : visualClientAnalysis?.hex || ''
        resolvedCategory = normalizeCategory(backendResult.category)
        resolvedGarment = backendResult.garmentType || `${resolvedColor} ${resolvedCategory}`
        // Gap-filling from the client is attribution-safe only for a value the client MEASURED. Its
        // colour is measured; its fabric/pattern/style are keyword and silhouette inferences, so an
        // absent model value stays absent rather than borrowing an inference.
        resolvedFabric = backendResult.fabric || ''
        resolvedPattern = backendResult.pattern || ''
        resolvedStyle = backendResult.style || ''
        resolvedConfidence = backendResult.confidenceScore ?? null
        resolvedName = backendResult.suggestedItemName || [resolvedColor, resolvedFabric, resolvedGarment].filter(Boolean).join(' ').trim()
        // Only the provider's own copy. A synthesized sentence built from absent attributes would
        // invent a weave and a finish the analysis never observed; the drawer's explicit
        // "generate description" action is where prose is composed, and it is the operator's call.
        resolvedDesc = backendResult.description || ''
      } else if (visualClientAnalysis) {
        // The client measured this image itself. Its colour is a measurement; its fabric, pattern
        // and style are inferences from the filename and the silhouette, which is what the section
        // labels them ("Detected Fabric"), and the extractor now reports no analysis at all rather
        // than a fabricated one when it could read nothing.
        resolvedColor = visualClientAnalysis.colorName || ''
        resolvedHex = visualClientAnalysis.hex || ''
        resolvedCategory = visualClientAnalysis.category
        resolvedGarment = visualClientAnalysis.garmentType
        resolvedFabric = visualClientAnalysis.fabric
        resolvedPattern = visualClientAnalysis.pattern
        resolvedStyle = visualClientAnalysis.style
        resolvedConfidence = visualClientAnalysis.confidenceScore ?? null
        resolvedName = visualClientAnalysis.suggestedItemName
        resolvedDesc = visualClientAnalysis.description
      } else if (backendResult) {
        // The backend's deterministic fallback with no client measurement to arbitrate. Its answer
        // is filename-derived and `isFallback` is set, so nothing here is a reading: only a colour
        // the fallback actually named is carried.
        resolvedColor = backendResult.detectedColor || ''
        // Derive hex from the colour name if known, otherwise leave empty.
        resolvedHex = backendResult.colorHex || getColorHex(backendResult.detectedColor, '') || ''
        resolvedCategory = normalizeCategory(backendResult.category)
        resolvedGarment = backendResult.garmentType || `${resolvedColor} ${resolvedCategory}`
        resolvedFabric = backendResult.fabric || ''
        resolvedPattern = backendResult.pattern || ''
        resolvedStyle = backendResult.style || ''
        resolvedConfidence = backendResult.confidenceScore ?? null
        resolvedName = backendResult.suggestedItemName || [resolvedColor, resolvedFabric, resolvedGarment].filter(Boolean).join(' ').trim()
        resolvedDesc = backendResult.description || ''
      } else {
        throw new Error('Analysis yielded no attributes')
      }

      // Update state hooks to refresh all form inputs immediately
      setColor(resolvedColor)
      // Normalised to lowercase: the swatch below is the HTML colour control, which accepts only
      // `#rrggbb` and silently renders black for an uppercase value. An unmeasured colour stays
      // empty here and the control supplies its own default only for display.
      setColorHex(resolvedHex ? resolvedHex.toLowerCase() : '')
      setCategory(resolvedCategory)
      setGarmentType(resolvedGarment)
      setFabric(resolvedFabric)
      setPattern(resolvedPattern)
      setStyle(resolvedStyle)
      setAiConfidence(resolvedConfidence)
      setName(resolvedName)
      setDescription(resolvedDesc)

      setAnalyzing(false)
      toast.success('Visual attributes extracted via Vision AI', {
        description: `${resolvedGarment} · ${resolvedFabric} · ${resolvedColor}`,
      })
      return
    } catch {
      // Fallback
    }

    setAnalyzing(false)
    toast.error('Could not complete visual analysis. Please enter attributes manually.')
  }

  const handleProcessFile = async (file: File) => {
    if (!isValidImageFile(file)) {
      toast.error('Please upload a valid image file (JPG, PNG, WEBP, HEIC)')
      return
    }

    try {
      setSelectedFileName(file.name)
      setSelectedFileSize(file.size)
      // A new photograph invalidates any remembered upload, so a failed replacement upload can
      // never leave the previous row id pointing at an image the operator no longer sees.
      setUploadedImageId(null)
      const dataUrl = await compressAndResizeImage(file, 1280, 0.85)
      setImageUrl(dataUrl)

      // Run Gemini Vision AI attribute extraction with filename context
      await runVisionAnalysis(dataUrl, file.name)

      // Persist the image only when this dashboard has an organisation id. Without one there is no
      // tenant to store it against, so the compressed data URL simply stays local (F-9).
      if (organizationId) {
        try {
          const uploadRes = await uploadBase64Image(organizationId, dataUrl, file.name)
          if (uploadRes?.url) {
            setImageUrl(uploadRes.url)
          }
          // The save payload needs the relative url above; the re-analysis needs this row id.
          if (uploadRes?.id) {
            setUploadedImageId(uploadRes.id)
          }
        } catch {
          // Retain dataUrl if upload endpoint is unreachable
        }
      }
    } catch {
      toast.error('Failed to process image file')
    }
  }

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (file) {
      handleProcessFile(file)
    }
  }

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
    const file = e.dataTransfer.files?.[0]
    if (file) {
      handleProcessFile(file)
    }
  }

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(true)
  }

  const handleDragLeave = (e: React.DragEvent) => {
    e.preventDefault()
    setIsDragging(false)
  }

  const handleClearImage = () => {
    setImageUrl('')
    setUploadedImageId(null)
    setSelectedFileName(null)
    setSelectedFileSize(null)
    if (fileInputRef.current) {
      fileInputRef.current.value = ''
    }
  }

  const handleSave = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) {
      toast.error('Item name is required')
      return
    }

    setIsSaving(true)
    try {
      const sizes = sizesInput
        .split(',')
        .map((s) => s.trim())
        .filter(Boolean)

      const parsedPrice = parseFloat(price) || 0
      const parsedCost = parseFloat(cost) || 0
      const parsedStock = parseInt(stockQuantity, 10) || 0

      let resolvedImageUrl = imageUrl.trim() || ''
      if (resolvedImageUrl.startsWith('data:image/') && organizationId) {
        try {
          const uploadRes = await uploadBase64Image(organizationId, resolvedImageUrl, selectedFileName || 'garment.jpg')
          if (uploadRes?.url) {
            resolvedImageUrl = uploadRes.url
            setImageUrl(resolvedImageUrl)
          }
          if (uploadRes?.id) {
            setUploadedImageId(uploadRes.id)
          }
        } catch {
          // Fall back to backend auto-offloader
        }
      }

      const newItem: InventoryItemMock = {
        id: editingItem?.id ?? `item-${Date.now()}`,
        name: name.trim(),
        sku: sku.trim(),
        category,
        color: color.trim(),
        // Empty string means "no measurement": `CatalogPanel` turns that into an omitted field, so
        // no default is ever sent as though the analysis had produced it. `InventoryItemMock` types
        // this as a required string, which is why the honest value here is '' and not `undefined`.
        colorHex,
        // No invented fabric or style. The drawer used to substitute `Silk Blend` and
        // `Classic Luxury` here, which put a weave and a house style on the product card that
        // nothing had observed; an empty field is omitted from the payload and the server stores
        // null. Same reasoning for the confidence below: `?? 0.92` made an unanalysed piece claim
        // "92% AI" in the attribute badge.
        fabric: fabric.trim(),
        style: style.trim(),
        pattern: pattern.trim() || undefined,
        sizes: sizes.length > 0 ? sizes : ['Standard'],
        price: parsedPrice,
        cost: parsedCost,
        stockQuantity: parsedStock,
        status: parsedStock === 0 ? 'reserved' : parsedStock <= 2 ? 'low_stock' : 'available',
        imageUrl: resolvedImageUrl,
        confidenceScore: aiConfidence ?? undefined,
        description: description.trim() || undefined,
        createdAt: editingItem?.createdAt ?? new Date().toISOString(),
      }

      await onSave(newItem)
      onClose()
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <Sheet open={open} onOpenChange={(next) => (!next ? onClose() : undefined)}>
      <SheetContent className="flex w-full flex-col gap-0 overflow-hidden p-0 sm:max-w-2xl">
        <SheetHeader className="gap-1.5 border-b px-6 pb-4 pt-6">
          <SheetTitle className="font-serif text-xl">
            {editingItem ? 'Edit piece' : 'Add a piece'}
          </SheetTitle>
          <SheetDescription className="text-[13px] leading-relaxed">
            Add a photograph and Aveline reads the garment; every field below stays editable, and
            nothing is saved until you add it to the catalog.
          </SheetDescription>
        </SheetHeader>

        <form onSubmit={handleSave} className="flex min-h-0 flex-1 flex-col">
          <div className="flex flex-1 flex-col gap-5 overflow-y-auto px-6 py-5">
            {/* Piece Image & Vision AI Analysis Import Area */}
            <FormSection
              step={1}
              title="The photograph"
              description="Aveline reads the garment from this image. Every value it returns stays editable."
            >
              <div className="flex flex-col gap-3">
                <div className="flex items-center justify-between">
                  <span className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
                    Source
                  </span>
                  <ToggleGroup
                    type="single"
                    value={inputMode}
                    onValueChange={(value) => {
                      if (value) setInputMode(value as 'upload' | 'url')
                    }}
                    variant="outline"
                    aria-label="Photograph source"
                    className="rounded-md border border-border bg-muted/30 p-0.5"
                  >
                    <ToggleGroupItem
                      value="upload"
                      className="h-6 gap-1 px-2.5 text-[11px] data-[state=on]:bg-background data-[state=on]:font-semibold"
                    >
                      <Upload className="size-3" aria-hidden />
                      <span>Upload File</span>
                    </ToggleGroupItem>
                    <ToggleGroupItem
                      value="url"
                      className="h-6 gap-1 px-2.5 text-[11px] data-[state=on]:bg-background data-[state=on]:font-semibold"
                    >
                      <LinkIcon className="size-3" aria-hidden />
                      <span>Image URL</span>
                    </ToggleGroupItem>
                  </ToggleGroup>
                </div>

                {/* Upload File Mode */}
                {inputMode === 'upload' ? (
                  <div>
                    <Input
                      type="file"
                      ref={fileInputRef}
                      accept="image/*"
                      onChange={handleFileChange}
                      aria-label="Choose a garment photograph"
                      className="hidden"
                    />

                    {!imageUrl ? (
                      <div
                        onDrop={handleDrop}
                        onDragOver={handleDragOver}
                        onDragLeave={handleDragLeave}
                        onClick={() => fileInputRef.current?.click()}
                        className={`flex flex-col items-center justify-center gap-2 p-6 rounded-xl border-2 border-dashed transition-all cursor-pointer ${isDragging
                            ? 'border-primary bg-primary/10 scale-[0.99]'
                            : 'border-border/80 hover:border-primary/50 hover:bg-muted/30 bg-muted/10'
                          }`}
                      >
                        <div className="flex size-10 items-center justify-center rounded-full bg-primary/10 text-primary">
                          <Upload className="size-5" />
                        </div>
                        <div className="flex flex-col gap-0.5 text-center">
                          <p className="text-xs font-medium text-foreground">
                            Drag & drop garment photo here, or <span className="text-primary font-semibold underline underline-offset-2">Browse Files</span>
                          </p>
                          <p className="text-[11px] text-muted-foreground">
                            Supports JPEG, PNG, WebP, HEIC · Auto-analyzed by Gemini Vision AI
                          </p>
                        </div>
                      </div>
                    ) : (
                      <div className="flex items-center justify-between gap-3 p-3 rounded-xl border border-border bg-card">
                        <div className="flex items-center gap-3 min-w-0">
                          <img
                            src={imageUrl}
                            alt="Garment preview"
                            className="size-14 rounded-lg object-cover border border-border shrink-0 shadow-2xs"
                          />
                          <div className="min-w-0">
                            <p className="text-xs font-medium text-foreground truncate">
                              {selectedFileName || 'Garment Photograph'}
                            </p>
                            <div className="flex items-center gap-1.5 mt-0.5">
                              {selectedFileSize && (
                                <span className="text-[10px] text-muted-foreground font-mono">
                                  {formatFileSize(selectedFileSize)}
                                </span>
                              )}
                              <Badge variant="outline" className="text-[9px] px-1.5 py-0 border-primary/30 text-primary">
                                {analyzing ? 'Analyzing...' : 'Vision AI Ready'}
                              </Badge>
                            </div>
                          </div>
                        </div>

                        <div className="flex items-center gap-1.5 shrink-0">
                          <Button
                            type="button"
                            variant="secondary"
                            size="sm"
                            disabled={analyzing}
                            onClick={() => runVisionAnalysis(imageUrl)}
                            className="gap-1 h-7 text-xs border border-primary/20 bg-primary/10 text-primary hover:bg-primary/20"
                          >
                            {analyzing ? (
                              <Loader2 className="size-3.5 animate-spin" />
                            ) : (
                              <Sparkles className="size-3.5" />
                            )}
                            <span>{analyzing ? 'Analyzing...' : 'Re-analyze'}</span>
                          </Button>
                          <Button
                            type="button"
                            variant="outline"
                            size="sm"
                            onClick={() => fileInputRef.current?.click()}
                            className="h-7 text-xs px-2.5"
                          >
                            <RotateCw className="size-3" />
                            <span>Change</span>
                          </Button>
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            onClick={handleClearImage}
                            aria-label="Remove photograph"
                            className="size-7 p-0 text-muted-foreground hover:text-destructive"
                          >
                            <Trash2 className="size-3.5" />
                          </Button>
                        </div>
                      </div>
                    )}
                  </div>
                ) : (
                  /* URL Mode */
                  <div className="flex flex-col gap-2">
                    <div className="flex gap-2">
                      <Input
                        value={imageUrl}
                        onChange={(e) => {
                          setImageUrl(e.target.value)
                          // Typing a URL abandons any stored upload, so its row id must not survive.
                          setUploadedImageId(null)
                          setSelectedFileName(null)
                          setSelectedFileSize(null)
                        }}
                        placeholder="https://... image URL"
                        className="text-xs"
                      />
                      <Button
                        type="button"
                        variant="secondary"
                        onClick={() => runVisionAnalysis(imageUrl)}
                        disabled={analyzing || !imageUrl.trim()}
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
                    {imageUrl && (
                      <div className="flex items-center gap-2 text-[11px] text-muted-foreground">
                        <ImageIcon className="size-3 text-primary" />
                        <span className="truncate">{imageUrl}</span>
                      </div>
                    )}
                  </div>
                )}
              </div>

            </FormSection>

            <FormSection
              step={2}
              title="The piece"
              description="How it is named and priced. Cost is the atelier's price, not the customer's."
            >

              {/* Core Info Grid */}
              <div className="grid gap-3.5 sm:grid-cols-2">
                <Field label="Item name" htmlFor="piece-name">
                  <Input
                    id="piece-name"
                    value={name}
                    onChange={(e) => setName(e.target.value)}
                    placeholder="e.g. Royal Emerald Silk Saree"
                    required
                  />
                </Field>
                <Field label="SKU code" htmlFor="piece-sku" hint="Appears on the floor tag.">
                  <Input
                    id="piece-sku"
                    value={sku}
                    onChange={(e) => setSku(e.target.value)}
                    placeholder="AVL-SAR-001"
                    className="font-mono"
                    required
                  />
                </Field>
              </div>

              <div className="grid gap-3.5 sm:grid-cols-3">
                <Field label="Category">
                  <Select value={category} onValueChange={setCategory}>
                    <SelectTrigger aria-label="Category" className="w-full">
                      <SelectValue placeholder="Choose a category" />
                    </SelectTrigger>
                    <SelectContent>
                      {CATEGORIES.map((cat) => (
                        <SelectItem key={cat} value={cat}>
                          {cat}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </Field>

                <Field label="Retail price (LKR)">
                  <Input
                    type="number"
                    value={price}
                    onChange={(e) => setPrice(e.target.value)}
                    placeholder="1450"
                    required
                  />
                </Field>

                <Field label="Atelier cost (LKR)">
                  <Input
                    type="number"
                    value={cost}
                    onChange={(e) => setCost(e.target.value)}
                    placeholder="650"
                  />
                </Field>
              </div>
            </FormSection>


            <FormSection
              step={3}
              title="What Aveline read"
              description="Filled from the photograph and editable. A value below was returned by the analysis, not guessed."
            >
              <div className="flex flex-col gap-3 rounded-xl border border-primary/20 bg-primary/5 p-3.5">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-1.5 text-xs font-semibold text-primary">
                    <Sparkles className="size-3.5" />
                    <span>Visual AI Extracted Attributes</span>
                  </div>
                  <div className="flex items-center gap-2">
                    {garmentType && (
                      <Badge variant="secondary" className="text-[10px] bg-primary/10 text-primary border border-primary/30">
                        {garmentType}
                      </Badge>
                    )}
                    {aiConfidence && (
                      <Badge variant="outline" className="text-[10px] border-primary/30 text-primary">
                        {Math.round(aiConfidence * 100)}% Confidence
                      </Badge>
                    )}
                  </div>
                </div>

                <div className="grid grid-cols-4 gap-3">
                  <div className="flex flex-col gap-1">
                    <Label className="text-[11px] text-muted-foreground">Cloth / Garment</Label>
                    <Input
                      value={garmentType || category}
                      onChange={(e) => setGarmentType(e.target.value)}
                      placeholder="e.g. Silk Saree"
                      className="h-7 text-xs"
                    />
                  </div>

                  <div className="flex flex-col gap-1">
                    <Label className="text-[11px] text-muted-foreground">Dominant Color</Label>
                    <div className="flex items-center gap-1.5">
                      <Input
                        type="color"
                        aria-label="Dominant colour"
                        value={colorHex || DEFAULT_COLOR_HEX}
                        onChange={(e) => setColorHex(e.target.value)}
                        className="size-6 shrink-0 cursor-pointer rounded border border-border p-0"
                      />
                      <Input
                        value={color}
                        aria-label="Colour name"
                        onChange={(e) => {
                          const val = e.target.value
                          setColor(val)
                          const hex = getColorHex(val, '')
                          if (hex) setColorHex(hex)
                        }}
                        placeholder="Enter colour name"
                        className="h-7 text-xs min-w-0"
                      />
                    </div>
                  </div>

                  <div className="flex flex-col gap-1">
                    <Label className="text-[11px] text-muted-foreground">Detected Fabric</Label>
                    <Input
                      value={fabric}
                      onChange={(e) => setFabric(e.target.value)}
                      aria-label="Detected fabric"
                      placeholder="Pure Mulberry Silk"
                      className="h-7 text-xs"
                    />
                  </div>

                  <div className="flex flex-col gap-1">
                    <Label className="text-[11px] text-muted-foreground">Style / Pattern</Label>
                    <Input
                      value={pattern || style}
                      onChange={(e) => {
                        setPattern(e.target.value)
                        setStyle(e.target.value)
                      }}
                      aria-label="Style and pattern"
                      placeholder="Zari Brocade"
                      className="h-7 text-xs"
                    />
                  </div>
                </div>
              </div>

            </FormSection>

            <FormSection
              step={4}
              title="Stock and sizes"
              description="A piece at zero shows as reserved; two or fewer shows as low stock."
            >
              <div className="grid gap-3.5 sm:grid-cols-2">
                <Field label="Initial stock quantity">
                  <Input
                    type="number"
                    value={stockQuantity}
                    onChange={(e) => setStockQuantity(e.target.value)}
                    placeholder="4"
                    required
                  />
                </Field>
                <Field label="Available sizes" hint="Comma-separated, e.g. 36, 38, Free Size.">
                  <Input
                    value={sizesInput}
                    onChange={(e) => setSizesInput(e.target.value)}
                    placeholder="36, 38, 40, Free Size"
                  />
                </Field>
              </div>
            </FormSection>

            <FormSection
              step={5}
              title="Description"
              description="What the piece is and how to style it. Aveline can draft it from the photograph."
            >
              <div className="flex flex-col gap-1.5">
                <div className="flex items-center justify-between">
                  <Label className="text-xs font-medium">Description / Styling Notes</Label>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={generatingDescription}
                    onClick={handleGenerateDescriptionWithAi}
                    className="h-6 px-2 text-[11px] gap-1 text-primary hover:text-primary hover:bg-primary/10 transition-colors"
                    title="Generate bespoke haute-couture description and styling recommendations using AI"
                  >
                    {generatingDescription ? (
                      <Loader2 className="size-3 animate-spin" />
                    ) : (
                      <Sparkles className="size-3 text-primary" />
                    )}
                    <span>{generatingDescription ? 'Generating...' : 'Generate with AI'}</span>
                  </Button>
                </div>
                <Textarea
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  placeholder="Detailed description, weave information, styling recommendations..."
                  rows={4}
                  className="text-[13px] leading-relaxed"
                />
              </div>
            </FormSection>

            <FormSection
              step={6}
              title="Floor tag"
              description="A scannable tag for the garment rail, fitting room or POS."
            >
              <FloorTagStudio
                organizationId={organizationId}
                organizationSlug={organizationSlug}
                itemId={effectiveItemId}
                sku={sku}
                name={name}
                price={price}
                category={category}
                color={color}
                fabric={fabric}
              />
            </FormSection>
          </div>

          {/* The footer stays put while the form scrolls, so the primary action is always reachable. */}
          <div className="flex shrink-0 items-center justify-between gap-2.5 border-t border-border bg-card px-6 py-4">
            <div>
              {editingItem && onDelete && (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    onDelete(editingItem)
                  }}
                  className="gap-1.5 text-xs text-destructive hover:bg-destructive/10 hover:text-destructive cursor-pointer"
                >
                  <Trash2 className="size-3.5" />
                  <span>Delete Piece</span>
                </Button>
              )}
            </div>

            <div className="flex items-center gap-2.5">
              <Button type="button" variant="outline" size="sm" onClick={onClose} className="cursor-pointer">
                Cancel
              </Button>
              <Button type="submit" size="sm" className="gap-1.5 cursor-pointer" disabled={isSaving}>
                {isSaving ? <Loader2 className="size-4 animate-spin" /> : <Check className="size-4" />}
                <span>{isSaving ? 'Saving...' : editingItem ? 'Save Changes' : 'Add to Catalog'}</span>
              </Button>
            </div>
          </div>
        </form>
      </SheetContent>
    </Sheet>
  )
}
