import { Link } from 'react-router-dom'
import { ArrowRight, Check } from 'lucide-react'

import { Reveal } from '@/components/site/Reveal'
import { SitePage } from '@/components/site/SitePage'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const TIERS = [
  {
    name: 'Starter',
    price: 'Free',
    cadence: 'while you explore',
    blurb: 'For a solo boutique getting started.',
    cta: 'Create account',
    to: '/sign-up',
    features: ['One boutique', 'Concierge memory', 'Orders & WhatsApp', 'Basic approvals'],
  },
  {
    name: 'Boutique',
    price: '$29',
    cadence: '/ month',
    blurb: 'For boutiques ready to feel effortless.',
    cta: 'Create account',
    to: '/sign-up',
    highlighted: true,
    features: [
      'Everything in Starter',
      'Outfits & sourcing (Elle)',
      'Full Ava memory & briefs',
      'Owner approvals on web',
      'Staff access on mobile',
    ],
  },
  {
    name: 'Atelier',
    price: 'Let’s talk',
    cadence: 'custom',
    blurb: 'For groups and multi-store boutiques.',
    cta: 'Contact sales',
    to: '/contact',
    features: [
      'Multiple boutiques',
      'Consolidated approvals',
      'Priority support',
      'Onboarding & training',
    ],
  },
]

export function PlansPage() {
  return (
    <SitePage>
      <section className="mx-auto w-full max-w-6xl px-5 py-20 lg:px-8">
        <Reveal className="max-w-2xl">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
            Plans
          </p>
          <h1 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
            Simple plans, personal care.
          </h1>
          <p className="mt-4 text-lg text-neutral-500">
            Start free. Upgrade when your boutique is ready to feel effortless.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-6 lg:grid-cols-3">
          {TIERS.map((tier, i) => (
            <Reveal key={tier.name} delay={i * 0.07}>
              <div
                className={cn(
                  'relative flex h-full flex-col rounded-3xl border-2 border-dashed p-8',
                  tier.highlighted
                    ? 'border-commerce bg-commerce/[0.04] shadow-[0_30px_80px_-40px_rgba(122,48,63,0.5)]'
                    : 'border-neutral-200 bg-white',
                )}
              >
                {tier.highlighted && (
                  <span className="absolute -top-3 left-1/2 -translate-x-1/2 rounded-full bg-commerce px-3 py-1 text-xs font-semibold text-white">
                    Most loved
                  </span>
                )}
                <h2 className="font-serif text-2xl font-medium text-neutral-900">{tier.name}</h2>
                <div className="mt-4 flex items-baseline gap-2">
                  <span className="font-serif text-4xl font-medium text-neutral-900">
                    {tier.price}
                  </span>
                  <span className="text-sm text-neutral-400">{tier.cadence}</span>
                </div>
                <p className="mt-3 text-sm text-neutral-500">{tier.blurb}</p>

                <ul className="mt-7 flex-1 space-y-2.5 border-t-2 border-dashed border-neutral-200 pt-6">
                  {tier.features.map((feature) => (
                    <li key={feature} className="flex items-start gap-2.5 text-sm text-neutral-700">
                      <Check
                        className={cn(
                          'mt-0.5 size-4 shrink-0',
                          tier.highlighted ? 'text-commerce' : 'text-neutral-400',
                        )}
                        aria-hidden
                      />
                      {feature}
                    </li>
                  ))}
                </ul>

                <Button
                  asChild
                  size="lg"
                  className="mt-8 h-11 w-full"
                  variant={tier.highlighted ? 'default' : 'outline'}
                >
                  <Link to={tier.to}>
                    {tier.cta}
                    <ArrowRight className="size-4" aria-hidden />
                  </Link>
                </Button>
              </div>
            </Reveal>
          ))}
        </div>

        <Reveal delay={0.1}>
          <p className="mt-12 text-center text-sm text-neutral-400">
            Plans shown are placeholders while pricing is finalised. Questions?{' '}
            <Link to="/contact" className="text-commerce hover:underline">
              Contact us
            </Link>
            .
          </p>
        </Reveal>
      </section>
    </SitePage>
  )
}
