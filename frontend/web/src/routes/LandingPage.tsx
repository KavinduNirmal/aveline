import { Link } from 'react-router-dom'
import {
  ArrowRight,
  Check,
  HeartHandshake,
  MessageCircle,
  ShieldCheck,
  Shirt,
  Sparkles,
  Wallet,
  type LucideIcon,
} from 'lucide-react'
import { motion, useReducedMotion } from 'motion/react'

import { Blossom } from '@/components/auth/Blossom'
import { AuroraField } from '@/components/site/AuroraField'
import { Reveal } from '@/components/site/Reveal'
import { SitePage } from '@/components/site/SitePage'
import { Button } from '@/components/ui/button'
import { PhoneMockup } from '@/components/site/PhoneMockup'
import { HeroSlideshow } from '@/components/site/HeroSlideshow'
import { SocialProofBar } from '@/components/site/SocialProofBar'
import { ProblemSection } from '@/components/site/ProblemSection'
import { TestimonialsSection } from '@/components/site/TestimonialsSection'
import { AppleIcon, CurvedArrow, PlayStoreIcon } from '@/components/site/Icons'
import { cn } from '@/lib/utils'

interface Feature {
  icon: LucideIcon
  title: string
  copy: string
  span: string
  tint: string
  ink: string
  chip: string
  wide?: boolean
}

const FEATURES: Feature[] = [
  {
    icon: Sparkles,
    title: 'Concierge memory',
    copy: 'Every preference, occasion and purchase remembered — so every message feels intimate and personal.',
    span: 'lg:col-span-3',
    tint: 'from-rose-50 via-[#fff1f4] to-white',
    ink: 'text-memory',
    chip: 'bg-memory/10 text-memory',
    wide: true,
  },
  {
    icon: MessageCircle,
    title: 'Orders & WhatsApp',
    copy: 'Inquiries and orders flow from WhatsApp and Instagram into one tidy internal workspace for your team.',
    span: 'lg:col-span-3',
    tint: 'from-amber-50 via-[#fdf6e7] to-white',
    ink: 'text-visual',
    chip: 'bg-visual/10 text-visual',
    wide: true,
  },
  {
    icon: ShieldCheck,
    title: 'Roles & approvals',
    copy: 'Owners set the guardrails. High-value calls and custom pricing pause for a human decision.',
    span: 'lg:col-span-2',
    tint: 'from-lavender-soft to-white',
    ink: 'text-lavender',
    chip: 'bg-lavender/10 text-lavender',
  },
  {
    icon: Shirt,
    title: 'Outfits & sourcing',
    copy: 'Reference images, matching stock, and supplier sourcing when the exact look isn’t on the rack.',
    span: 'lg:col-span-2',
    tint: 'from-rose-50 to-white',
    ink: 'text-coral',
    chip: 'bg-coral/10 text-coral',
  },
  {
    icon: Wallet,
    title: 'Payments & delivery',
    copy: 'Deposits, margins, and courier dispatches handled — with clean numbers the owner can trust.',
    span: 'lg:col-span-2',
    tint: 'from-amber-50 to-white',
    ink: 'text-commerce',
    chip: 'bg-commerce/10 text-commerce',
  },
  {
    icon: HeartHandshake,
    title: 'The human touch',
    copy: 'Aveline drafts and suggests; your boutique staff always have the final word with the customer.',
    span: 'lg:col-span-6',
    tint: 'from-[#f0eafa] via-[#f7f1fb] to-white',
    ink: 'text-commerce',
    chip: 'bg-commerce/10 text-commerce',
    wide: true,
  },
]

interface Agent {
  name: string
  tint: string
  ink: string
  chip: string
  tag: string
  line: string
  copy: string
  points: string[]
  preview: string
}

const AGENTS: Agent[] = [
  {
    name: 'Ava',
    tint: 'from-rose-100 via-[#ffe3ec] to-white',
    ink: 'text-memory',
    chip: 'bg-memory',
    tag: 'The memory agent',
    line: 'She remembers every customer.',
    copy: 'Parses messages, builds warm profiles and recalls each preference, occasion and past purchase.',
    points: ['Facts & preferences', 'Conversation briefs', 'Personal drafts'],
    preview: 'Hi Shanali — the blush dress you loved is back in your size.',
  },
  {
    name: 'Elle',
    tint: 'from-amber-100 via-[#fdf0d8] to-white',
    ink: 'text-visual',
    chip: 'bg-visual',
    tag: 'The visual agent',
    line: 'She sees what suits you.',
    copy: 'Reads a reference photo, searches the shelf and composes outfits that fit the moment.',
    points: ['Image understanding', 'Customer matching', 'Supplier sourcing'],
    preview: 'Matched three outfits for Saturday’s wedding.',
  },
  {
    name: 'Lina',
    tint: 'from-rose-200/70 via-[#ffe9ef] to-white',
    ink: 'text-commerce',
    chip: 'bg-commerce',
    tag: 'The commerce agent',
    line: 'She closes with care.',
    copy: 'Checks margins, issues deposits, and pauses big decisions for your approval before delivery.',
    points: ['Pricing & margins', 'Payments', 'Approvals & delivery'],
    preview: 'Deposit request sent — holding the order for your approval.',
  },
]

interface Step {
  n: string
  iconColor: string
  tint: string
  accent: string
  title: string
  copy: string
}

const STEPS: Step[] = [
  {
    n: '01',
    iconColor: 'text-memory',
    tint: 'from-[#ffe3ec] via-[#fce3ec] to-[#f0eafa]',
    accent: 'text-[#b0566b]',
    title: 'Create your account',
    copy: 'Owners open a boutique. Staff join with an invitation code.',
  },
  {
    n: '02',
    iconColor: 'text-visual',
    tint: 'from-[#fdeed2] via-[#f7e8c8] to-[#ffe9e2]',
    accent: 'text-[#8a6a14]',
    title: 'Connect your day',
    copy: 'Bring in WhatsApp, your catalogue and your boutique rules.',
  },
  {
    n: '03',
    iconColor: 'text-commerce',
    tint: 'from-[#ffe9ef] via-[#f8d7e0] to-[#ffe3d6]',
    accent: 'text-[#7a303f]',
    title: 'Let Aveline tend the details',
    copy: 'Ava, Elle and Lina keep clients, garments and deals moving — you approve the big calls.',
  },
]

function AgentWorkflow() {
  const reduce = useReducedMotion()

  const flows = [
    { key: 'ava', d: 'M450 0 L450 56 L150 56 L150 130', color: '#b0566b' },
    { key: 'elle', d: 'M450 0 L450 56 L450 130', color: '#8a6a14' },
    { key: 'lina', d: 'M450 0 L450 56 L750 56 L750 130', color: '#7a303f' },
  ]

  return (
    <div className="relative mx-auto mt-16 max-w-5xl">
      {/* Aveline hub with 8-petal counter-rotating blossom */}
      <div className="flex flex-col items-center">
        <p className="font-serif text-3xl font-medium text-neutral-900">Aveline</p>
        <div className="relative mt-3 flex size-24 items-center justify-center">
          <motion.div
            className="absolute -inset-4 rounded-full bg-gradient-to-br from-commerce via-memory to-visual opacity-20 blur-2xl"
            animate={reduce ? {} : { scale: [1, 1.12, 1], opacity: [0.16, 0.3, 0.16] }}
            transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
          />
          <motion.div
            className="absolute inset-0 rounded-full bg-[conic-gradient(from_0deg,#8b2e42,#b0566b,#c9972b,#8e7cc3,#ef7a68,#8b2e42)] p-[3px]"
            animate={reduce ? {} : { rotate: 360 }}
            transition={{ duration: 24, repeat: Infinity, ease: 'linear' }}
          >
            <div className="flex size-full items-center justify-center rounded-full bg-[#fdfaf8]">
              <Blossom
                animateCounter
                counterDuration={16}
                className="size-11 text-primary drop-shadow-[0_4px_16px_rgba(139,46,66,0.45)]"
              />
            </div>
          </motion.div>
        </div>
        <p className="mt-3 text-xs font-semibold uppercase tracking-[0.22em] text-neutral-400">
          powers the three
        </p>
      </div>

      {/* Orthogonal connector lines */}
      <svg
        aria-hidden
        viewBox="0 0 900 130"
        preserveAspectRatio="none"
        className="mx-auto -mt-1 h-28 w-full max-w-4xl overflow-visible"
      >
        {flows.map((f) => (
          <g key={f.key}>
            {/* base line */}
            <path
              d={f.d}
              fill="none"
              stroke="#e7dcdc"
              strokeWidth="2"
              strokeDasharray="2 8"
              strokeLinecap="round"
              strokeLinejoin="round"
            />
            {/* colored gradient line with flowing pulse */}
            <motion.path
              d={f.d}
              fill="none"
              stroke={f.color}
              strokeOpacity="0.9"
              strokeWidth="2.5"
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeDasharray="14 16"
              animate={reduce ? {} : { strokeDashoffset: [0, -30] }}
              transition={{ duration: 1.3, repeat: Infinity, ease: 'linear' }}
            />
            {/* traveling comet pulses */}
            {!reduce && (
              <motion.circle
                r="4.5"
                fill={f.color}
                style={{ offsetPath: `path('${f.d}')`, offsetDistance: '0%' }}
                animate={{ offsetDistance: ['0%', '100%'] }}
                transition={{ duration: 2.2, repeat: Infinity, ease: 'easeInOut' }}
              />
            )}
            {!reduce && (
              <motion.circle
                r="2.5"
                fill={f.color}
                opacity="0.6"
                style={{ offsetPath: `path('${f.d}')`, offsetDistance: '0%' }}
                animate={{ offsetDistance: ['0%', '100%'] }}
                transition={{ duration: 2.2, repeat: Infinity, ease: 'easeInOut', delay: 0.35 }}
              />
            )}
          </g>
        ))}
      </svg>

      {/* Agent cards with different colored blossoms */}
      <div className="grid gap-6 md:grid-cols-3">
        {AGENTS.map((agent, i) => (
          <motion.div
            key={agent.name}
            initial={{ opacity: 0, y: 28 }}
            whileInView={{ opacity: 1, y: 0 }}
            viewport={{ once: true }}
            transition={{ duration: 0.6, delay: 0.15 + i * 0.12, ease: [0.22, 1, 0.36, 1] }}
            className="group relative flex h-full flex-col overflow-hidden rounded-2xl border-2 border-dashed border-neutral-200/90 bg-white p-7 shadow-[0_28px_70px_-55px_rgba(139,46,66,0.4)] transition-all duration-300 hover:-translate-y-1 hover:border-primary/40 hover:shadow-[0_30px_75px_-45px_rgba(139,46,66,0.3)]"
          >
            <div
              className={cn('pointer-events-none absolute inset-0 bg-gradient-to-br opacity-70 transition-opacity duration-500 group-hover:opacity-100', agent.tint)}
              aria-hidden
            />
            <div className="relative flex h-full flex-col">
              <div className="flex items-center gap-3">
                <span
                  className={cn(
                    'flex size-12 items-center justify-center rounded-2xl text-white shadow-md transition-transform duration-300 group-hover:-translate-y-0.5',
                    agent.chip,
                  )}
                >
                  <Blossom className="size-6 text-white" />
                </span>
                <div>
                  <p className={cn('font-serif text-2xl font-medium leading-none', agent.ink)}>{agent.name}</p>
                  <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-neutral-400">
                    {agent.tag}
                  </p>
                </div>
              </div>

              <p className={cn('mt-5 font-serif text-xl font-medium leading-snug', agent.ink)}>
                {agent.line}
              </p>
              <p className="mt-2 text-sm leading-relaxed text-neutral-600">{agent.copy}</p>

              <div className="mt-auto flex flex-wrap gap-2 pt-5">
                {agent.points.map((point) => (
                  <span
                    key={point}
                    className="rounded-full border border-neutral-200 bg-white/70 px-3 py-1 text-xs font-medium text-neutral-700"
                  >
                    {point}
                  </span>
                ))}
              </div>
            </div>
          </motion.div>
        ))}
      </div>

      {/* Concept of Blossoms — Unique Editorial Presentation (Not a card) */}
      <motion.div
        initial={{ opacity: 0, y: 24 }}
        whileInView={{ opacity: 1, y: 0 }}
        viewport={{ once: true }}
        transition={{ duration: 0.7, delay: 0.2 }}
        className="relative mt-20 text-center"
      >
        {/* Soft ethereal radiant aura (no card bounding box) */}
        <div
          className="pointer-events-none absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 size-80 rounded-full bg-gradient-to-tr from-primary/10 via-rose-200/20 to-amber-100/10 blur-3xl"
          aria-hidden
        />

        {/* Delicate ornamental divider */}
        <div className="mx-auto mb-8 flex w-full max-w-xs items-center justify-center gap-3">
          <span className="h-px flex-1 bg-gradient-to-r from-transparent to-neutral-300" />
          <span className="size-1.5 rounded-full bg-primary/40" />
          <span className="h-px flex-1 bg-gradient-to-l from-transparent to-neutral-300" />
        </div>

        {/* Luminous counter-rotating Blossom medallion */}
        <div className="relative mx-auto flex size-16 items-center justify-center sm:size-20">
          <motion.div
            className="absolute inset-0 rounded-full bg-primary/10"
            animate={reduce ? {} : { scale: [1, 1.15, 1], opacity: [0.35, 0.65, 0.35] }}
            transition={{ duration: 4, repeat: Infinity, ease: 'easeInOut' }}
          />
          <div className="relative flex size-12 items-center justify-center rounded-full border border-primary/20 bg-white/90 shadow-2xs backdrop-blur-sm sm:size-14">
            <Blossom
              animateCounter
              counterDuration={14}
              className="size-7 text-primary sm:size-8"
            />
          </div>
        </div>

        {/* Editorial Narrative */}
        <div className="relative mx-auto mt-5 max-w-2xl px-4">
          <p className="text-xs font-semibold uppercase tracking-[0.22em] text-primary">
            The Concept of{' '}
            <span className="bg-gradient-to-r from-primary via-[#b0566b] to-amber-700 bg-clip-text text-transparent">
              Blossoms
            </span>
          </p>
          <h3 className="mt-3 font-serif text-3xl font-medium tracking-tight text-neutral-900 sm:text-4xl">
            Every agentic action powered by{' '}
            <span className="bg-gradient-to-r from-primary via-[#b0566b] to-amber-700 bg-clip-text text-transparent italic">
              Blossoms
            </span>
            .
          </h3>
          <p className="mx-auto mt-4 max-w-xl text-base leading-relaxed text-neutral-600">
            Aveline operates on{' '}
            <span className="bg-gradient-to-r from-primary via-[#b0566b] to-amber-700 bg-clip-text text-transparent font-semibold">
              Blossoms
            </span>
            —universal credits consumed only when genuine concierge work is performed.
            No opaque seat charges or mystery token bills; your atelier only expends energy when intelligence is delivered.
          </p>

          <div className="mt-6 flex items-center justify-center">
            <Link
              to="/plans"
              className="group inline-flex items-center gap-2.5 rounded-full border border-neutral-300/80 bg-white/80 px-6 py-2.5 text-sm font-medium text-neutral-800 shadow-2xs backdrop-blur-md transition-all hover:border-primary/50 hover:bg-white hover:text-primary hover:shadow-xs"
            >
              <span>Check pricing for detailed information</span>
              <CurvedArrow className="text-primary" />
            </Link>
          </div>
        </div>
      </motion.div>

      <p className="mt-10 text-center text-xs font-semibold uppercase tracking-[0.22em] text-neutral-400">
        One Aveline · three specialists · one workflow
      </p>
    </div>
  )
}

const QUERIES = [
  'Wedding on Saturday — anything blush in my size?',
  'Can you reserve the Cinnamon Linen suit for Friday?',
  'Hold the gold filigree earrings until tomorrow afternoon.',
  'Found three cocktail gowns matching her requested Ceylon palette.',
  'VIP deposit terms prepared — awaiting 1-tap sign-off.',
  'Margin confirmed at 42% for bespoke couture order.',
  'Does the sapphire evening gown run true to UK 10?',
  'Customer sent Pinterest image — identified matching raw silk stock.',
]

function MessageMarquee() {
  const items = [...QUERIES, ...QUERIES]
  return (
    <div className="relative overflow-hidden py-4">
      <div className="marquee-track flex w-max items-center gap-10">
        {items.map((q, i) => (
          <span key={i} className="flex shrink-0 items-center gap-3 text-sm text-neutral-700 sm:text-base">
            <Blossom animateCounter counterDuration={12} className="size-4 text-primary/70" />
            <span className="font-serif italic">“{q}”</span>
          </span>
        ))}
      </div>
    </div>
  )
}

export function LandingPage() {
  const reduceMotion = useReducedMotion()

  return (
    <SitePage>
      {/* ------------------------------------------------ 1. Hero Section */}
      <section className="relative overflow-hidden">
        <AuroraField />
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_75%_65%_at_35%_25%,rgba(253,250,248,0.96)_0%,rgba(253,250,248,0.65)_50%,rgba(253,250,248,0)_85%)]"
        />
        <div className="relative z-10 mx-auto w-full max-w-6xl px-5 pb-20 pt-16 lg:px-8 lg:pt-24">
          <div className="grid items-center gap-14 lg:grid-cols-[1.1fr_0.9fr]">
            {/* Left Copy & Primary CTAs */}
            <div className="text-center lg:text-left">
              <Reveal>
                <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-primary/30 bg-white/80 px-4 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-primary shadow-xs backdrop-blur-md">
                  <Sparkles className="size-3.5" aria-hidden />
                  The Atelier Assistant That Remembers
                </span>
              </Reveal>

              <Reveal delay={0.08}>
                <h1 className="mt-6 max-w-xl font-serif text-5xl font-medium leading-[1.06] tracking-tight text-neutral-900 sm:text-6xl lg:text-7xl [text-shadow:0_2px_34px_rgba(255,255,255,0.6)]">
                  She remembers, so{' '}
                  <span className="bg-gradient-to-r from-primary via-[#b0566b] to-amber-700 bg-clip-text text-transparent">
                    your staff don’t have to.
                  </span>
                </h1>
              </Reveal>

              <Reveal delay={0.16}>
                <p className="mx-auto mt-6 max-w-xl text-lg leading-relaxed text-neutral-600 lg:mx-0">
                  Aveline is the internal concierge workspace for luxury boutique staff. When
                  patrons inquiry on WhatsApp or Instagram, Ava, Elle, and Lina recall past tastes,
                  curate matching garments, and draft deposits — waiting for your one-tap sign-off.
                </p>
              </Reveal>

              <Reveal delay={0.24}>
                <div className="mt-8 flex flex-wrap items-center justify-center gap-3 lg:justify-start">
                  <Button
                    asChild
                    size="lg"
                    className="group h-12 rounded-full bg-primary px-8 text-white shadow-sm transition-all hover:bg-primary/90 hover:shadow-md"
                  >
                    <Link to="/sign-up" className="inline-flex items-center gap-2.5">
                      <span>Create boutique account</span>
                      <CurvedArrow className="text-white" />
                    </Link>
                  </Button>
                  <Button
                    asChild
                    size="lg"
                    variant="outline"
                    className="group h-12 rounded-full border-neutral-300 bg-white/80 px-6 shadow-xs backdrop-blur-md transition-all hover:border-neutral-400 hover:bg-white hover:shadow-sm"
                  >
                    <Link to="/download" className="inline-flex items-center gap-2.5">
                      <span className="flex items-center gap-1.5 text-neutral-600 transition-colors group-hover:text-neutral-900">
                        <AppleIcon className="size-4" aria-hidden="true" />
                        <PlayStoreIcon className="size-3.5" />
                      </span>
                      <span>Download staff app</span>
                    </Link>
                  </Button>
                </div>
              </Reveal>

              <Reveal delay={0.32}>
                <p className="mt-8 text-xs uppercase tracking-[0.22em] text-neutral-400">
                  Staff-facing workspace · Zero unreviewed automated messages · Clerk-secured
                </p>
              </Reveal>
            </div>

            {/* Right: Interactive Animated Phone Mockup */}
            <Reveal delay={0.18}>
              <PhoneMockup />
            </Reveal>
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ 2. Sample Messages Carousel */}
      <section className="border-y-2 border-dashed border-neutral-200 bg-white/70 backdrop-blur-md">
        <div className="mx-auto w-full max-w-6xl px-5 py-4 lg:px-8">
          <MessageMarquee />
        </div>
      </section>

      {/* ------------------------------------------------ 3. Image Slideshow with Aveline Messages */}
      <section className="relative overflow-hidden py-20 sm:py-24 bg-white/50">
        <div className="mx-auto w-full max-w-6xl px-5 lg:px-8">
          <Reveal className="mx-auto max-w-2xl text-center">
            <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-primary/30 bg-primary/5 px-4 py-1.5 text-xs font-semibold uppercase tracking-[0.2em] text-primary">
              <Blossom animateCounter counterDuration={15} className="size-3.5 text-primary" />
              Ateliers In Motion
            </span>
            <h2 className="mt-4 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
              Inside Sri Lanka’s premier boutiques.
            </h2>
            <p className="mt-4 text-base text-neutral-600 sm:text-lg">
              See how Ava, Elle, and Lina quietly assist floor associates across bespoke fitting rooms,
              silk showrooms, and ready-to-wear ateliers.
            </p>
          </Reveal>

          <Reveal delay={0.12} className="mt-12">
            <HeroSlideshow />
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ 4. Trusted Shops with Bigger Statistics */}
      <SocialProofBar />

      {/* ------------------------------------------------ 5. Problem Section (2 by 2 Whimsical Cards) */}
      <ProblemSection />

      {/* ------------------------------------------------ Persona Quote */}
      <section className="relative overflow-hidden border-y-2 border-dashed border-neutral-200 bg-white">
        <AuroraField className="opacity-25" />
        <div className="relative mx-auto grid w-full max-w-6xl items-center gap-12 px-5 py-24 lg:grid-cols-[1.15fr_0.85fr] lg:px-8">
          <Reveal>
            <p className="font-serif text-4xl font-medium italic leading-snug text-neutral-800 sm:text-5xl">
              “She remembers, so you don’t have to — every client, every detail,
              warmly.”
            </p>
            <p className="mt-6 text-base text-neutral-500">
              Aveline isn’t a robotic bot that chats with your customers directly. She’s three
              specialists working quietly behind your staff floor team.
            </p>
          </Reveal>

          <Reveal delay={0.1}>
            <div className="flex flex-col divide-y-2 divide-dashed divide-neutral-200">
              {AGENTS.map((agent) => (
                <div key={agent.name} className="flex items-start gap-4 py-5">
                  <span className={cn('mt-1 size-5 shrink-0')}>
                    <Blossom className={cn('size-5', agent.ink)} />
                  </span>
                  <div>
                    <p className="font-serif text-xl font-medium text-neutral-900">
                      {agent.name} — <span className="italic">{agent.tag}</span>
                    </p>
                    <p className="mt-1 text-sm text-neutral-600">{agent.line}</p>
                  </div>
                </div>
              ))}
            </div>
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ Three of her — diagram */}
      <section className="relative overflow-hidden border-b-2 border-dashed border-neutral-200 bg-white">
        <AuroraField className="opacity-40" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="mx-auto max-w-2xl text-center">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-primary">
              Meet the three of her · Powered by Blossoms
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              One Aveline, three minds.
            </h2>
            <p className="mt-4 text-lg text-neutral-600">
              A specialist for the customer, the garment and the deal — powered by transparent
              Blossom credits for every agentic action.
            </p>
          </Reveal>

          <AgentWorkflow />

          <Reveal delay={0.15}>
            <p className="mx-auto mt-14 max-w-2xl text-center text-[15px] leading-relaxed text-neutral-400">
              Every high-impact action pauses for human approval — your owner,
              your call. Aveline completes the rest.
            </p>
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ Atelier Workspace Features */}
      <section id="features" className="relative overflow-hidden scroll-mt-20">
        <AuroraField className="opacity-55" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-primary">
              Atelier Workspace Features
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              Everything your boutique does, in one calm place.
            </h2>
          </Reveal>

          <div className="mt-16 grid gap-6 sm:grid-cols-2 lg:grid-cols-6">
            {FEATURES.map(({ icon: Icon, title, copy, span, tint, ink, chip, wide }, i) => (
              <Reveal key={title} delay={i * 0.05} className={cn(span, 'h-full')}>
                <div className="group relative h-full overflow-hidden rounded-2xl border-2 border-dashed border-neutral-200/90 bg-white p-8 shadow-[0_24px_60px_-50px_rgba(139,46,66,0.35)] transition-all duration-300 hover:-translate-y-1 hover:border-primary/40 hover:shadow-[0_30px_70px_-40px_rgba(139,46,66,0.25)]">
                  <div className={cn('pointer-events-none absolute inset-0 bg-gradient-to-br opacity-70', tint)} aria-hidden />
                  {wide && (
                    <Blossom
                      animateCounter
                      counterDuration={20}
                      className="absolute -right-8 -top-8 size-36 text-neutral-900/[0.04] transition-transform duration-500 group-hover:scale-110"
                    />
                  )}
                  <div className="relative flex h-full flex-col">
                    <span className={cn('flex size-12 items-center justify-center rounded-2xl transition-transform duration-300 group-hover:scale-110', chip)}>
                      <Icon className="size-6" aria-hidden />
                    </span>
                    <h3 className={cn('mt-6 font-serif text-3xl font-medium text-neutral-900', wide && 'sm:text-4xl')}>
                      {title}
                    </h3>
                    <p className={cn('mt-3 text-base leading-relaxed text-neutral-600', wide && 'max-w-xl')}>{copy}</p>
                    {wide && (
                      <p className={cn('mt-6 text-xs font-semibold uppercase tracking-[0.18em]', ink)}>
                        Always on · always personal
                      </p>
                    )}
                  </div>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Testimonials Section */}
      <TestimonialsSection />

      {/* ------------------------------------------------ How it works (Aligned Track) */}
      <section className="relative overflow-hidden border-y-2 border-dashed border-neutral-200 bg-white">
        <AuroraField className="opacity-40" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="mx-auto max-w-2xl text-center">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-primary">
              How it works
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              From first message to final delivery.
            </h2>
            <p className="mt-4 text-lg text-neutral-600">
              Three steps. One assistant doing the remembering in between.
            </p>
          </Reveal>

          <div className="relative mt-20">
            {/* Perfectly aligned journey track passing directly through medallion centers (top: 86px) */}
            <div className="pointer-events-none absolute left-[16%] right-[16%] top-[80px] hidden h-[12px] overflow-hidden rounded-full lg:block">
              <div className="absolute left-0 right-0 top-[5px] border-t-2 border-dashed border-neutral-300/80" />
              {!reduceMotion && (
                <motion.div
                  className="absolute top-[5px] h-[2.5px] w-64 rounded-full bg-gradient-to-r from-transparent via-[#b0566b]/35 via-[#c9972b]/65 to-[#8b2e42]"
                  animate={{ left: ['-40%', '100%'] }}
                  transition={{ duration: 4.8, repeat: Infinity, ease: 'easeInOut' }}
                >
                  <span className="absolute right-0 top-1/2 size-1.5 -translate-y-1/2 rounded-full bg-[#8b2e42] shadow-[0_0_6px_rgba(139,46,66,0.9)]" />
                </motion.div>
              )}
            </div>

            <div className="grid gap-12 md:grid-cols-3">
              {STEPS.map((step, i) => (
                <motion.div
                  key={step.n}
                  initial={{ opacity: 0, y: 30 }}
                  whileInView={{ opacity: 1, y: 0 }}
                  viewport={{ once: true, margin: '-70px' }}
                  transition={{ duration: 0.7, delay: i * 0.16, ease: [0.22, 1, 0.36, 1] }}
                  className="relative flex flex-col items-center text-center"
                >
                  <span className="rounded-full border border-neutral-200 bg-white px-3.5 py-1.5 text-[11px] font-semibold uppercase tracking-[0.18em] text-neutral-500 shadow-xs">
                    Step {step.n}
                  </span>

                  <div className="relative z-10 mt-5 flex size-20 items-center justify-center">
                    <span
                      className={cn(
                        'absolute inset-0 rounded-full bg-gradient-to-br opacity-70',
                        step.tint,
                      )}
                    />
                    <span className="absolute inset-0 rounded-full border border-white/70" />
                    <motion.span
                      animate={reduceMotion ? {} : { scale: [1, 1.08, 1] }}
                      transition={{ duration: 4 + i, repeat: Infinity, ease: 'easeInOut', delay: i * 0.6 }}
                      className={cn('relative flex size-14 items-center justify-center rounded-full bg-white/80 shadow-xs', step.accent)}
                    >
                      <Blossom className={cn('size-7', step.iconColor)} />
                    </motion.span>
                  </div>

                  <h3 className="mt-6 font-serif text-2xl font-medium text-neutral-900">
                    {step.title}
                  </h3>
                  <p className="mt-2 max-w-xs text-[15px] leading-relaxed text-neutral-600">
                    {step.copy}
                  </p>
                </motion.div>
              ))}
            </div>

            {/* Journey ribbon */}
            <Reveal delay={0.1}>
              <div className="mx-auto mt-16 flex w-fit flex-wrap items-center justify-center gap-x-3 gap-y-3 rounded-full border-2 border-dashed border-neutral-200 bg-white/70 px-7 py-3.5 backdrop-blur-md">
                {['Customer messages', 'Ava · Elle · Lina draft', 'Staff approves & sends'].map(
                  (label, i, arr) => (
                    <span key={label} className="flex items-center gap-3">
                      <span className="text-sm font-medium text-neutral-700">{label}</span>
                      {i < arr.length - 1 && (
                        <ArrowRight className="size-4 text-primary/70" aria-hidden />
                      )}
                    </span>
                  ),
                )}
              </div>
            </Reveal>
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Enterprise */}
      <section className="px-5 py-20 lg:px-8">
        <Reveal className="mx-auto max-w-6xl">
          <div className="relative overflow-hidden rounded-2xl border-2 border-dashed border-lavender/40 bg-lavender-soft/80 shadow-[0_40px_90px_-60px_rgba(142,124,195,0.5)]">
            <AuroraField className="opacity-35" />
            <div className="relative flex flex-col items-start justify-between gap-8 p-10 lg:flex-row lg:items-center lg:p-14">
              <div className="max-w-xl">
                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-lavender">
                  Aveline for boutique groups
                </p>
                <h2 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900">
                  Running several ateliers?
                </h2>
                <p className="mt-3 text-base leading-relaxed text-neutral-600">
                  Multi-store customer memory, unified inventory matching, and owner-level approvals.
                  Let’s configure Aveline around your locations.
                </p>
                <ul className="mt-6 grid gap-3 sm:grid-cols-2">
                  {['One view across every atelier', 'Shared VIP customer memory', 'Owner-level margin sign-off', 'Priority boutique onboarding'].map((perk) => (
                    <li key={perk} className="flex items-center gap-2.5 text-sm font-medium text-neutral-700">
                      <span className="flex size-5 items-center justify-center rounded-full bg-lavender/20 text-lavender">
                        <Check className="size-3.5" aria-hidden />
                      </span>
                      {perk}
                    </li>
                  ))}
                </ul>
              </div>
              <div className="flex flex-wrap gap-3">
                <Button
                  asChild
                  size="lg"
                  className="group h-12 rounded-full bg-primary px-6 text-white shadow-sm transition-all hover:bg-primary/90"
                >
                  <Link to="/contact" className="inline-flex items-center gap-2.5">
                    <span>Contact atelier sales</span>
                    <CurvedArrow className="text-white" />
                  </Link>
                </Button>
                <Button
                  asChild
                  size="lg"
                  variant="outline"
                  className="group h-12 rounded-full border-neutral-300 bg-white/80 px-6 backdrop-blur-md transition-all hover:bg-white"
                >
                  <Link to="/plans" className="inline-flex items-center gap-2.5">
                    <span>View plans</span>
                    <CurvedArrow className="text-neutral-600 transition-colors group-hover:text-neutral-900" />
                  </Link>
                </Button>
              </div>
            </div>
          </div>
        </Reveal>
      </section>

      {/* ------------------------------------------------ Final CTA */}
      <section className="relative overflow-hidden px-5 pb-28 lg:px-8">
        <AuroraField />
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_60%_70%_at_50%_50%,rgba(253,250,248,0.85)_0%,rgba(253,250,248,0)_70%)]"
        />
        <Reveal className="relative z-10 mx-auto max-w-2xl text-center">
          <p className="font-serif text-2xl italic text-memory">Ava · Elle · Lina</p>
          <h2 className="mt-4 font-serif text-5xl font-medium leading-tight tracking-tight text-neutral-900 [text-shadow:0_2px_30px_rgba(255,255,255,0.5)]">
            Ready to give your boutique memory?
          </h2>
          <p className="mx-auto mt-4 max-w-md text-lg text-neutral-600">
            Join the premier ateliers letting Ava, Elle and Lina tend the details while you stay in control.
          </p>
          <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
            <Button
              asChild
              size="lg"
              className="group h-12 rounded-full bg-primary px-8 text-white shadow-sm transition-all hover:bg-primary/90 hover:shadow-md"
            >
              <Link to="/sign-up" className="inline-flex items-center gap-2.5">
                <span>Create account</span>
                <CurvedArrow className="text-white" />
              </Link>
            </Button>
            <Button
              asChild
              size="lg"
              variant="outline"
              className="group h-12 rounded-full border-neutral-300 bg-white/80 px-6 shadow-xs backdrop-blur-md transition-all hover:border-neutral-400 hover:bg-white hover:shadow-sm"
            >
              <Link to="/download" className="inline-flex items-center gap-2.5">
                <span className="flex items-center gap-1.5 text-neutral-600 transition-colors group-hover:text-neutral-900">
                  <AppleIcon className="size-4" aria-hidden="true" />
                  <PlayStoreIcon className="size-3.5" />
                </span>
                <span>Download staff app</span>
              </Link>
            </Button>
          </div>
          <p className="mt-6 text-xs uppercase tracking-[0.2em] text-neutral-400">
            No credit card required · Set up in minutes · Clerk-secured
          </p>
        </Reveal>
      </section>
    </SitePage>
  )
}
