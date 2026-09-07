import type { PlanTier } from '@/lib/onboarding'

export interface PlanInfo {
  tier: PlanTier
  name: string
  price: string
  blossoms: number
  staff: number
  customers: number
  tagline: string
  badge?: string
  color: string
}

export const PLANS: PlanInfo[] = [
  {
    tier: 'Seed',
    name: 'Seed',
    price: 'Free',
    blossoms: 150,
    staff: 1,
    customers: 50,
    tagline: 'Essential boutique AI foundation.',
    color: 'border-border hover:border-emerald-300',
  },
  {
    tier: 'Bloom',
    name: 'Bloom',
    price: 'LKR 3,500/mo',
    blossoms: 750,
    staff: 3,
    customers: 250,
    tagline: 'Daily boutique operations & WhatsApp.',
    badge: 'Atelier Choice',
    color: 'border-primary/40 bg-primary/5 hover:border-primary',
  },
  {
    tier: 'Orchid',
    name: 'Orchid',
    price: 'LKR 9,000/mo',
    blossoms: 2000,
    staff: 10,
    customers: 1000,
    tagline: 'Bespoke AI tuning & deep memory.',
    badge: 'Scale',
    color: 'border-border hover:border-purple-300',
  },
  {
    tier: 'Rose',
    name: 'Rose',
    price: 'LKR 20,000/mo',
    blossoms: 5000,
    staff: 25,
    customers: 5000,
    tagline: 'Multi-branch & API intelligence.',
    color: 'border-border hover:border-amber-300',
  },
]

/** Formatted monthly Blossom allowance for a plan tier, e.g. "750 Blossoms/mo". */
export function planBlossomLabel(tier: PlanTier): string {
  const blossoms = PLANS.find((p) => p.tier === tier)?.blossoms ?? 150
  return `${blossoms.toLocaleString()} Blossoms/mo`
}

/** Monthly Blossom credit value for a plan tier. */
export function planBlossoms(tier: PlanTier): number {
  return PLANS.find((p) => p.tier === tier)?.blossoms ?? 150
}
