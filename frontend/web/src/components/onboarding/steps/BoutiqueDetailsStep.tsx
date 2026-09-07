import { ArrowLeft, ArrowRight } from 'lucide-react'
import type { FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { cn } from '@/lib/utils'
import {
  ADDRESS_MAX,
  BOUTIQUE_DESCRIPTION_MAX,
  BOUTIQUE_NAME_MAX,
  formatLkPhone,
  isValidLkPhone,
  LOGO_URL_MAX,
} from '@/lib/boutique'

import { useOwnerOnboardingWizard } from '../wizard-context'

export function BoutiqueDetailsStep() {
  const { draft, patch, goTo, isSubmitting, handleSaveBoutiqueDetails } =
    useOwnerOnboardingWizard()

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    void handleSaveBoutiqueDetails()
  }

  const nameLength = draft.boutiqueName.trim().length
  const descriptionLength = draft.description.trim().length
  const phoneValid = isValidLkPhone(draft.phoneNumber)

  // Live gate: required fields present + lengths within limits + a full 9-digit number.
  const canProceed =
    nameLength > 0 &&
    nameLength <= BOUTIQUE_NAME_MAX &&
    draft.address.trim().length > 0 &&
    draft.address.trim().length <= ADDRESS_MAX &&
    phoneValid &&
    descriptionLength <= BOUTIQUE_DESCRIPTION_MAX

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle className="font-serif text-xl font-medium">Boutique Information</CardTitle>
          <Button variant="ghost" size="sm" onClick={() => goTo(2)}>
            <ArrowLeft className="size-3.5" data-icon="inline-start" /> Back
          </Button>
        </div>
        <CardDescription>
          This information establishes your organization profile and public atelier identity.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form id="detailsForm" onSubmit={onSubmit} className="flex flex-col gap-5">
          <div className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="boutiqueName" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Boutique Name <span className="text-destructive">*</span>
              </Label>
              <span className="text-xs text-muted-foreground tabular-nums">
                {nameLength}/{BOUTIQUE_NAME_MAX}
              </span>
            </div>
            <Input
              id="boutiqueName"
              required
              maxLength={BOUTIQUE_NAME_MAX}
              value={draft.boutiqueName}
              onChange={(e) => patch({ boutiqueName: e.target.value })}
              placeholder="e.g. House of Fashions"
            />
            {nameLength >= BOUTIQUE_NAME_MAX && (
              <p className="text-xs text-destructive">
                Boutique name is limited to {BOUTIQUE_NAME_MAX} characters.
              </p>
            )}
            {draft.boutiqueName && (
              <p className="text-xs text-muted-foreground font-mono">
                Domain handle: aveline.app/b/
                {draft.boutiqueName.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '')}
              </p>
            )}
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="address" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Physical Address <span className="text-destructive">*</span>
              </Label>
              <span className="text-xs text-muted-foreground tabular-nums">
                {draft.address.trim().length}/{ADDRESS_MAX}
              </span>
            </div>
            <Textarea
              id="address"
              required
              rows={2}
              maxLength={ADDRESS_MAX}
              value={draft.address}
              onChange={(e) => patch({ address: e.target.value })}
              placeholder="No. 42, Galle Road, Colombo 03"
              className="resize-none"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="phone" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Contact Phone Number <span className="text-destructive">*</span>
            </Label>
            <Input
              id="phone"
              required
              type="tel"
              inputMode="numeric"
              autoComplete="tel-national"
              value={draft.phoneNumber}
              onChange={(e) => patch({ phoneNumber: formatLkPhone(e.target.value) })}
              placeholder="+94 77 12 12 123"
              className={cn(!phoneValid && draft.phoneNumber.trim() !== '+94' && 'border-destructive focus-visible:ring-destructive')}
            />
            <p className="text-xs text-muted-foreground">
              A Sri Lankan number: your 9 digits after the +94 country code.
            </p>
            {!phoneValid && draft.phoneNumber.trim() !== '+94' && (
              <p className="text-xs text-destructive">
                Enter a valid number, e.g. +94 77 12 12 123 (numbers only).
              </p>
            )}
          </div>

          <div className="flex flex-col gap-1.5">
            <div className="flex items-center justify-between">
              <Label htmlFor="description" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                Boutique Description (Optional)
              </Label>
              <span className="text-xs text-muted-foreground tabular-nums">
                {descriptionLength}/{BOUTIQUE_DESCRIPTION_MAX}
              </span>
            </div>
            <Textarea
              id="description"
              rows={2}
              maxLength={BOUTIQUE_DESCRIPTION_MAX}
              value={draft.description}
              onChange={(e) => patch({ description: e.target.value })}
              placeholder="A boutique specializing in luxury sarees and bespoke formal wear."
              className="resize-none"
            />
            {descriptionLength >= BOUTIQUE_DESCRIPTION_MAX && (
              <p className="text-xs text-destructive">
                Description is limited to {BOUTIQUE_DESCRIPTION_MAX} characters.
              </p>
            )}
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="logoUrl" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Logo Image URL (Optional)
            </Label>
            <Input
              id="logoUrl"
              type="url"
              maxLength={LOGO_URL_MAX}
              value={draft.logoUrl}
              onChange={(e) => patch({ logoUrl: e.target.value })}
              placeholder="https://example.com/logo.png"
            />
          </div>
        </form>
      </CardContent>
      <CardFooter className="flex justify-between gap-3">
        <Button type="button" variant="outline" onClick={() => goTo(2)}>
          Back
        </Button>
        <Button form="detailsForm" type="submit" disabled={isSubmitting || !canProceed}>
          {isSubmitting ? 'Saving…' : 'Continue to Plan Selection'}{' '}
          <ArrowRight className="size-4" data-icon="inline-end" />
        </Button>
      </CardFooter>
    </Card>
  )
}
