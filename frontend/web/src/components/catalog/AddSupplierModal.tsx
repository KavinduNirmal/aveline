import React, { useState } from 'react'
import { Building2, X, Sparkles, MapPin, Mail, Phone, Clock, Coins, Globe, Loader2 } from 'lucide-react'
import { toast } from 'sonner'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { createSupplier } from '@/lib/catalog-api'
import type { SupplierMock } from './mockData'

interface AddSupplierModalProps {
  open: boolean
  organizationId?: string
  onClose: () => void
  onSave: (supplier: SupplierMock) => void
}

export function AddSupplierModal({
  open,
  organizationId,
  onClose,
  onSave,
}: AddSupplierModalProps) {
  const [name, setName] = useState('')
  const [specialty, setSpecialty] = useState('')
  const [location, setLocation] = useState('')
  const [websiteUrl, setWebsiteUrl] = useState('')
  const [contactEmail, setContactEmail] = useState('')
  const [contactPhone, setContactPhone] = useState('')
  const [deliveryTimeDays, setDeliveryTimeDays] = useState('5')
  const [minimumOrder, setMinimumOrder] = useState('25000')
  const [isSubmitting, setIsSubmitting] = useState(false)

  if (!open) return null

  const resetForm = () => {
    setName('')
    setSpecialty('')
    setLocation('')
    setWebsiteUrl('')
    setContactEmail('')
    setContactPhone('')
    setDeliveryTimeDays('5')
    setMinimumOrder('25000')
  }

  const handleClose = () => {
    resetForm()
    onClose()
  }

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault()
    if (!name.trim()) {
      toast.error('Atelier name is required')
      return
    }

    setIsSubmitting(true)
    try {
      const payload = {
        name: name.trim(),
        specialty: specialty.trim() || undefined,
        location: location.trim() || undefined,
        websiteUrl: websiteUrl.trim() || undefined,
        apiEndpoint: websiteUrl.trim() || undefined,
        contactEmail: contactEmail.trim() || undefined,
        contactPhone: contactPhone.trim() || undefined,
        deliveryTimeDays: parseInt(deliveryTimeDays, 10) || 5,
        minimumOrder: parseFloat(minimumOrder) || 0,
        isActive: true,
      }

      let savedSupplier: SupplierMock
      if (organizationId) {
        const apiResult = await createSupplier(organizationId, payload)
        savedSupplier = {
          id: apiResult.id,
          name: apiResult.name,
          specialty: apiResult.specialty || payload.specialty || 'Fine Handloom Craft',
          contactEmail: apiResult.contactEmail || payload.contactEmail || '',
          contactPhone: apiResult.contactPhone || payload.contactPhone || '',
          location: apiResult.location || payload.location || 'Atelier Network',
          websiteUrl: apiResult.websiteUrl || payload.websiteUrl || '',
          minimumOrder: apiResult.minimumOrder ?? payload.minimumOrder,
          deliveryTimeDays: apiResult.deliveryTimeDays ?? payload.deliveryTimeDays,
          isActive: apiResult.isActive ?? true,
          sampleCatalogCount: 0,
        }
      } else {
        savedSupplier = {
          id: `sup-${Date.now()}`,
          name: payload.name,
          specialty: payload.specialty || 'Fine Handloom Craft',
          contactEmail: payload.contactEmail || '',
          contactPhone: payload.contactPhone || '',
          location: payload.location || 'Colombo, Sri Lanka',
          websiteUrl: payload.websiteUrl || '',
          minimumOrder: payload.minimumOrder,
          deliveryTimeDays: payload.deliveryTimeDays,
          isActive: true,
          sampleCatalogCount: 0,
        }
      }

      onSave(savedSupplier)
      toast.success(`Partner Atelier "${savedSupplier.name}" added successfully`, {
        description: 'Available for direct sourcing and client orders.',
      })
      handleClose()
    } catch {
      toast.error('Failed to register partner atelier. Please try again.')
    } finally {
      setIsSubmitting(false)
    }
  }

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4 animate-in fade-in">
      <div
        className="relative flex flex-col w-full max-w-xl max-h-[90vh] overflow-y-auto rounded-2xl border border-border bg-card shadow-2xl p-6 gap-5 animate-in zoom-in-95"
        role="dialog"
        aria-labelledby="add-supplier-title"
      >
        {/* Modal Header */}
        <div className="flex items-start justify-between border-b border-border/80 pb-4">
          <div className="flex items-center gap-3">
            <div className="flex size-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Building2 className="size-5" />
            </div>
            <div>
              <h3 id="add-supplier-title" className="font-serif text-lg font-semibold text-foreground">
                Connect Partner Atelier
              </h3>
              <p className="text-xs text-muted-foreground">
                Register a heritage mill, artisan weaver, or partner boutique for client sourcing
              </p>
            </div>
          </div>
          <Button
            type="button"
            variant="ghost"
            size="icon"
            onClick={handleClose}
            className="rounded-lg text-muted-foreground hover:bg-muted hover:text-foreground transition-colors size-8"
            aria-label="Close"
          >
            <X className="size-4" />
          </Button>
        </div>

        {/* Modal Form */}
        <form onSubmit={handleSubmit} className="flex flex-col gap-4">
          {/* Atelier Name */}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="supplier-name" className="text-xs font-medium">
              Atelier / Supplier Name <span className="text-destructive">*</span>
            </Label>
            <Input
              id="supplier-name"
              placeholder="e.g. Colombo Heritage Silk Weavers"
              value={name}
              onChange={(e) => setName(e.target.value)}
              className="h-9 text-sm"
              required
              autoFocus
            />
          </div>

          {/* Craft Specialty */}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="supplier-specialty" className="text-xs font-medium flex items-center gap-1.5">
              <Sparkles className="size-3 text-primary" />
              Craft Specialty / Textile Focus
            </Label>
            <Textarea
              id="supplier-specialty"
              placeholder="e.g. Pure Mulberry Silk, Gold Zari Brocade, Banarasi Weaves, Tailored Suits"
              value={specialty}
              onChange={(e) => setSpecialty(e.target.value)}
              className="min-h-16 text-sm resize-none"
            />
          </div>

          {/* Location & Contact Grid */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-3.5">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="supplier-location" className="text-xs font-medium flex items-center gap-1.5">
                <MapPin className="size-3 text-primary" />
                Location / City
              </Label>
              <Input
                id="supplier-location"
                placeholder="e.g. Pettah, Colombo or Varanasi"
                value={location}
                onChange={(e) => setLocation(e.target.value)}
                className="h-9 text-sm"
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="supplier-email" className="text-xs font-medium flex items-center gap-1.5">
                <Mail className="size-3 text-primary" />
                Contact Email
              </Label>
              <Input
                id="supplier-email"
                type="email"
                placeholder="e.g. orders@colombosilks.lk"
                value={contactEmail}
                onChange={(e) => setContactEmail(e.target.value)}
                className="h-9 text-sm"
              />
            </div>
          </div>

          {/* Storefront / Catalog Website URL */}
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="supplier-website" className="text-xs font-medium flex items-center justify-between">
              <span className="flex items-center gap-1.5">
                <Globe className="size-3 text-primary" />
                Storefront / Catalog Website URL
              </span>
              <span className="text-[10px] text-muted-foreground font-normal">
                Enables Elle Visual Agent Web Scraping
              </span>
            </Label>
            <Input
              id="supplier-website"
              type="url"
              placeholder="e.g. https://maisondesoie.example.com"
              value={websiteUrl}
              onChange={(e) => setWebsiteUrl(e.target.value)}
              className="h-9 text-sm"
            />
          </div>

          {/* Phone & Operational Metrics Grid */}
          <div className="grid grid-cols-1 sm:grid-cols-3 gap-3.5">
            <div className="flex flex-col gap-1.5">
              <Label htmlFor="supplier-phone" className="text-xs font-medium flex items-center gap-1.5">
                <Phone className="size-3 text-primary" />
                WhatsApp / Phone
              </Label>
              <Input
                id="supplier-phone"
                placeholder="e.g. +94 77 123 4567"
                value={contactPhone}
                onChange={(e) => setContactPhone(e.target.value)}
                className="h-9 text-sm"
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="supplier-leadtime" className="text-xs font-medium flex items-center gap-1.5">
                <Clock className="size-3 text-primary" />
                Lead Time (Days)
              </Label>
              <Input
                id="supplier-leadtime"
                type="number"
                min="1"
                max="90"
                placeholder="5"
                value={deliveryTimeDays}
                onChange={(e) => setDeliveryTimeDays(e.target.value)}
                className="h-9 text-sm"
              />
            </div>

            <div className="flex flex-col gap-1.5">
              <Label htmlFor="supplier-moq" className="text-xs font-medium flex items-center gap-1.5">
                <Coins className="size-3 text-primary" />
                Min. Order (LKR)
              </Label>
              <Input
                id="supplier-moq"
                type="number"
                min="0"
                step="500"
                placeholder="25000"
                value={minimumOrder}
                onChange={(e) => setMinimumOrder(e.target.value)}
                className="h-9 text-sm"
              />
            </div>
          </div>

          {/* Action Buttons */}
          <div className="flex items-center justify-end gap-2.5 pt-4 border-t border-border/80 mt-2">
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={handleClose}
              disabled={isSubmitting}
            >
              Cancel
            </Button>
            <Button
              type="submit"
              size="sm"
              disabled={isSubmitting}
              className="gap-1.5 bg-primary text-primary-foreground font-medium"
            >
              {isSubmitting ? (
                <>
                  <Loader2 className="size-3.5 animate-spin" />
                  <span>Connecting...</span>
                </>
              ) : (
                <>
                  <Building2 className="size-3.5" />
                  <span>Connect Atelier</span>
                </>
              )}
            </Button>
          </div>
        </form>
      </div>
    </div>
  )
}
