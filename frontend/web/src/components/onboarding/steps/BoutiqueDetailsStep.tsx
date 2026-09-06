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

import { useOwnerOnboardingWizard } from '../wizard-context'

export function BoutiqueDetailsStep() {
  const { draft, patch, goTo, isSubmitting, handleSaveBoutiqueDetails } =
    useOwnerOnboardingWizard()

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    void handleSaveBoutiqueDetails()
  }

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
            <Label htmlFor="boutiqueName" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Boutique Name <span className="text-destructive">*</span>
            </Label>
            <Input
              id="boutiqueName"
              required
              value={draft.boutiqueName}
              onChange={(e) => patch({ boutiqueName: e.target.value })}
              placeholder="e.g. House of Fashions"
            />
            {draft.boutiqueName && (
              <p className="text-xs text-muted-foreground font-mono">
                Domain handle: aveline.app/b/
                {draft.boutiqueName.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/(^-|-$)/g, '')}
              </p>
            )}
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="address" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Physical Address <span className="text-destructive">*</span>
            </Label>
            <Textarea
              id="address"
              required
              rows={2}
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
              value={draft.phoneNumber}
              onChange={(e) => patch({ phoneNumber: e.target.value })}
              placeholder="+94 77 123 4567"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="description" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Boutique Description (Optional)
            </Label>
            <Textarea
              id="description"
              rows={2}
              value={draft.description}
              onChange={(e) => patch({ description: e.target.value })}
              placeholder="A boutique specializing in luxury sarees and bespoke formal wear."
              className="resize-none"
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <Label htmlFor="logoUrl" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Logo Image URL (Optional)
            </Label>
            <Input
              id="logoUrl"
              type="url"
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
        <Button form="detailsForm" type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : 'Continue to Plan Selection'}{' '}
          <ArrowRight className="size-4" data-icon="inline-end" />
        </Button>
      </CardFooter>
    </Card>
  )
}
