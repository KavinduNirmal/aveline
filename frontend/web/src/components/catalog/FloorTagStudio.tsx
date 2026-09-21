import { useState } from 'react'
import { CheckCheck, Copy, Download, Printer, QrCode } from 'lucide-react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { QrCodeSvg } from '@/components/ui/QrCodeSvg'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { cn } from '@/lib/utils'
import { formatMoney } from '@/lib/format-money'
import { generateQrCode } from '@/lib/catalog-api'

/** How the tag encodes the piece. */
type QrFormat = 'json' | 'url' | 'sku'

const FORMATS: { id: QrFormat; label: string; hint: string }[] = [
  { id: 'json', label: 'Structured JSON', hint: 'Everything a scanner needs, including the shop.' },
  { id: 'url', label: 'Boutique URL', hint: 'Opens the piece in this boutique.' },
  { id: 'sku', label: 'Raw SKU', hint: 'The SKU alone, for an existing POS.' },
]

interface FloorTagStudioProps {
  /** The organization the QR is generated for. A missing id means no download is possible. */
  organizationId?: string | null
  /** The piece's id, or the prospective id while it is still unsaved. */
  itemId: string
  sku: string
  name: string
  price: string
  category: string
  color: string
  fabric: string
}

/**
 * The floor-tag studio: the tag itself, how it encodes, and the exports.
 *
 * Extracted from the piece form because it is a self-contained tool with its own state (encoding,
 * copy, two downloads and print), and because at the form's width a side-by-side tag and control
 * column left both cramped. It now stacks: the tag on top at a size worth looking at, the controls
 * beneath it, each control on its own row.
 */
export function FloorTagStudio({
  organizationId,
  itemId,
  sku,
  name,
  price,
  category,
  color,
  fabric,
}: FloorTagStudioProps) {
  const [format, setFormat] = useState<QrFormat>('json')
  const [copied, setCopied] = useState(false)
  const [downloadingPng, setDownloadingPng] = useState(false)
  const [downloadingSvg, setDownloadingSvg] = useState(false)

  const payload =
    format === 'sku'
      ? sku || 'AVL-000'
      : format === 'url'
        ? `${typeof window !== 'undefined' ? window.location.origin : 'https://aveline.app'}/catalog/items/${itemId}`
        : JSON.stringify({
            type: 'aveline_inventory_item',
            orgId: organizationId,
            itemId,
            sku: sku || 'AVL-000',
            url: `/catalog/items/${itemId}`,
            v: 1,
          })

  const activeFormat = FORMATS.find((f) => f.id === format) ?? FORMATS[0]

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(payload)
      setCopied(true)
      toast.success('QR payload copied.', { description: activeFormat.label })
      setTimeout(() => setCopied(false), 2000)
    } catch {
      toast.error('Could not copy the payload.')
    }
  }

  const download = async (kind: 'png' | 'svg') => {
    if (!organizationId) return
    const setBusy = kind === 'png' ? setDownloadingPng : setDownloadingSvg
    setBusy(true)
    try {
      const result = await generateQrCode(organizationId, {
        payload,
        format: kind === 'png' ? 'json' : 'svg',
        size: kind === 'png' ? 600 : 300,
        eccLevel: 'M',
        quietZone: 2,
      })

      if (kind === 'png') {
        if (!result?.dataUrl) throw new Error('No image payload returned')
        const link = document.createElement('a')
        link.href = result.dataUrl
        link.download = `${sku || 'piece'}_floor_tag.png`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
        toast.success('QR code downloaded (PNG 600px).')
      } else {
        if (!result?.svg) throw new Error('No SVG payload returned')
        const blob = new Blob([result.svg], { type: 'image/svg+xml;charset=utf-8' })
        const url = URL.createObjectURL(blob)
        const link = document.createElement('a')
        link.href = url
        link.download = `${sku || 'piece'}_floor_tag.svg`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
        URL.revokeObjectURL(url)
        toast.success('QR code downloaded (SVG).')
      }
    } catch {
      toast.error(kind === 'png' ? 'Failed to download the PNG.' : 'Failed to download the SVG.')
    } finally {
      setBusy(false)
    }
  }

  const handlePrint = () => {
    const printWindow = window.open('', '_blank', 'width=420,height=640')
    if (!printWindow) {
      toast.error('Pop-ups are blocked. Allow pop-ups to print tags.')
      return
    }

    const escape = (value: string) =>
      value.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')

    printWindow.document.write(`<!doctype html><html><head><title>Floor tag ${escape(
      sku || 'AVL-000',
    )}</title>
<style>
  @page { margin: 12mm; }
  body { font-family: ui-sans-serif, system-ui, sans-serif; display: flex; align-items: center;
         justify-content: center; min-height: 100vh; background: whitesmoke; padding: 20px; }
  .tag { width: 320px; background: white; border: 2px solid black; border-radius: 16px;
         padding: 24px 20px; text-align: center; }
  .brand { font-size: 13px; font-weight: 800; letter-spacing: 3px; text-transform: uppercase; }
  .category { display: block; font-size: 10px; font-weight: 600; letter-spacing: 1px;
              text-transform: uppercase; color: gray; margin: 4px 0 14px; padding-bottom: 12px;
              border-bottom: 1px dashed lightgray; }
  .qr { display: flex; justify-content: center; margin: 10px 0 16px; }
  .title { font-size: 15px; font-weight: 700; line-height: 1.3; margin-bottom: 6px; }
  .sku { font-family: ui-monospace, monospace; font-size: 11px; color: dimgray; }
  .attrs { font-size: 10px; color: gray; margin-top: 8px; }
  .price { margin-top: 14px; padding-top: 12px; border-top: 1px dashed lightgray; font-size: 15px;
           font-weight: 700; }
  .price span { display: block; font-size: 9px; font-weight: 500; letter-spacing: 1px;
                text-transform: uppercase; color: darkgray; margin-bottom: 2px; }
</style></head><body>
  <div class="tag">
    <div class="brand">Aveline Atelier</div>
    <span class="category">${escape(category || 'Floor piece')}</span>
    <div class="qr" id="qr"></div>
    <div class="title">${escape(name || 'Untitled garment')}</div>
    <div class="sku">${escape(sku || 'AVL-000')}</div>
    <div class="attrs">${escape([color, fabric].filter(Boolean).join(' · '))}</div>
    <div class="price"><span>Retail</span>${escape(
      formatMoney(price ? Number(price) : null),
    )}</div>
  </div>
  <script>setTimeout(function(){ window.print(); }, 250);</script>
</body></html>`)
    printWindow.document.close()
  }

  return (
    <div className="flex flex-col gap-4">
      {/* The tag, at a size the operator can actually check before printing. */}
      <div className="flex justify-center">
        <div className="relative w-full max-w-[16rem] overflow-hidden rounded-2xl border-2 border-border/80 bg-card p-5 text-center shadow-xs">
          <div className="absolute inset-x-0 top-0 h-1 bg-gradient-to-r from-primary/80 via-primary to-primary/80" />
          <p className="mt-1 text-[10px] font-bold uppercase tracking-[0.2em] text-foreground/80">
            Aveline Atelier
          </p>
          <p className="mb-3 text-[9px] uppercase tracking-wider text-muted-foreground">
            {category || 'Floor piece'}
          </p>

          <div className="my-2 flex justify-center rounded-xl border border-border/40 bg-white p-2.5">
            <QrCodeSvg value={payload} size={150} className="rounded-md" />
          </div>

          <p className="truncate px-1 text-xs font-semibold">{name || 'Untitled garment'}</p>
          <p className="font-mono text-[11px] text-muted-foreground">{sku || 'AVL-000'}</p>

          {color || fabric ? (
            <p className="mt-2 truncate text-[10px] text-muted-foreground">
              {[color, fabric].filter(Boolean).join(' · ')}
            </p>
          ) : null}

          <div className="mt-3 border-t border-dashed pt-2.5">
            <p className="text-[9px] uppercase tracking-wider text-muted-foreground">Retail</p>
            <p className="text-sm font-bold">{formatMoney(price ? Number(price) : null)}</p>
          </div>
        </div>
      </div>

      {/* Encoding, on its own row per option, so the choice is legible rather than a tight strip. */}
      <div className="flex flex-col gap-2">
        <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          Encodes
        </p>
        <ToggleGroup
          type="single"
          value={format}
          onValueChange={(value) => {
            if (value) setFormat(value as QrFormat)
          }}
          variant="outline"
          className="grid w-full grid-cols-3 gap-1.5"
        >
          {FORMATS.map((option) => (
            <ToggleGroupItem
              key={option.id}
              value={option.id}
              className="h-9 text-[11px] font-medium"
            >
              {option.label}
            </ToggleGroupItem>
          ))}
        </ToggleGroup>
        <p className="text-xs text-muted-foreground">{activeFormat.hint}</p>
      </div>

      <div className="flex flex-col gap-2">
        <div className="flex items-center justify-between">
          <p className="text-[11px] font-semibold uppercase tracking-[0.14em] text-muted-foreground">
            Payload
          </p>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="h-6 gap-1 px-1.5 text-[11px] text-primary hover:bg-primary/10 hover:text-primary"
            onClick={() => void handleCopy()}
          >
            {copied ? (
              <CheckCheck className="size-3 text-success" aria-hidden />
            ) : (
              <Copy className="size-3" aria-hidden />
            )}
            {copied ? 'Copied' : 'Copy'}
          </Button>
        </div>
        <code
          className={cn(
            'block max-h-24 overflow-y-auto rounded-lg border border-border/60 bg-muted/40 p-2.5',
            'break-all font-mono text-[11px] leading-relaxed text-muted-foreground',
          )}
        >
          {payload}
        </code>
      </div>

      <div className="grid grid-cols-3 gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={downloadingPng || !organizationId}
          onClick={() => void download('png')}
          className="gap-1.5"
        >
          <Download className="size-3.5" aria-hidden />
          {downloadingPng ? 'Preparing…' : 'PNG 600px'}
        </Button>
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={downloadingSvg || !organizationId}
          onClick={() => void download('svg')}
          className="gap-1.5"
        >
          <Download className="size-3.5" aria-hidden />
          {downloadingSvg ? 'Preparing…' : 'Vector SVG'}
        </Button>
        <Button type="button" variant="secondary" size="sm" onClick={handlePrint} className="gap-1.5">
          <Printer className="size-3.5" aria-hidden />
          Print tag
        </Button>
      </div>

      {!organizationId ? (
        // F-9: a missing organisation is an absent field. Nothing is invented and no download is
        // attempted against a tenant that does not exist.
        <p className="text-xs text-muted-foreground">
          Downloads need the boutique id; printing the tag still works.
        </p>
      ) : null}

      <Badge variant="outline" className="self-start border-primary/30 text-[10px] text-primary">
        <QrCode className="size-3" aria-hidden /> Scan and print ready
      </Badge>
    </div>
  )
}
