import { createContext, useCallback, useContext, useEffect, useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'

import { useUserContext } from '@/contexts/UserContext'
import {
  ADDRESS_MAX,
  BOUTIQUE_DESCRIPTION_MAX,
  BOUTIQUE_NAME_MAX,
  isValidLkPhone,
} from '@/lib/boutique'
import type { PlanTier } from '@/lib/onboarding'
import {
  completeOnboarding,
  fetchOnboardingStatus,
  saveAiCustomization,
  saveBoutiqueDetails,
  selectPlan,
} from '@/lib/onboarding'
import { acceptInvitation } from '@/lib/organizations'

export type AccountType = 'owner' | 'staff'

export interface WizardDraft {
  accountType: AccountType
  inviteCode: string
  organizationId: string
  boutiqueName: string
  address: string
  phoneNumber: string
  description: string
  logoUrl: string
  selectedPlanTier: PlanTier
  brandVoice: string
  businessRules: string
  preferredColorsFabrics: string
  customerPreferences: string
}

export const DEFAULT_DRAFT: WizardDraft = {
  accountType: 'owner',
  inviteCode: '',
  organizationId: '',
  boutiqueName: '',
  address: '',
  phoneNumber: '+94 ',
  description: '',
  logoUrl: '',
  selectedPlanTier: 'Bloom',
  brandVoice: 'Poised, discreet, and warmly attentive',
  businessRules:
    'Max discount 15%. Return window within 14 days with tags intact. Complimentary islandwide courier on orders exceeding LKR 25,000.',
  preferredColorsFabrics:
    'Pure mulberry silk, Italian linen, hand-loomed cotton, cashmere, bridal ivory, and jewel tones.',
  customerPreferences:
    'Welcome client with Ceylon silver tips tea. Address by formal title. Note styling sizing and anniversary dates.',
}

interface OwnerOnboardingWizardContextValue {
  /** Wizard step label number (2 = Role, 3 = Details, 4 = Plan, 5 = Context, 6 = Integrations, 7 = Team, 8 = Launch). */
  step: number
  goTo: (step: number) => void
  draft: WizardDraft
  patch: (partial: Partial<WizardDraft>) => void
  isLoadingStatus: boolean
  isSubmitting: boolean
  isSuccess: boolean
  completedAllocation: number
  handleStaffJoin: () => Promise<void>
  handleSaveBoutiqueDetails: () => Promise<void>
  handleSelectPlan: () => Promise<void>
  handleSaveAiContext: () => Promise<void>
  handleCompleteOnboarding: () => Promise<void>
  handleEnterDashboard: () => void
}

const OwnerOnboardingWizardContext = createContext<OwnerOnboardingWizardContextValue | null>(null)

export function useOwnerOnboardingWizard(): OwnerOnboardingWizardContextValue {
  const ctx = useContext(OwnerOnboardingWizardContext)
  if (!ctx) {
    throw new Error('useOwnerOnboardingWizard must be used within OwnerOnboardingWizardProvider')
  }
  return ctx
}

export function OwnerOnboardingWizardProvider({ children }: { children: React.ReactNode }) {
  const { refreshUser } = useUserContext()
  const navigate = useNavigate()

  const [step, setStep] = useState<number>(2)
  const [draft, setDraft] = useState<WizardDraft>(DEFAULT_DRAFT)

  const [isLoadingStatus, setIsLoadingStatus] = useState(true)
  const [isSubmitting, setIsSubmitting] = useState(false)
  const [isSuccess, setIsSuccess] = useState(false)
  const [completedAllocation, setCompletedAllocation] = useState<number>(750)

  const goTo = useCallback((next: number) => {
    setStep(next)
  }, [])

  const patch = useCallback((partial: Partial<WizardDraft>) => {
    setDraft((prev) => ({ ...prev, ...partial }))
  }, [])

  // Hydrate from any existing onboarding progress on mount.
  useEffect(() => {
    let mounted = true
    async function init() {
      try {
        const res = await fetchOnboardingStatus()
        if (!mounted) return
        if (res.hasCompletedOnboarding) {
          navigate('/app', { replace: true })
          return
        }
        if (res.organization) {
          const org = res.organization
          patch({
            organizationId: org.id,
            boutiqueName: org.name || '',
            address: org.address || '',
            phoneNumber: org.phoneNumber || '+94 ',
            description: org.description || '',
            logoUrl: org.logoUrl || '',
            selectedPlanTier: org.planTier || 'Bloom',
            brandVoice: org.brandVoice || DEFAULT_DRAFT.brandVoice,
            businessRules: org.businessRules || DEFAULT_DRAFT.businessRules,
            preferredColorsFabrics:
              org.preferredColorsFabrics || DEFAULT_DRAFT.preferredColorsFabrics,
            customerPreferences:
              org.customerPreferences || DEFAULT_DRAFT.customerPreferences,
          })
          setStep(Math.max(2, res.currentStep))
        }
      } catch {
        // First time or fresh session - default to step 2.
      } finally {
        if (mounted) setIsLoadingStatus(false)
      }
    }
    void init()
    return () => {
      mounted = false
    }
  }, [navigate, patch])

  const handleStaffJoin = useCallback(async () => {
    const code = draft.inviteCode.trim()
    if (!code) {
      toast.error('Please enter the invitation code provided by your boutique owner.')
      return
    }
    try {
      setIsSubmitting(true)
      await acceptInvitation(code)
      await refreshUser()
      navigate('/app', { replace: true })
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Unable to accept invitation code.')
    } finally {
      setIsSubmitting(false)
    }
  }, [draft.inviteCode, navigate, refreshUser])

  const handleSaveBoutiqueDetails = useCallback(async () => {
    const name = draft.boutiqueName.trim()
    const address = draft.address.trim()
    const description = draft.description.trim()

    if (!name) {
      toast.error('Please enter your boutique name.')
      return
    }
    if (name.length > BOUTIQUE_NAME_MAX) {
      toast.error(`Boutique name must be ${BOUTIQUE_NAME_MAX} characters or fewer.`)
      return
    }
    if (!address) {
      toast.error('Please enter your boutique physical address.')
      return
    }
    if (address.length > ADDRESS_MAX) {
      toast.error(`Physical address must be ${ADDRESS_MAX} characters or fewer.`)
      return
    }
    if (!isValidLkPhone(draft.phoneNumber)) {
      toast.error('Enter a valid Sri Lankan phone number, e.g. +94 77 12 12 123 (9 digits).')
      return
    }
    if (description.length > BOUTIQUE_DESCRIPTION_MAX) {
      toast.error(`Boutique description must be ${BOUTIQUE_DESCRIPTION_MAX} characters or fewer.`)
      return
    }
    try {
      setIsSubmitting(true)
      const saved = await saveBoutiqueDetails({
        name,
        address,
        phoneNumber: draft.phoneNumber.trim(),
        description: description || undefined,
        logoUrl: draft.logoUrl.trim() || undefined,
      })
      patch({ organizationId: saved.id })
      setStep(4)
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Failed to save boutique details.')
    } finally {
      setIsSubmitting(false)
    }
  }, [draft])

  const handleSelectPlan = useCallback(async () => {
    try {
      setIsSubmitting(true)
      await selectPlan(draft.selectedPlanTier)
      setStep(5)
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Failed to select plan.')
    } finally {
      setIsSubmitting(false)
    }
  }, [draft.selectedPlanTier])

  const handleSaveAiContext = useCallback(async () => {
    try {
      setIsSubmitting(true)
      // Only send fields permitted by the selected tier.
      const payload =
        draft.selectedPlanTier === 'Seed'
          ? {}
          : draft.selectedPlanTier === 'Bloom'
            ? {
                brandVoice: draft.brandVoice.trim() || undefined,
                businessRules: draft.businessRules.trim() || undefined,
              }
            : {
                brandVoice: draft.brandVoice.trim() || undefined,
                businessRules: draft.businessRules.trim() || undefined,
                preferredColorsFabrics: draft.preferredColorsFabrics.trim() || undefined,
                customerPreferences: draft.customerPreferences.trim() || undefined,
              }
      await saveAiCustomization(payload)
      setStep(6)
    } catch (err: unknown) {
      toast.error(err instanceof Error ? err.message : 'Failed to save AI customization.')
    } finally {
      setIsSubmitting(false)
    }
  }, [draft])

  const handleCompleteOnboarding = useCallback(async () => {
    try {
      setIsSubmitting(true)
      const res = await completeOnboarding()
      setCompletedAllocation(res.blossomAllocation)
      setIsSuccess(true)
      toast.success(`Boutique created with ${res.blossomAllocation.toLocaleString()} Blossoms.`)
      await refreshUser()
    } catch (err: unknown) {
      toast.error(
        err instanceof Error ? err.message : 'Failed to finalize boutique onboarding.',
      )
    } finally {
      setIsSubmitting(false)
    }
  }, [refreshUser])

  const handleEnterDashboard = useCallback(() => {
    navigate('/app', { replace: true })
  }, [navigate])

  const value = useMemo<OwnerOnboardingWizardContextValue>(
    () => ({
      step,
      goTo,
      draft,
      patch,
      isLoadingStatus,
      isSubmitting,
      isSuccess,
      completedAllocation,
      handleStaffJoin,
      handleSaveBoutiqueDetails,
      handleSelectPlan,
      handleSaveAiContext,
      handleCompleteOnboarding,
      handleEnterDashboard,
    }),
    [
      step,
      goTo,
      draft,
      patch,
      isLoadingStatus,
      isSubmitting,
      isSuccess,
      completedAllocation,
      handleStaffJoin,
      handleSaveBoutiqueDetails,
      handleSelectPlan,
      handleSaveAiContext,
      handleCompleteOnboarding,
      handleEnterDashboard,
    ],
  )

  // Clerk user is not part of the context value (avoided to limit tree re-renders).

  return (
    <OwnerOnboardingWizardContext.Provider value={value}>
      {children}
    </OwnerOnboardingWizardContext.Provider>
  )
}
