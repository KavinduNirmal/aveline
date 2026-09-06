import { ArrowRight, Sparkles } from 'lucide-react'

import { Alert, AlertDescription } from '@/components/ui/alert'
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

import { PLANS } from '../plans'
import { useOwnerOnboardingWizard } from '../wizard-context'

export function PlanSelectionStep() {
  const { draft, patch, goTo, isSubmitting, handleSelectPlan } = useOwnerOnboardingWizard()

  return (
    <div className="flex flex-col gap-6">
      <Alert className="border-primary/40 bg-primary/5">
        <Sparkles className="size-4 text-primary" />
        <AlertDescription className="text-sm font-medium">
          <strong>Demo Mode Active:</strong> You are setting up your atelier in demonstration mode.
          No payment or credit card is collected today; your account will receive full Blossom
          credits immediately, and our team will contact you for payment verification later.
        </AlertDescription>
      </Alert>

      <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
        {PLANS.map((plan) => (
          <Card
            key={plan.tier}
            className={`cursor-pointer transition-all border-2 relative ${
              draft.selectedPlanTier === plan.tier
                ? 'border-primary ring-2 ring-primary/20 shadow-md'
                : plan.color
            }`}
            onClick={() => patch({ selectedPlanTier: plan.tier })}
          >
            {plan.badge && (
              <Badge className="absolute top-3 right-3 text-[10px]" variant="secondary">
                {plan.badge}
              </Badge>
            )}
            <CardHeader className="pb-3">
              <CardTitle className="font-serif text-xl flex items-center justify-between">
                {plan.name}
                <span className="text-sm font-sans font-semibold text-primary">{plan.price}</span>
              </CardTitle>
              <CardDescription>{plan.tagline}</CardDescription>
            </CardHeader>
            <CardContent className="flex flex-col gap-2.5 text-xs text-muted-foreground pb-4">
              <div className="flex items-center justify-between py-1 border-b border-border/50">
                <span className="font-medium text-foreground">Monthly Blossom Credits</span>
                <span className="font-semibold text-primary">{plan.blossoms.toLocaleString()}</span>
              </div>
              <div className="flex items-center justify-between py-1 border-b border-border/50">
                <span>Staff Seats</span>
                <span className="text-foreground font-medium">{plan.staff}</span>
              </div>
              <div className="flex items-center justify-between py-1">
                <span>Active Customer Limit</span>
                <span className="text-foreground font-medium">{plan.customers.toLocaleString()}</span>
              </div>
            </CardContent>
            <CardFooter className="pt-0">
              <Badge
                variant={draft.selectedPlanTier === plan.tier ? 'default' : 'outline'}
                className="w-full justify-center py-1"
              >
                {draft.selectedPlanTier === plan.tier ? 'Selected Plan' : 'Select Plan'}
              </Badge>
            </CardFooter>
          </Card>
        ))}
      </div>

      <div className="flex justify-between items-center">
        <Button variant="outline" onClick={() => goTo(3)}>
          Back
        </Button>
        <Button onClick={() => void handleSelectPlan()} disabled={isSubmitting}>
          {isSubmitting ? 'Saving…' : 'Continue to AI Customization'}{' '}
          <ArrowRight className="size-4" data-icon="inline-end" />
        </Button>
      </div>
    </div>
  )
}
