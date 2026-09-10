import { ArrowLeft, ArrowRight, Lock } from 'lucide-react'
import type { FormEvent } from 'react'

import { Alert, AlertDescription } from '@/components/ui/alert'
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
import { ToggleGroup, ToggleGroupItem } from '@/components/ui/toggle-group'

import { useOwnerOnboardingWizard } from '../wizard-context'

const BRAND_VOICE_OPTIONS = [
  'Poised, discreet, and warmly attentive',
  'Warm, friendly, and approachable atelier',
  'High-fashion, editorial, and sophisticated',
]

export function AiCustomizationStep() {
  const { draft, patch, goTo, isSubmitting, handleSaveAiContext } = useOwnerOnboardingWizard()

  const onSubmit = (e: FormEvent) => {
    e.preventDefault()
    void handleSaveAiContext()
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex items-center justify-between">
          <CardTitle className="font-serif text-xl font-medium">Boutique AI Concierge Context</CardTitle>
          <Button variant="ghost" size="sm" onClick={() => goTo(4)}>
            <ArrowLeft className="size-3.5" data-icon="inline-start" /> Back
          </Button>
        </div>
        <CardDescription>
          Tailor how Aveline&apos;s Customer Memory, Visual Insight, and Commerce agents interact
          with your clientele. Features adapt to your chosen {draft.selectedPlanTier} plan.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <form id="contextForm" onSubmit={onSubmit} className="flex flex-col gap-6">
          {draft.selectedPlanTier === 'Seed' ? (
            <Alert className="bg-muted">
              <Lock className="size-4 text-muted-foreground" />
              <AlertDescription className="text-xs text-muted-foreground">
                The <strong>Seed Plan</strong> operates on standard Quiet Luxury concierge presets.
                Custom prompts and bespoke business policy tuning unlock on the <strong>Bloom</strong> and{' '}
                <strong>Orchid</strong> plans. You may proceed with standard curated defaults or step back to
                upgrade your plan.
              </AlertDescription>
            </Alert>
          ) : (
            <>
              {/* Brand Voice */}
              <div className="flex flex-col gap-2">
                <Label htmlFor="brandVoice" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Brand Voice &amp; Tone (Customer Interaction Agent)
                </Label>
                <ToggleGroup
                  type="single"
                  value={draft.brandVoice}
                  onValueChange={(val) => {
                    if (val) patch({ brandVoice: val })
                  }}
                  className="flex flex-wrap justify-start"
                >
                  {BRAND_VOICE_OPTIONS.map((tone) => (
                    <ToggleGroupItem key={tone} value={tone} variant="outline" size="sm">
                      {tone}
                    </ToggleGroupItem>
                  ))}
                </ToggleGroup>
                <Input
                  id="brandVoice"
                  value={draft.brandVoice}
                  onChange={(e) => patch({ brandVoice: e.target.value })}
                  placeholder="Custom brand voice prompt"
                />
              </div>

              {/* Business Rules */}
              <div className="flex flex-col gap-1.5">
                <Label htmlFor="bizRules" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                  Business &amp; Commerce Rules (Commerce Agent)
                </Label>
                <Textarea
                  id="bizRules"
                  rows={2}
                  value={draft.businessRules}
                  onChange={(e) => patch({ businessRules: e.target.value })}
                  placeholder="Max discount %, delivery rules, return and exchange policy"
                  className="resize-none"
                />
                <p className="text-xs text-muted-foreground">
                  Used by the Commerce Agent to negotiate authorized discounts and enforce delivery terms.
                </p>
              </div>

              {/* Orchid / Rose Tier Fields */}
              {draft.selectedPlanTier === 'Bloom' ? (
                <Alert className="bg-muted/50 border border-dashed">
                  <Lock className="size-4 text-muted-foreground" />
                  <AlertDescription className="text-xs text-muted-foreground">
                    Fabric specialization recognition and deep client memory rules are available on{' '}
                    <strong>Orchid</strong> and <strong>Rose</strong> plans.
                  </AlertDescription>
                </Alert>
              ) : (
                <>
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="fabrics" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                      Preferred Colors &amp; Fabrics (Visual Insight Agent)
                    </Label>
                    <Textarea
                      id="fabrics"
                      rows={2}
                      value={draft.preferredColorsFabrics}
                      onChange={(e) => patch({ preferredColorsFabrics: e.target.value })}
                      placeholder="e.g. Raw silk, French lace, fine cashmere, pastel blush, emerald jewel tones"
                      className="resize-none"
                    />
                  </div>

                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor="prefs" className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">
                      Customer Etiquette &amp; Memory (Customer Memory Agent)
                    </Label>
                    <Textarea
                      id="prefs"
                      rows={2}
                      value={draft.customerPreferences}
                      onChange={(e) => patch({ customerPreferences: e.target.value })}
                      placeholder="e.g. Welcome client with Ceylon tea, note sizing nuances and anniversaries"
                      className="resize-none"
                    />
                  </div>
                </>
              )}
            </>
          )}
        </form>
      </CardContent>
      <CardFooter className="flex justify-between gap-3">
        <Button variant="outline" onClick={() => goTo(4)}>
          Back
        </Button>
        <Button form="contextForm" type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : 'Continue to Integrations'}{' '}
          <ArrowRight className="size-4" data-icon="inline-end" />
        </Button>
      </CardFooter>
    </Card>
  )
}
