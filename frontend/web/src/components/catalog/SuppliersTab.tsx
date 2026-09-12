import { useState } from 'react'
import {
  Building2,
  MapPin,
  Mail,
  Phone,
  Clock,
  DollarSign,
  Package,
  Layers,
  X,
} from 'lucide-react'
import { Card } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import type { SupplierMock } from './mockData'

interface SuppliersTabProps {
  suppliers: SupplierMock[]
}

export function SuppliersTab({ suppliers }: SuppliersTabProps) {
  const [activeCatalogSupplier, setActiveCatalogSupplier] = useState<SupplierMock | null>(null)

  return (
    <div className="space-y-6">
      {/* Tab Header */}
      <div>
        <h3 className="font-serif text-base font-semibold text-foreground">
          Partner Ateliers & Heritage Fabric Mills
        </h3>
        <p className="text-xs text-muted-foreground">
          Direct supplier integrations for handloom silks, bespoke zari embroidery, and fabric sourcing
        </p>
      </div>

      {/* Suppliers Grid */}
      {suppliers.length === 0 ? (
        <Card className="flex flex-col items-center justify-center p-12 text-center border-dashed border-border/80 bg-card/50">
          <Building2 className="size-12 text-muted-foreground/40 mb-3" />
          <h4 className="font-serif text-base font-medium">No Partner Ateliers Found</h4>
          <p className="text-xs text-muted-foreground max-w-sm mt-1 mb-4">
            Connect your heritage suppliers and fabric mills to track sourcing lead times and minimum orders.
          </p>
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
                        ? 'bg-emerald-500/10 text-emerald-600 border-emerald-500/20 text-[10px]'
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

                {/* Contact Info */}
                <div className="space-y-1.5 text-xs text-muted-foreground mb-4">
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
                      <DollarSign className="size-3 text-primary" />
                      ${supplier.minimumOrder.toLocaleString()}
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
          <Card className="w-full max-w-2xl border-border bg-background shadow-2xl p-6 space-y-4 animate-in zoom-in-95">
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
    </div>
  )
}
