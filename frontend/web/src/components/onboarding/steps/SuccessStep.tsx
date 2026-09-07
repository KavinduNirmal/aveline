import { ArrowRight, CheckCircle2, Sparkles } from 'lucide-react'

import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

import { useOwnerOnboardingWizard } from '../wizard-context'

export function SuccessStep() {
  const { draft, completedAllocation, handleEnterDashboard } = useOwnerOnboardingWizard()

  return (
    <Card className="text-center py-8 shadow-xl border-primary/20">
      <CardHeader className="flex flex-col items-center gap-3">
        <div className="size-16 rounded-full bg-primary/10 flex items-center justify-center text-primary ring-8 ring-primary/5">
          <CheckCircle2 className="size-8" />
        </div>
        <CardTitle className="font-serif text-3xl font-medium">
          Welcome to Aveline, {draft.boutiqueName}
        </CardTitle>
        <CardDescription className="max-w-md mx-auto text-sm">
          Your boutique account has been created and your role configured as Principal Owner.
        </CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col items-center gap-4">
        <div className="flex items-center gap-3 py-2 px-5 rounded-full bg-primary/10 border border-primary/20">
          <Sparkles className="size-4 text-primary" />
          <span className="text-sm font-medium text-foreground">
            <strong>{completedAllocation.toLocaleString()} Blossoms</strong> provisioned for your atelier
          </span>
        </div>

        <div className="flex flex-col gap-1 max-w-sm text-xs text-muted-foreground">
          <p>• Customer Memory Agent is warmed up with your boutique identity.</p>
          <p>• Commerce Agent policies are armed and ready.</p>
          <p>• Staff invitations can now be generated from the Atelier Dashboard.</p>
        </div>
      </CardContent>
      <CardFooter className="flex justify-center pt-2">
        <Button size="lg" className="min-w-[220px]" onClick={handleEnterDashboard}>
          Enter Atelier Dashboard <ArrowRight className="size-4" data-icon="inline-end" />
        </Button>
      </CardFooter>
    </Card>
  )
}
