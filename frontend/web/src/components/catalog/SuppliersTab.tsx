import { useState } from 'react'
import {
  Building2,
  MapPin,
  Mail,
  Phone,
  Clock,
  Coins,
  Package,
  Layers,
  Globe,
  ExternalLink,
  Plus,
  X,
} from 'lucide-react'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { formatMoney } from '@/lib/format-money'
import { AddSupplierModal } from './AddSupplierModal'
import type { SupplierMock } from './mockData'

interface SuppliersTabProps {
  suppliers: SupplierMock[]
  organizationId?: string
  onAddSupplier?: (supplier: SupplierMock) => void
}

export function SuppliersTab({ suppliers, organizationId, onAddSupplier }: SuppliersTabProps) {
  const [activeCatalogSupplier, setActiveCatalogSupplier] = useState<SupplierMock | null>(null)
  const [addModalOpen, setAddModalOpen] = useState(false)

  const handleSaveSupplier = (newSupplier: SupplierMock) => {
    if (onAddSupplier) {
      onAddSupplier(newSupplier)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Tab Header */}
      <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
        <div>
          <h3 className="font-serif text-base font-semibold text-foreground">
            Partner Ateliers & Heritage Fabric Mills
          </h3>
          <p className="text-xs text-muted-foreground">
            Direct supplier integrations for handloom silks, bespoke zari embroidery, and fabric sourcing
          </p>
        </div>
        <Button
          size="sm"
          className="gap-1.5 self-start sm:self-auto text-xs font-medium"
          onClick={() => setAddModalOpen(true)}
        >
          <Plus className="size-3.5" />
          <span>Add Partner Atelier</span>
        </Button>
      </div>

      {/* Suppliers Grid */}
      {suppliers.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center border-dashed border-border/80 bg-card/50">
          <Building2 className="size-12 text-muted-foreground/40 mb-3" />
          <h4 className="font-serif text-base font-medium">No Partner Ateliers Found</h4>
          <p className="text-xs text-muted-foreground max-w-sm mt-1 mb-5">
            Connect your heritage suppliers and fabric mills to track sourcing lead times and minimum orders.
          </p>
          <Button
            size="sm"
            variant="outline"
            className="gap-1.5 text-xs"
            onClick={() => setAddModalOpen(true)}
          >
            <Plus className="size-3.5" />
            <span>Add Your First Atelier</span>
          </Button>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
          {suppliers.map((supplier) => (
            <Card
              key={supplier.id}
              className="flex flex-col justify-between overflow-hidden border-border/80 bg-card p-5 shadow-2xs transition-all hover:border-border hover:shadow-md"
            >
              <div>
                {/* Header & Status */}
                <div className="flex items-start justify-between gap-2 mb-3">
                  <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
                    <Building2 className="size-5" />
                  </div>
                  <Badge
                    variant="outline"
                    className={
                      supplier.isActive
                        ? 'bg-success/10 text-success border-success/20 text-[10px]'
                        : 'text-muted-foreground text-[10px]'
                    }
                  >
                    {supplier.isActive ? 'Active Partner' : 'Inactive'}
                  </Badge>
                </div>

                {/* Title & Location */}
                <h4 className="font-serif text-base font-semibold text-foreground">
                  {supplier.name}
                </h4>
                <div className="flex items-center gap-1.5 text-xs text-muted-foreground mt-0.5 mb-2">
                  <MapPin className="size-3 text-primary shrink-0" />
                  <span>{supplier.location}</span>
                </div>

                {/* Specialty Tag */}
                <div className="rounded-lg bg-muted/40 p-2 text-xs text-muted-foreground leading-relaxed mb-4">
                  <span className="font-medium text-foreground block text-[11px] mb-0.5">
                    Craft Specialty:
                  </span>
                  {supplier.specialty}
                </div>

                {/* Contact & Storefront Info */}
                <div className="flex flex-col gap-1.5 text-xs text-muted-foreground mb-4">
                  {supplier.websiteUrl && (
                    <div className="flex items-center justify-between gap-1.5 rounded-lg bg-primary/5 px-2.5 py-1.5 border border-primary/10">
                      <div className="flex items-center gap-1.5 truncate text-[11px]">
                        <Globe className="size-3 text-primary shrink-0" />
                        <a
                          href={supplier.websiteUrl}
                          target="_blank"
                          rel="noreferrer"
                          className="truncate hover:underline text-foreground font-medium flex items-center gap-1"
                        >
                          <span>{supplier.websiteUrl.replace(/^https?:\/\//, '')}</span>
                          <ExternalLink className="size-2.5 opacity-60" />
                        </a>
                      </div>
                      <Badge variant="outline" className="text-[9px] bg-primary/10 text-primary border-primary/20 shrink-0">
                        AI Scraper Ready
                      </Badge>
                    </div>
                  )}
                  <div className="flex items-center gap-2">
                    <Mail className="size-3 text-muted-foreground/70" />
                    <span className="truncate">{supplier.contactEmail}</span>
                  </div>
                  <div className="flex items-center gap-2">
                    <Phone className="size-3 text-muted-foreground/70" />
                    <span>{supplier.contactPhone}</span>
                  </div>
                </div>

                {/* Operational Metrics */}
                <div className="grid grid-cols-2 gap-2 border-t border-border/60 pt-3 text-[11px]">
                  <div className="rounded-lg bg-muted/20 p-2">
                    <span className="text-muted-foreground block text-[10px]">Lead Time</span>
                    <span className="font-semibold text-foreground flex items-center gap-1 mt-0.5">
                      <Clock className="size-3 text-primary" />
                      {supplier.deliveryTimeDays} days
                    </span>
                  </div>
                  <div className="rounded-lg bg-muted/20 p-2">
                    <span className="text-muted-foreground block text-[10px]">Min. Order (MOQ)</span>
                    <span className="font-semibold text-foreground flex items-center gap-1 mt-0.5">
                      <Coins className="size-3 text-primary" aria-hidden />
                      {formatMoney(supplier.minimumOrder)}
                    </span>
                  </div>
                </div>
              </div>

              {/* Catalog Action */}
              <div className="pt-4 mt-4 border-t border-border/60">
                <Button
                  variant="outline"
                  size="sm"
                  className="w-full gap-1.5 text-xs h-8 rounded-lg"
                  onClick={() => setActiveCatalogSupplier(supplier)}
                >
                  <Layers className="size-3.5" />
                  <span>
                    View Atelier Catalog ({supplier.sampleCatalogCount ?? 0} items)
                  </span>
                </Button>
              </div>
            </Card>
          ))}
        </div>
      )}

      {/* Supplier Catalog Modal */}
      {activeCatalogSupplier && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/50 backdrop-blur-xs p-4 animate-in fade-in">
          <Card className="flex flex-col w-full max-w-2xl border-border bg-background shadow-2xl p-6 gap-4 animate-in zoom-in-95">
            <div className="flex items-center justify-between border-b border-border pb-3">
              <div>
                <h3 className="font-serif text-base font-semibold">
                  {activeCatalogSupplier.name} - Catalog
                </h3>
                <p className="text-xs text-muted-foreground">
                  Wholesale samples available for direct sourcing and client orders
                </p>
              </div>
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setActiveCatalogSupplier(null)}
                className="size-8 p-0"
              >
                <X className="size-4" />
              </Button>
            </div>

            {activeCatalogSupplier.catalogItems && activeCatalogSupplier.catalogItems.length > 0 ? (
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 max-h-96 overflow-y-auto p-1">
                {activeCatalogSupplier.catalogItems.map((catItem) => (
                  <div
                    key={catItem.id}
                    className="flex gap-3 rounded-xl border border-border/80 bg-card p-3 shadow-2xs"
                  >
                    <img
                      loading="lazy"
                      decoding="async"
                      src={catItem.imageUrl}
                      alt={catItem.name}
                      className="size-16 rounded-lg object-cover border border-border"
                    />
                    <div className="min-w-0 flex-1">
                      <Badge variant="outline" className="text-[9px] py-0 mb-1">
                        {catItem.category}
                      </Badge>
                      <h5 className="truncate text-xs font-medium text-foreground">
                        {catItem.name}
                      </h5>
                      <p className="text-[11px] text-muted-foreground">{catItem.fabric}</p>
                      <p className="text-xs font-semibold text-primary mt-1">
                        Wholesale: ${catItem.wholesalePrice}
                      </p>
                    </div>
                  </div>
                ))}
              </div>
            ) : (
              <div className="py-12 text-center text-muted-foreground">
                <Package className="size-10 stroke-1 mx-auto mb-2 text-muted-foreground/40" />
                <p className="text-xs font-medium text-foreground">Live Catalog Synchronizing</p>
                <p className="text-[11px] text-muted-foreground mt-0.5">
                  Direct API sync for {activeCatalogSupplier.name} catalog is enabled.
                </p>
              </div>
            )}

            <div className="flex justify-end pt-3 border-t border-border">
              <Button size="sm" onClick={() => setActiveCatalogSupplier(null)}>
                Close Catalog
              </Button>
            </div>
          </Card>
        </div>
      )}

      {/* Add Partner Atelier Modal */}
      <AddSupplierModal
        open={addModalOpen}
        organizationId={organizationId}
        onClose={() => setAddModalOpen(false)}
        onSave={handleSaveSupplier}
      />
    </div>
  )
}
