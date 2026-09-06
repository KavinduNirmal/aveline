import { useUser } from '@clerk/react'
import { CheckCircle2, Sparkles } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Separator } from '@/components/ui/separator'

import { planBlossomLabel } from '../plans'
import { useOwnerOnboardingWizard } from '../wizard-context'

export function ReviewStep() {
  const { user: clerkUser } = useUser()
  const { draft, goTo, isSubmitting, handleCompleteOnboarding } = useOwnerOnboardingWizard()

  return (
    <Card>
      <CardHeader>
        <CardTitle className="font-serif text-2xl font-medium">Review &amp; Launch Boutique</CardTitle>
        <CardDescription>
          Verify your atelier configuration. Clicking finalize will activate your organization,
          credit your Blossom allowance, and warm up your AI agents.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-5">
        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 bg-muted/30 p-4 rounded-xl border border-border">
          <div className="flex flex-col gap-1">
            <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Boutique</span>
            <p className="font-serif font-medium text-foreground text-lg">{draft.boutiqueName}</p>
            <p className="text-xs text-muted-foreground">{draft.address}</p>
            <p className="text-xs text-muted-foreground">{draft.phoneNumber}</p>
          </div>
          <div className="flex flex-col gap-1">
            <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">Selected Plan</span>
            <div className="flex items-center gap-2">
              <p className="font-medium text-foreground text-lg">{draft.selectedPlanTier} Tier</p>
              <Badge variant="secondary">Demo Mode</Badge>
            </div>
            <p className="text-xs text-primary font-medium">{planBlossomLabel(draft.selectedPlanTier)}</p>
            <p className="text-xs text-muted-foreground">
              Principal Owner: {clerkUser?.fullName || clerkUser?.primaryEmailAddress?.emailAddress}
            </p>
          </div>
        </div>

        <Separator />

        <div className="flex flex-col gap-2">
          <span className="text-xs font-semibold uppercase tracking-wider text-muted-foreground">AI Concierge Persona</span>
          <p className="text-xs text-foreground bg-muted/50 p-3 rounded-lg border border-border font-serif italic">
            &ldquo;{draft.selectedPlanTier === 'Seed' ? 'Standard Quiet Luxury Essential Concierge' : draft.brandVoice}&rdquo;
          </p>
        </div>

        <div className="flex items-center gap-3 p-3 rounded-lg bg-emerald-500/10 border border-emerald-500/20 text-emerald-800 dark:text-emerald-300 text-xs">
          <CheckCircle2 className="size-4 shrink-0" />
          <span>Ready to initialize Customer Memory, Visual Insight, and Commerce agents.</span>
        </div>
      </CardContent>
      <CardFooter className="flex justify-between gap-3">
        <Button variant="outline" onClick={() => goTo(7)}>
          Back to Team
        </Button>
        <Button
          size="lg"
          onClick={() => void handleCompleteOnboarding()}
          disabled={isSubmitting}
          className="min-w-[200px]"
        >
          {isSubmitting ? (
            <span className="flex items-center gap-2">
              <span className="animate-spin size-4 border-2 border-current border-t-transparent rounded-full" />
              Warming Up Agents…
            </span>
          ) : (
            <>
              <Sparkles className="size-4" data-icon="inline-start" /> Create Boutique
            </>
          )}
        </Button>
      </CardFooter>
    </Card>
  )
}
