import { UserX, Clock, Search, TrendingDown, AlertCircle } from 'lucide-react'
import { Blossom } from '@/components/auth/Blossom'
import { Reveal } from '@/components/site/Reveal'
import { cn } from '@/lib/utils'

interface ProblemCard {
  icon: typeof UserX
  tag: string
  title: string
  copy: string
  scenario: string
  tint: string
  border: string
  tagColor: string
}

const PROBLEMS: ProblemCard[] = [
  {
    icon: UserX,
    tag: 'Memory Gap',
    title: 'The Forgotten Preference',
    copy: 'A high-value VIP messages on WhatsApp asking for weekend wear. A junior associate forgets she refuses synthetic blends and prefers tea length.',
    scenario: 'Result: Off-tone recommendations erode years of cultivated personal trust.',
    tint: 'bg-gradient-to-b from-[#fff2f5] to-white',
    border: 'border-rose-200/80',
    tagColor: 'bg-memory/10 text-memory',
  },
  {
    icon: Clock,
    tag: 'Lost Momentum',
    title: 'The 3 AM WhatsApp Drift',
    copy: 'High-intent inquiries arrive late at night or during peak dressing room rushes. By the time staff replies the next morning, the moment has passed.',
    scenario: 'Result: 64% of luxury impulsive shoppers purchase elsewhere within 4 hours.',
    tint: 'bg-gradient-to-b from-[#fef8ed] to-white',
    border: 'border-amber-200/80',
    tagColor: 'bg-visual/10 text-visual',
  },
  {
    icon: Search,
    tag: 'Lost Hours',
    title: 'The Sourcing Black Hole',
    copy: 'A patron sends a Pinterest screenshot or asks for an outfit matching an heirloom necklace. Staff spend 45 minutes searching storage shelves.',
    scenario: 'Result: Valuable floor styling time lost to manual stock searching.',
    tint: 'bg-gradient-to-b from-[#f6f2fd] to-white',
    border: 'border-purple-200/80',
    tagColor: 'bg-lavender/10 text-lavender',
  },
  {
    icon: TrendingDown,
    tag: 'Revenue Leak',
    title: 'The Unapproved Margin Leak',
    copy: 'Associates offer ad-hoc discounts, hold items without non-refundable deposits, or promise unverified delivery deadlines to close chats.',
    scenario: 'Result: Costly checkout surprises and disputed bottom lines for the owner.',
    tint: 'bg-gradient-to-b from-[#fff1f3] to-white',
    border: 'border-primary/20',
    tagColor: 'bg-commerce/10 text-commerce',
  },
]

export function ProblemSection() {
  return (
    <section className="relative overflow-hidden py-24 sm:py-28">
      <div className="relative mx-auto w-full max-w-6xl px-5 lg:px-8">
        {/* Section Header */}
        <div className="mx-auto max-w-2xl text-center">
          <Reveal>
            <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-primary/30 bg-primary/5 px-4 py-1.5 text-xs font-semibold uppercase tracking-[0.2em] text-primary">
              <AlertCircle className="size-3.5" />
              The Boutique Dilemma
            </span>
          </Reveal>

          <Reveal delay={0.08}>
            <h2 className="mt-5 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl lg:text-6xl">
              Your customers are{' '}
              <span className="bg-gradient-to-r from-primary via-[#b0566b] to-amber-700 bg-clip-text text-transparent italic">
                slipping through the cracks.
              </span>
            </h2>
          </Reveal>

          <Reveal delay={0.16}>
            <p className="mt-5 text-base leading-relaxed text-neutral-600 sm:text-lg">
              Luxury boutique retail is built on intimacy and recognition. But when customer
              messages scatter across personal staff WhatsApps and Instagram DMs, even the most
              dedicated associates lose track.
            </p>
          </Reveal>
        </div>

        {/* Whimsical Problem Cards Grid: 2 by 2 layout with rounded-2xl and larger typography */}
        <div className="mt-16 grid gap-8 md:grid-cols-2">
          {PROBLEMS.map((prob, i) => {
            const Icon = prob.icon
            return (
              <Reveal key={prob.title} delay={0.1 + i * 0.08} className="h-full">
                <div
                  className={cn(
                    'group relative flex h-full flex-col overflow-hidden rounded-2xl border-2 border-dashed p-8 shadow-[0_20px_50px_-30px_rgba(139,46,66,0.12)] transition-all duration-300 hover:-translate-y-1.5 hover:shadow-[0_25px_60px_-25px_rgba(139,46,66,0.22)]',
                    prob.border,
                    prob.tint
                  )}
                >
                  {/* Decorative faint background blossom */}
                  <Blossom
                    className="pointer-events-none absolute -bottom-6 -right-6 size-36 text-neutral-900/[0.03] transition-transform duration-500 group-hover:rotate-45"
                  />

                  {/* Top Bar: Icon + Category Tag */}
                  <div className="flex items-center justify-between">
                    <span className="flex size-12 items-center justify-center rounded-xl bg-white shadow-xs border border-neutral-200/60">
                      <Icon className="size-6 text-neutral-800" />
                    </span>
                    <span className={cn('rounded-full px-3 py-1 text-xs font-semibold', prob.tagColor)}>
                      {prob.tag}
                    </span>
                  </div>

                  {/* Card Title & Content */}
                  <h3 className="mt-6 font-serif text-2xl sm:text-3xl font-medium text-neutral-900 leading-snug">
                    {prob.title}
                  </h3>

                  <p className="mt-3.5 text-sm sm:text-base leading-relaxed text-neutral-600">
                    {prob.copy}
                  </p>

                  {/* Consequence Pill */}
                  <div className="mt-auto pt-6">
                    <div className="rounded-xl border border-neutral-200/80 bg-white/85 p-4 text-xs sm:text-sm leading-relaxed text-neutral-800 shadow-2xs">
                      {prob.scenario}
                    </div>
                  </div>
                </div>
              </Reveal>
            )
          })}
        </div>
      </div>
    </section>
  )
}
