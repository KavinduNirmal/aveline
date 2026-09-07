import { Check, Crown } from 'lucide-react'

import { AuroraField } from '@/components/site/AuroraField'
import { Skeleton } from '@/components/ui/skeleton'

import { AccountTypeStep } from './steps/AccountTypeStep'
import { AiCustomizationStep } from './steps/AiCustomizationStep'
import { BoutiqueDetailsStep } from './steps/BoutiqueDetailsStep'
import { IntegrationsStep } from './steps/IntegrationsStep'
import { InviteStaffStep } from './steps/InviteStaffStep'
import { PlanSelectionStep } from './steps/PlanSelectionStep'
import { ReviewStep } from './steps/ReviewStep'
import { SuccessStep } from './steps/SuccessStep'
import { OwnerOnboardingWizardProvider, useOwnerOnboardingWizard } from './wizard-context'

const STEP_LABELS = [
  { num: 1, label: 'Sign In' },
  { num: 2, label: 'Role' },
  { num: 3, label: 'Details' },
  { num: 4, label: 'Plan' },
  { num: 5, label: 'Context' },
  { num: 6, label: 'Integrations' },
  { num: 7, label: 'Team' },
  { num: 8, label: 'Launch' },
]

function StepIndicator({ step }: { step: number }) {
  return (
    <div className="max-w-xl mx-auto w-full px-2 py-3 bg-muted/40 rounded-full border border-border flex items-center justify-center gap-1.5 sm:gap-2">
      {STEP_LABELS.map((s, idx) => {
        const done = step > s.num
        const active = step === s.num
        return (
          <div key={s.num} className="flex items-center gap-1.5 sm:gap-2">
            {idx > 0 && <span className={`h-px w-2 sm:w-3 ${done || active ? 'bg-primary/50' : 'bg-border'}`} />}
            <div
              title={s.label}
              className={`flex items-center justify-center rounded-full text-[10px] font-semibold transition-all ${
                active
                  ? 'bg-primary text-primary-foreground ring-2 ring-primary/20 size-6 sm:size-6'
                  : done
                    ? 'bg-primary/20 text-primary size-5 sm:size-6'
                    : 'bg-muted text-muted-foreground size-5 sm:size-6'
              }`}
            >
              {done ? <Check className="size-3" aria-hidden /> : s.num}
            </div>
          </div>
        )
      })}
    </div>
  )
}

function WizardView() {
  const { step, isLoadingStatus, isSuccess } = useOwnerOnboardingWizard()

  if (isLoadingStatus) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-background">
        <div className="flex flex-col items-center gap-3">
          <Skeleton className="size-12 rounded-full" />
          <Skeleton className="h-4 w-48" />
        </div>
      </div>
    )
  }

  const title = isSuccess
    ? 'Boutique Atelier Ready'
    : step === 2
      ? 'Welcome to Aveline'
      : step === 3
        ? 'Tell Us About Your Boutique'
        : step === 4
          ? 'Select Your Atelier Plan'
          : step === 5
            ? 'Customize Your Concierge Intelligence'
            : step === 6
              ? 'Connect Your Channels'
              : step === 7
                ? 'Invite Your Team'
                : 'Review & Initialize Boutique'

  const subtitle = isSuccess
    ? 'Your boutique operations and AI intelligence agents have been initialized.'
    : step === 2
      ? 'Choose your role to configure your boutique workspace or join an existing atelier.'
      : step === 3
        ? 'Establish your boutique presence and identity across the concierge network.'
        : step === 4
          ? 'Experience Aveline risk-free in demo mode. No payment is required today.'
          : step === 5
            ? 'Help your AI agents understand your unique brand voice, policies, and customer etiquette.'
            : step === 6
              ? 'Optionally link WhatsApp, Instagram, and your payment gateway. Credentials stay encrypted.'
              : step === 7
                ? 'Bring your team aboard with an email invitation or a shareable code.'
                : 'Confirm your boutique parameters and initialize your customer intelligence agents.'

  // Renders the active wizard stage.
  const renderStep = () => {
    if (isSuccess) return <SuccessStep />
    switch (step) {
      case 2:
        return <AccountTypeStep />
      case 3:
        return <BoutiqueDetailsStep />
      case 4:
        return <PlanSelectionStep />
      case 5:
        return <AiCustomizationStep />
      case 6:
        return <IntegrationsStep />
      case 7:
        return <InviteStaffStep />
      case 8:
        return <ReviewStep />
      default:
        return null
    }
  }

  return (
    <div className="relative min-h-screen bg-background overflow-hidden">
      {/* Animated aurora background shared with the marketing site */}
      <AuroraField />
      <div
        aria-hidden
        className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_70%_60%_at_50%_0%,rgba(253,250,248,0.92)_0%,rgba(253,250,248,0.55)_55%,rgba(253,250,248,0)_100%)]"
      />

      <div className="relative z-10 flex flex-col justify-center items-center min-h-screen py-10 px-4 sm:px-6 lg:px-8">
        <div className="max-w-3xl w-full flex flex-col gap-6">
          {/* Atelier Brand Header */}
          <div className="text-center flex flex-col items-center gap-2">
            <span className="text-[11px] uppercase tracking-[0.3em] font-semibold text-muted-foreground flex items-center gap-1.5">
              <Crown className="size-3.5 text-primary" aria-hidden />
              Aveline Atelier Concierge
            </span>
            <h1 className="text-3xl sm:text-4xl font-serif font-medium text-foreground tracking-tight">
              {title}
            </h1>
            <p className="text-sm text-muted-foreground max-w-lg">{subtitle}</p>
          </div>

          {/* Progress Stepper Bar (Hidden on Success) */}
          {!isSuccess && <StepIndicator step={step} />}

          {renderStep()}
        </div>
      </div>
    </div>
  )
}

export function OwnerOnboardingWizard() {
  return (
    <OwnerOnboardingWizardProvider>
      <WizardView />
    </OwnerOnboardingWizardProvider>
  )
}
