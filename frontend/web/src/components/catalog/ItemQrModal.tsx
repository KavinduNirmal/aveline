import { useState, useMemo } from 'react'
import {
  X,
  QrCode,
  Download,
  Printer,
  Copy,
  CheckCheck,
  Loader2,
  ExternalLink,
} from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Label } from '@/components/ui/label'
import { QrCodeSvg } from '@/components/ui/QrCodeSvg'
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'
import { generateQrCode } from '@/lib/catalog-api'
import { formatMoney } from '@/lib/format-money'
import type { InventoryItemMock } from './mockData'

interface ItemQrModalProps {
  item: InventoryItemMock | null
  open: boolean
  organizationId?: string
  onClose: () => void
}

export function ItemQrModal({
  item,
  open,
  organizationId,
  onClose,
}: ItemQrModalProps) {
  const [copiedPayload, setCopiedPayload] = useState(false)
  const [isDownloadingPng, setIsDownloadingPng] = useState(false)
  const [isDownloadingSvg, setIsDownloadingSvg] = useState(false)
  const [qrFormatType, setQrFormatType] = useState<'json' | 'url' | 'sku'>('json')

  // F-9: never invent a tenant id — the QR payload carries whatever the real organisation is, and
  // an absent one leaves the field empty rather than naming another shop.
  const effectiveOrgId = organizationId
  const effectiveItemId = item?.id || 'prospective-piece'
  const effectiveSku = item?.sku || 'AVL-000'

  const activeQrPayload = useMemo(() => {
    if (!item) return ''
    if (qrFormatType === 'sku') {
      return item.sku || 'AVL-000'
    }
    if (qrFormatType === 'url') {
      const origin = typeof window !== 'undefined' ? window.location.origin : 'https://aveline.app'
      return `${origin}/catalog/items/${effectiveItemId}`
    }
    return JSON.stringify({
      type: 'aveline_inventory_item',
      orgId: effectiveOrgId,
      itemId: effectiveItemId,
      sku: effectiveSku,
      url: `/catalog/items/${effectiveItemId}`,
      v: 1,
    })
  }, [item, qrFormatType, effectiveOrgId, effectiveItemId, effectiveSku])

  if (!open || !item) return null

  const handleCopyPayload = () => {
    if (!activeQrPayload) return
    navigator.clipboard.writeText(activeQrPayload)
    setCopiedPayload(true)
    toast.success('QR payload copied to clipboard', {
      description: `${effectiveSku} · Format: ${qrFormatType.toUpperCase()}`,
    })
    setTimeout(() => setCopiedPayload(false), 2000)
  }

  const handleDownloadPng = async () => {
    if (!activeQrPayload || !effectiveOrgId) return
    setIsDownloadingPng(true)
    try {
      const res = await generateQrCode(effectiveOrgId, {
        payload: activeQrPayload,
        format: 'json',
        size: 600,
        eccLevel: 'M',
        quietZone: 2,
      })

      if (res?.dataUrl) {
        const link = document.createElement('a')
        link.href = res.dataUrl
        link.download = `${effectiveSku}_floor_tag.png`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
        toast.success('High-resolution QR code downloaded (PNG 600px)')
      } else {
        throw new Error('No image payload returned')
      }
    } catch {
      toast.error('Failed to download PNG QR code')
    } finally {
      setIsDownloadingPng(false)
    }
  }

  const handleDownloadSvg = async () => {
    if (!activeQrPayload || !effectiveOrgId) return
    setIsDownloadingSvg(true)
    try {
      const res = await generateQrCode(effectiveOrgId, {
        payload: activeQrPayload,
        format: 'svg',
        size: 300,
        eccLevel: 'M',
        quietZone: 2,
      })

      const svgContent = res?.svg
      if (svgContent) {
        const blob = new Blob([svgContent], { type: 'image/svg+xml;charset=utf-8' })
        const url = URL.createObjectURL(blob)
        const link = document.createElement('a')
        link.href = url
        link.download = `${effectiveSku}_floor_tag.svg`
        document.body.appendChild(link)
        link.click()
        document.body.removeChild(link)
        URL.revokeObjectURL(url)
        toast.success('Vector QR code downloaded (SVG)')
      } else {
        throw new Error('No SVG payload returned')
      }
    } catch {
      toast.error('Failed to download SVG QR code')
    } finally {
      setIsDownloadingSvg(false)
    }
  }

  const handlePrintTag = () => {
    const activeName = item.name || 'Boutique Collection Piece'
    const activePrice = formatMoney(item.price)
    const activeCategory = item.category || 'Haute Couture'
    const activeFabric = item.fabric ? `Fabric: ${item.fabric}` : ''
    const activeColor = item.color ? `Color: ${item.color}` : ''

    const printWindow = window.open('', '_blank', 'width=420,height=620')
    if (!printWindow) {
      toast.error('Pop-up blocked. Please allow pop-ups to print garment tags.')
      return
    }

    const svgElement = document.getElementById('item-qr-modal-preview-svg')
    const svgHtml = svgElement ? svgElement.outerHTML : ''

    printWindow.document.write(`
      <!DOCTYPE html>
      <html>
        <head>
          <title>Garment Floor Tag - ${effectiveSku}</title>
          <style>
            * { box-sizing: border-box; margin: 0; padding: 0; }
            body {
              font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
              display: flex;
              align-items: center;
              justify-content: center;
              min-height: 100vh;
              background: whitesmoke;
              padding: 20px;
            }
            .tag {
              width: 320px;
              background: white;
              border: 2px solid black;
              border-radius: 16px;
              padding: 24px 20px;
              text-align: center;
              box-shadow: 0 4px 20px rgba(0,0,0,0.06);
            }
            .brand {
              font-size: 13px;
              font-weight: 800;
              letter-spacing: 3px;
              text-transform: uppercase;
              color: black;
            }
            .category-badge {
              display: inline-block;
              font-size: 10px;
              font-weight: 600;
              letter-spacing: 1px;
              text-transform: uppercase;
              color: gray;
              margin-top: 4px;
              margin-bottom: 14px;
              padding-bottom: 12px;
              border-bottom: 1px dashed gainsboro;
              width: 100%;
            }
            .qr-container {
              display: flex;
              justify-content: center;
              margin: 10px 0 16px;
            }
            .item-title {
              font-size: 15px;
              font-weight: 700;
              color: black;
              line-height: 1.3;
              margin-bottom: 6px;
            }
            .sku-pill {
              display: inline-block;
              font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
              font-size: 12px;
              font-weight: 600;
              background: whitesmoke;
              color: dimgray;
              padding: 2px 10px;
              border-radius: 6px;
              margin-bottom: 12px;
            }
            .meta {
              font-size: 11px;
              color: gray;
              margin-bottom: 14px;
              line-height: 1.4;
            }
            .price-box {
              border-top: 1px dashed gainsboro;
              padding-top: 14px;
            }
            .price-label {
              font-size: 9px;
              text-transform: uppercase;
              letter-spacing: 1.5px;
              color: darkgray;
            }
            .price-val {
              font-size: 22px;
              font-weight: 800;
              color: black;
              margin-top: 2px;
            }
            .footer-note {
              font-size: 9px;
              color: darkgray;
              margin-top: 14px;
              letter-spacing: 0.5px;
            }
            @media print {
              body { background: white; padding: 0; }
              .tag { border: 2px solid black; box-shadow: none; }
            }
          </style>
        </head>
        <body>
          <div class="tag">
            <div class="brand">Aveline Boutique</div>
            <div class="category-badge">${activeCategory} · Floor Collection</div>
            <div class="qr-container">
              ${svgHtml}
            </div>
            <div class="item-title">${activeName}</div>
            <div class="sku-pill">${effectiveSku}</div>
            ${activeColor || activeFabric ? `<div class="meta">${[activeColor, activeFabric].filter(Boolean).join(' · ')}</div>` : ''}
            <div class="price-box">
              <div class="price-label">Retail Price</div>
              <div class="price-val">${activePrice}</div>
            </div>
            <div class="footer-note">Scan with Aveline floor app for live stock & VIP styling</div>
          </div>
          <script>
            window.onload = function() {
              window.print();
              setTimeout(function() { window.close(); }, 500);
            };
          </script>
        </body>
      </html>
    `)
    printWindow.document.close()
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4 animate-in fade-in duration-200">
      <Card className="w-full max-w-xl max-h-[90vh] flex flex-col overflow-hidden border-border bg-card shadow-2xl">
        {/* Modal Header */}
        <div className="flex items-center justify-between px-6 py-4 border-b border-border bg-muted/20 shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="flex size-8 items-center justify-center rounded-lg bg-primary/10 text-primary border border-primary/20">
              <QrCode className="size-4" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <h3 className="font-serif text-base font-semibold text-foreground">
                  Garment Floor Tag & QR Code
                </h3>
                <Badge variant="outline" className="text-[10px] font-mono border-primary/30 text-primary">
                  {effectiveSku}
                </Badge>
              </div>
              <p className="text-xs text-muted-foreground">
                Physical tag barcode for boutique labeling, fitting rooms, and POS scanning
              </p>
            </div>
          </div>

          <Button
            size="sm"
            variant="ghost"
            onClick={onClose}
            className="size-8 p-0 rounded-full hover:bg-muted text-muted-foreground hover:text-foreground"
          >
            <X className="size-4" />
          </Button>
        </div>

        {/* Modal Body */}
        <div className="flex flex-col p-6 overflow-y-auto gap-6">
          <div className="grid grid-cols-1 md:grid-cols-12 gap-6 items-center">
            {/* Left: Luxury Atelier Floor Tag Preview Card */}
            <div className="md:col-span-6 flex justify-center">
              <div className="w-64 bg-card border-2 border-border/90 rounded-2xl p-4 text-center shadow-md flex flex-col items-center relative overflow-hidden">
                <div className="absolute top-0 inset-x-0 h-1 bg-gradient-to-r from-primary/80 via-primary to-primary/80" />
                <span className="text-[10px] font-bold tracking-[0.2em] uppercase text-foreground/80 mt-1">
                  Aveline Atelier
                </span>
                <span className="text-[9px] text-muted-foreground tracking-wider uppercase mb-2">
                  {item.category} · Floor Piece
                </span>

                {/* Vector QR Code */}
                <div className="p-2.5 bg-white rounded-xl shadow-inner my-1.5 border border-border/40">
                  <div id="item-qr-modal-preview-svg">
                    <QrCodeSvg value={activeQrPayload} size={150} className="rounded-md" />
                  </div>
                </div>

                <p className="text-xs font-semibold text-foreground mt-2 truncate w-full px-1">
                  {item.name}
                </p>
                <span className="text-[11px] font-mono font-medium text-muted-foreground bg-muted/60 px-2 py-0.5 rounded-md mt-1">
                  {effectiveSku}
                </span>

                {/* Color and Fabric metadata */}
                {(item.color || item.fabric) && (
                  <p className="text-[10px] text-muted-foreground mt-1 truncate max-w-[90%]">
                    {[item.color, item.fabric].filter(Boolean).join(' · ')}
                  </p>
                )}

                <div className="w-full mt-3 pt-2.5 border-t border-dashed border-border/80 flex items-center justify-between text-xs px-1">
                  <span className="text-[10px] text-muted-foreground uppercase tracking-wider">Retail</span>
                  <span className="font-bold text-sm text-foreground">
                    {formatMoney(item.price)}
                  </span>
                </div>
              </div>
            </div>

            {/* Right: Controls, Format Selection & Export Actions */}
            <div className="flex flex-col md:col-span-6 gap-4">
              {/* Format Selector */}
              <div className="flex flex-col gap-1.5">
                <Label className="text-xs text-muted-foreground font-medium">QR Payload Encoding</Label>
                <ToggleGroup
                  type="single"
                  value={qrFormatType}
                  onValueChange={(value) => {
                    if (value) setQrFormatType(value as 'json' | 'url' | 'sku')
                  }}
                  variant="outline"
                  className="grid grid-cols-3 gap-1 rounded-lg border border-border/60 bg-muted/30 p-1 text-xs"
                >
                  <ToggleGroupItem value="json" className="text-[11px] font-medium">
                    JSON
                  </ToggleGroupItem>
                  <ToggleGroupItem value="url" className="text-[11px] font-medium">
                    URL
                  </ToggleGroupItem>
                  <ToggleGroupItem value="sku" className="text-[11px] font-medium">
                    SKU
                  </ToggleGroupItem>
                </ToggleGroup>
              </div>

              {/* Encoded Data String */}
              <div className="flex flex-col gap-1">
                <div className="flex items-center justify-between">
                  <span className="text-[10px] text-muted-foreground font-medium">Active Encoded Data</span>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={handleCopyPayload}
                    className="h-5 px-1.5 text-[10px] gap-1 text-primary hover:text-primary hover:bg-primary/10 cursor-pointer"
                  >
                    {copiedPayload ? <CheckCheck className="size-3 text-success" /> : <Copy className="size-3" />}
                    <span>{copiedPayload ? 'Copied' : 'Copy'}</span>
                  </Button>
                </div>
                <div className="p-2.5 rounded-lg bg-muted/40 border border-border/50 font-mono text-[10px] text-muted-foreground break-all max-h-20 overflow-y-auto leading-relaxed">
                  {activeQrPayload}
                </div>
              </div>

              {/* Direct Link Info if available */}
              {item.id && (
                <div className="flex items-center gap-1.5 text-[11px] text-muted-foreground">
                  <ExternalLink className="size-3 text-primary shrink-0" />
                  <span className="truncate font-mono text-[10px]">
                    /catalog/items/{item.id}
                  </span>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Modal Footer Actions */}
        <div className="flex flex-wrap items-center justify-between gap-2.5 px-6 py-3.5 border-t border-border bg-card shrink-0">
          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isDownloadingPng}
              onClick={handleDownloadPng}
              className="h-8 text-xs gap-1.5 border-border hover:border-primary/50 hover:bg-primary/5 hover:text-primary cursor-pointer"
            >
              {isDownloadingPng ? <Loader2 className="size-3.5 animate-spin" /> : <Download className="size-3.5" />}
              <span>PNG (600px)</span>
            </Button>

            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={isDownloadingSvg}
              onClick={handleDownloadSvg}
              className="h-8 text-xs gap-1.5 border-border hover:border-primary/50 hover:bg-primary/5 hover:text-primary cursor-pointer"
            >
              {isDownloadingSvg ? <Loader2 className="size-3.5 animate-spin" /> : <Download className="size-3.5" />}
              <span>Vector SVG</span>
            </Button>
          </div>

          <div className="flex items-center gap-2">
            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={handlePrintTag}
              className="h-8 text-xs gap-1.5 bg-primary/10 text-primary hover:bg-primary/20 border border-primary/30 font-medium cursor-pointer"
            >
              <Printer className="size-3.5" />
              <span>Print Floor Tag</span>
            </Button>
            <Button type="button" variant="outline" size="sm" onClick={onClose} className="h-8 text-xs cursor-pointer">
              Close
            </Button>
          </div>
        </div>
      </Card>
    </div>
  )
}
