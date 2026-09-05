import { Link } from 'react-router-dom'
import {
  ArrowRight,
  Coins,
  Eye,
  Heart,
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
import { cn } from '@/lib/utils'

const FEATURES = [
  {
    icon: Sparkles,
    title: 'Concierge memory',
    copy: 'Every preference, occasion and purchase remembered — so every message feels personal.',
  },
  {
    icon: MessageCircle,
    title: 'Orders & WhatsApp',
    copy: 'Inquiries and orders flow from WhatsApp and Instagram into one tidy workspace.',
  },
  {
    icon: ShieldCheck,
    title: 'Roles & approvals',
    copy: 'Owners set the rules. High-value calls pause for a human decision, never slip past.',
  },
  {
    icon: Shirt,
    title: 'Outfits & sourcing',
    copy: 'Reference images, matching stock, and supplier sourcing when it isn’t on the shelf.',
  },
  {
    icon: Wallet,
    title: 'Payments & delivery',
    copy: 'Deposits, margins and couriers handled — with clean numbers the owner can trust.',
  },
  {
    icon: HeartHandshake,
    title: 'The human touch',
    copy: 'Aveline drafts and suggests; your staff always have the final word with the customer.',
  },
]

interface Agent {
  name: string
  icon: LucideIcon
  tint: string
  ink: string
  chip: string
  tag: string
  line: string
  copy: string
  points: string[]
}

const AGENTS: Agent[] = [
  {
    name: 'Ava',
    icon: Heart,
    tint: 'from-rose-100/90 via-[#ffe3ec] to-white',
    ink: 'text-memory',
    chip: 'bg-memory',
    tag: 'The memory agent',
    line: 'She remembers every customer.',
    copy: 'Parses messages, builds warm profiles and recalls each preference, occasion and past purchase.',
    points: ['Facts & preferences', 'Conversation briefs', 'Personal drafts'],
  },
  {
    name: 'Elle',
    icon: Eye,
    tint: 'from-amber-100/90 via-[#fdf0d8] to-white',
    ink: 'text-visual',
    chip: 'bg-visual',
    tag: 'The visual agent',
    line: 'She sees what suits you.',
    copy: 'Reads a reference photo, searches the shelf and composes outfits that fit the moment.',
    points: ['Image understanding', 'Customer matching', 'Supplier sourcing'],
  },
  {
    name: 'Lina',
    icon: Coins,
    tint: 'from-rose-200/70 via-[#ffe9ef] to-white',
    ink: 'text-commerce',
    chip: 'bg-commerce',
    tag: 'The commerce agent',
    line: 'She closes with care.',
    copy: 'Checks margins, issues deposits, and pauses big decisions for your approval before delivery.',
    points: ['Pricing & margins', 'Payments', 'Approvals & delivery'],
  },
]

interface Step {
  n: string
  icon: LucideIcon
  tint: string
  title: string
  copy: string
}

const STEPS: Step[] = [
  {
    n: '01',
    icon: Heart,
    tint: 'from-[#ffe3ec] via-[#fce3ec] to-[#f0eafa]',
    title: 'Create your account',
    copy: 'Owners open a boutique. Staff join with an invitation code.',
  },
  {
    n: '02',
    icon: Eye,
    tint: 'from-[#fdeed2] via-[#f7e8c8] to-[#ffe9e2]',
    title: 'Connect your day',
    copy: 'Bring in WhatsApp, your catalogue and your business rules.',
  },
  {
    n: '03',
    icon: Coins,
    tint: 'from-[#ffe9ef] via-[#f8d7e0] to-[#ffe3d6]',
    title: 'Let Aveline tend the details',
    copy: 'Ava, Elle and Lina keep customers, products and deals moving — you approve the big calls.',
  },
]

function StepArt({ step, index }: { step: Step; index: number }) {
  const reduce = useReducedMotion()
  const Icon = step.icon
  return (
    <div
      className={cn(
        'relative aspect-[5/4] overflow-hidden rounded-3xl border-2 border-dashed border-neutral-200 bg-gradient-to-br',
        step.tint,
      )}
    >
      <motion.div
        className="absolute -right-6 -top-8 text-rose-200/60"
        animate={reduce ? {} : { y: [0, -12, 0], rotate: [0, 12, 0] }}
        transition={{ duration: 8 + index * 2, repeat: Infinity, ease: 'easeInOut' }}
      >
        <Blossom className="size-40" />
      </motion.div>
      <motion.div
        className="absolute -bottom-8 -left-6 text-amber-200/70"
        animate={reduce ? {} : { y: [0, 10, 0], rotate: [0, -14, 0] }}
        transition={{ duration: 9 + index * 2, repeat: Infinity, ease: 'easeInOut', delay: 1 }}
      >
        <Blossom className="size-32" />
      </motion.div>

      <span className="absolute left-5 top-5 font-serif text-6xl font-medium text-white/80 drop-shadow-sm">
        {step.n}
      </span>

      <div className="absolute bottom-5 right-5 flex size-16 items-center justify-center rounded-full bg-white/85 text-commerce shadow-sm backdrop-blur">
        <Icon className="size-7" aria-hidden />
      </div>
    </div>
  )
}

export function LandingPage() {
  return (
    <SitePage>
      {/* ------------------------------------------------ Hero */}
      <section className="relative overflow-hidden">
        <AuroraField />
        <div className="relative mx-auto flex w-full max-w-5xl flex-col items-center px-5 pb-24 pt-20 text-center lg:px-8 lg:pt-28">
          <Reveal>
            <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-commerce/25 bg-white/70 px-4 py-2 text-xs font-medium uppercase tracking-[0.18em] text-commerce backdrop-blur">
              <Sparkles className="size-3.5" aria-hidden />
              Aveline — the assistant that remembers
            </span>
          </Reveal>

          <Reveal delay={0.08}>
            <h1 className="mt-8 max-w-3xl font-serif text-5xl font-medium leading-[1.06] tracking-tight text-neutral-900 sm:text-6xl lg:text-7xl">
              A boutique’s memory,{' '}
              <span className="bg-gradient-to-r from-commerce via-memory to-visual bg-clip-text text-transparent">
                made personal.
              </span>
            </h1>
          </Reveal>

          <Reveal delay={0.16}>
            <p className="mt-6 max-w-xl text-lg leading-relaxed text-neutral-500">
              Aveline remembers every customer, every product and every deal — so
              you can stay close and personal without worrying about the little
              things.
            </p>
          </Reveal>

          <Reveal delay={0.24}>
            <div className="mt-9 flex flex-wrap items-center justify-center gap-3">
              <Button asChild size="lg" className="h-12 px-8">
                <Link to="/sign-up">
                  Create account
                  <ArrowRight className="size-4" aria-hidden />
                </Link>
              </Button>
              <Button asChild size="lg" variant="outline" className="h-12 border-neutral-300 bg-white/80 px-8 backdrop-blur">
                <Link to="/download">Download app</Link>
              </Button>
            </div>
          </Reveal>

          <Reveal delay={0.32}>
            <p className="mt-8 text-xs uppercase tracking-[0.22em] text-neutral-400">
              For boutique owners, managers &amp; staff · Sri Lanka · Clerk-secured
            </p>
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ Persona */}
      <section className="relative border-y-2 border-dashed border-neutral-200 bg-white/80 backdrop-blur">
        <Reveal className="mx-auto max-w-3xl px-5 py-24 text-center lg:px-8">
          <p className="font-serif text-4xl font-medium italic leading-snug text-neutral-800 sm:text-5xl">
            “She remembers, so you don’t have to — every client, every detail,
            warmly.”
          </p>
          <p className="mt-6 text-base text-neutral-400">
            Meet the three who keep your boutique moving.
          </p>
        </Reveal>
      </section>

      {/* ------------------------------------------------ Features */}
      <section id="features" className="relative overflow-hidden scroll-mt-20">
        <AuroraField className="opacity-60" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              Features
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              Everything your boutique does, in one calm place.
            </h2>
          </Reveal>

          <div className="mt-16 grid gap-x-10 gap-y-12 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map(({ icon: Icon, title, copy }, i) => (
              <Reveal key={title} delay={i * 0.05}>
                <div className="group relative">
                  <span className="flex size-12 items-center justify-center rounded-full bg-commerce/10 text-commerce transition-all duration-300 group-hover:scale-110 group-hover:bg-commerce group-hover:text-white">
                    <Icon className="size-6" aria-hidden />
                  </span>
                  <h3 className="mt-6 font-serif text-3xl font-medium text-neutral-900">
                    {title}
                  </h3>
                  <p className="mt-3 max-w-xs text-base leading-relaxed text-neutral-500">
                    {copy}
                  </p>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Three agents — joined */}
      <section className="relative overflow-hidden border-y-2 border-dashed border-neutral-200 bg-white">
        <AuroraField className="opacity-45" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="mx-auto max-w-2xl text-center">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              The three of her
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              Ava, Elle &amp; Lina.
            </h2>
            <p className="mt-4 text-lg text-neutral-500">
              Three specialists, one Aveline — working together behind every
              conversation.
            </p>
          </Reveal>

          {/* One joined panel: outer corners rounded only */}
          <Reveal delay={0.08} className="mt-16">
            <div className="overflow-hidden rounded-[2.5rem] border-2 border-dashed border-neutral-200 bg-white shadow-[0_40px_90px_-60px_rgba(122,48,63,0.35)]">
              <div className="grid lg:grid-cols-3">
                {AGENTS.map((agent, i) => {
                  const Icon = agent.icon
                  return (
                    <motion.div
                      key={agent.name}
                      initial={{ opacity: 0, y: 24 }}
                      whileInView={{ opacity: 1, y: 0 }}
                      viewport={{ once: true }}
                      transition={{ duration: 0.6, delay: 0.1 + i * 0.12, ease: [0.22, 1, 0.36, 1] }}
                      className={cn(
                        'group relative p-10 lg:p-12',
                        'border-t-2 border-dashed border-neutral-200 first:border-t-0',
                        'lg:border-l-2 lg:border-t-0 lg:first:border-l-0',
                      )}
                    >
                      <div
                        className={cn(
                          'pointer-events-none absolute inset-0 bg-gradient-to-br opacity-70 transition-opacity duration-500 group-hover:opacity-100',
                          agent.tint,
                        )}
                        aria-hidden
                      />
                      <div className="relative flex h-full flex-col">
                        <span
                          className={cn(
                            'flex size-14 items-center justify-center rounded-2xl text-white shadow-md transition-transform duration-300 group-hover:-translate-y-1 group-hover:rotate-3',
                            agent.chip,
                          )}
                        >
                          <Icon className="size-7" aria-hidden />
                        </span>
                        <p className={cn('mt-7 font-serif text-3xl font-medium lg:text-4xl', agent.ink)}>
                          {agent.name}
                        </p>
                        <p className="mt-1 text-xs font-semibold uppercase tracking-[0.18em] text-neutral-400">
                          {agent.tag}
                        </p>
                        <p className={cn('mt-5 font-serif text-2xl font-medium leading-snug', agent.ink)}>
                          {agent.line}
                        </p>
                        <p className="mt-3 text-base leading-relaxed text-neutral-500">{agent.copy}</p>

                        <ul className="mt-8 space-y-3">
                          {agent.points.map((point) => (
                            <li key={point} className="flex items-center gap-3 text-[15px] font-medium text-neutral-700">
                              <span className={cn('size-2 rounded-full', agent.chip)} />
                              {point}
                            </li>
                          ))}
                        </ul>
                      </div>
                    </motion.div>
                  )
                })}
              </div>
            </div>
          </Reveal>

          <Reveal delay={0.15}>
            <p className="mx-auto mt-12 max-w-2xl text-center text-[15px] leading-relaxed text-neutral-400">
              Every high-impact action pauses for human approval — your owner,
              your call. Aveline completes the rest.
            </p>
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ How it works */}
      <section className="relative overflow-hidden">
        <AuroraField className="opacity-55" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              How it works
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              From first message to final delivery.
            </h2>
          </Reveal>

          <div className="mt-16 grid gap-8 md:grid-cols-3">
            {STEPS.map((step, i) => (
              <motion.div
                key={step.n}
                initial={{ opacity: 0, y: 34 }}
                whileInView={{ opacity: 1, y: 0 }}
                viewport={{ once: true, margin: '-60px' }}
                transition={{ duration: 0.7, delay: i * 0.14, ease: [0.22, 1, 0.36, 1] }}
              >
                <StepArt step={step} index={i} />
                <h3 className="mt-6 font-serif text-2xl font-medium text-neutral-900">
                  {step.title}
                </h3>
                <p className="mt-2 text-[15px] leading-relaxed text-neutral-500">{step.copy}</p>
              </motion.div>
            ))}
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Enterprise */}
      <section className="px-5 lg:px-8">
        <Reveal className="mx-auto max-w-6xl">
          <div className="relative overflow-hidden rounded-[2.5rem] border-2 border-dashed border-lavender/40 bg-lavender-soft/70">
            <AuroraField className="opacity-40" />
            <div className="relative flex flex-col items-start justify-between gap-6 p-10 lg:flex-row lg:items-center lg:p-14">
              <div className="max-w-xl">
                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-lavender">
                  Aveline for groups
                </p>
                <h2 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900">
                  Running several boutiques?
                </h2>
                <p className="mt-3 text-base leading-relaxed text-neutral-600">
                  Multi-store rules, consolidated approvals and dedicated support.
                  Let’s shape a plan around your ateliers.
                </p>
              </div>
              <div className="flex flex-wrap gap-3">
                <Button asChild size="lg" className="h-12 px-6">
                  <Link to="/contact">
                    Contact sales
                    <ArrowRight className="size-4" aria-hidden />
                  </Link>
                </Button>
                <Button asChild size="lg" variant="outline" className="h-12 border-neutral-300 bg-white/80 px-6">
                  <Link to="/plans">See plans</Link>
                </Button>
              </div>
            </div>
          </div>
        </Reveal>
      </section>

      {/* ------------------------------------------------ Final CTA */}
      <section className="relative overflow-hidden px-5 pb-28 pt-24 lg:px-8">
        <AuroraField />
        <Reveal className="relative mx-auto max-w-2xl text-center">
          <h2 className="font-serif text-5xl font-medium leading-tight tracking-tight text-neutral-900">
            Ready to be remembered?
          </h2>
          <p className="mx-auto mt-4 max-w-md text-lg text-neutral-500">
            Join the boutiques letting Ava, Elle and Lina tend the details.
          </p>
          <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
            <Button asChild size="lg" className="h-12 px-8">
              <Link to="/sign-up">Create account</Link>
            </Button>
            <Button asChild size="lg" variant="outline" className="h-12 border-neutral-300 bg-white/80 px-8">
              <Link to="/download">Download app</Link>
            </Button>
          </div>
        </Reveal>
      </section>
    </SitePage>
  )
}
