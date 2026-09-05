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
    tint: 'from-rose-100 via-[#ffe3ec] to-white',
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
    tint: 'from-amber-100 via-[#fdf0d8] to-white',
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

const LINKS = [
  { d: 'M600 8 C 430 22 320 46 210 118', color: '#b0566b' },
  { d: 'M600 8 C 600 40 600 78 600 118', color: '#8a6a14' },
  { d: 'M600 8 C 770 22 880 46 990 118', color: '#7a303f' },
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

function StepImage({ step, index }: { step: Step; index: number }) {
  const reduce = useReducedMotion()
  const Icon = step.icon
  return (
    <div className={cn('relative h-44 overflow-hidden bg-gradient-to-br', step.tint)}>
      <motion.div
        className="absolute -right-8 -top-10 text-rose-200/70"
        animate={reduce ? {} : { y: [0, -12, 0], rotate: [0, 12, 0] }}
        transition={{ duration: 8 + index * 2, repeat: Infinity, ease: 'easeInOut' }}
      >
        <Blossom className="size-40" />
      </motion.div>
      <motion.div
        className="absolute -bottom-10 -left-7 text-amber-200/80"
        animate={reduce ? {} : { y: [0, 10, 0], rotate: [0, -14, 0] }}
        transition={{ duration: 9 + index * 2, repeat: Infinity, ease: 'easeInOut', delay: 1 }}
      >
        <Blossom className="size-32" />
      </motion.div>

      <span className="absolute left-6 top-4 font-serif text-6xl font-medium text-white/90 drop-shadow-sm">
        {step.n}
      </span>

      <div className="absolute bottom-4 right-5 flex size-14 items-center justify-center rounded-2xl bg-white/90 text-commerce shadow-sm">
        <Icon className="size-6" aria-hidden />
      </div>
    </div>
  )
}

/** Animated hub + connectors showing Aveline branching into the three agents. */
function AgentDiagram({ reduce }: { reduce: boolean | null }) {
  return (
    <div className="relative mx-auto mt-16 max-w-5xl">
      {/* Hub */}
      <Reveal>
        <div className="flex flex-col items-center">
          <div className="relative flex size-32 items-center justify-center">
            <motion.div
              className="absolute inset-0 rounded-full bg-gradient-to-br from-commerce via-memory to-visual opacity-20 blur-2xl"
              animate={reduce ? {} : { scale: [1, 1.12, 1], opacity: [0.18, 0.32, 0.18] }}
              transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
            />
            <motion.div
              className="absolute inset-0 rounded-full bg-[conic-gradient(from_0deg,#7a303f,#b0566b,#8a6a14,#8e7cc3,#ef7a68,#7a303f)] p-[3px]"
              animate={reduce ? {} : { rotate: 360 }}
              transition={{ duration: 26, repeat: Infinity, ease: 'linear' }}
            >
              <div className="flex size-full items-center justify-center rounded-full bg-[#fdfaf8]">
                <motion.div
                  animate={reduce ? {} : { scale: [1, 1.06, 1] }}
                  transition={{ duration: 4, repeat: Infinity, ease: 'easeInOut' }}
                >
                  <Blossom className="size-14 text-commerce drop-shadow-[0_4px_16px_rgba(122,48,63,0.45)]" />
                </motion.div>
              </div>
            </motion.div>
          </div>
          <p className="mt-4 font-serif text-2xl font-medium text-neutral-900">Aveline</p>
          <p className="mt-1 text-xs font-semibold uppercase tracking-[0.2em] text-neutral-400">
            One concierge
          </p>
        </div>
      </Reveal>

      {/* Connectors */}
      <svg
        aria-hidden
        viewBox="0 0 1200 130"
        className="mx-auto mt-3 h-28 w-full max-w-4xl overflow-visible"
      >
        {LINKS.map((link, i) => (
          <motion.path
            key={i}
            d={link.d}
            fill="none"
            stroke={link.color}
            strokeOpacity="0.5"
            strokeWidth="2.5"
            strokeLinecap="round"
            strokeDasharray="7 8"
            animate={reduce ? {} : { strokeDashoffset: [0, -15] }}
            transition={{ duration: 1.4 + i * 0.3, repeat: Infinity, ease: 'linear' }}
          />
        ))}
      </svg>

      {/* Agent cards */}
      <div className="grid gap-6 md:grid-cols-3">
        {AGENTS.map((agent, i) => {
          const Icon = agent.icon
          return (
            <motion.div
              key={agent.name}
              initial={{ opacity: 0, y: 28 }}
              whileInView={{ opacity: 1, y: 0 }}
              viewport={{ once: true }}
              transition={{ duration: 0.6, delay: 0.15 + i * 0.12, ease: [0.22, 1, 0.36, 1] }}
              className="group relative flex h-full flex-col overflow-hidden rounded-2xl border border-neutral-200 bg-white p-8 shadow-[0_28px_70px_-55px_rgba(122,48,63,0.5)] transition-transform duration-300 hover:-translate-y-1"
            >
              <div
                className={cn(
                  'pointer-events-none absolute inset-0 bg-gradient-to-br opacity-70 transition-opacity duration-500 group-hover:opacity-100',
                  agent.tint,
                )}
                aria-hidden
              />
              <div className="relative flex h-full flex-col">
                <div className="flex items-center gap-3">
                  <span
                    className={cn(
                      'flex size-12 items-center justify-center rounded-2xl text-white shadow-md transition-transform duration-300 group-hover:-translate-y-0.5 group-hover:rotate-3',
                      agent.chip,
                    )}
                  >
                    <Icon className="size-6" aria-hidden />
                  </span>
                  <div>
                    <p className={cn('font-serif text-2xl font-medium leading-none', agent.ink)}>
                      {agent.name}
                    </p>
                    <p className="mt-1 text-[11px] font-semibold uppercase tracking-[0.16em] text-neutral-400">
                      {agent.tag}
                    </p>
                  </div>
                </div>

                <p className={cn('mt-5 font-serif text-xl font-medium leading-snug', agent.ink)}>
                  {agent.line}
                </p>
                <p className="mt-2 text-sm leading-relaxed text-neutral-600">{agent.copy}</p>

                <ul className="mt-6 space-y-2.5 border-t-2 border-dashed border-neutral-200 pt-5">
                  {agent.points.map((point) => (
                    <li key={point} className="flex items-center gap-2.5 text-sm font-medium text-neutral-700">
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
  )
}

const QUERIES = [
  'Wedding on Saturday — anything blush?',
  'Can you get the dress I saw on Pinterest?',
  'Reserve the fourth one for me.',
  'Do you have it in my size?',
  'Is the navy in stock?',
  'Hold the gold earrings until Friday.',
  'Do you have something bluish for an evening?',
  'Anything new for my sister’s engagement?',
]

function ConversationPreview() {
  const reduce = useReducedMotion()
  return (
    <div className="relative mx-auto w-full max-w-sm">
      <div className="absolute -inset-6 -z-10 rounded-[2rem] bg-gradient-to-br from-commerce/20 via-memory/15 to-visual/20 blur-2xl" aria-hidden />
      <motion.div
        className="absolute -right-6 -top-8 -z-10 text-rose-200/70"
        animate={reduce ? {} : { y: [0, -10, 0], rotate: [0, 10, 0] }}
        transition={{ duration: 7, repeat: Infinity, ease: 'easeInOut' }}
      >
        <Blossom className="size-28" />
      </motion.div>

      <motion.div
        animate={reduce ? {} : { y: [0, -8, 0] }}
        transition={{ duration: 6, repeat: Infinity, ease: 'easeInOut' }}
        className="relative overflow-hidden rounded-[1.75rem] border border-neutral-200 bg-white/95 p-5 shadow-[0_40px_90px_-50px_rgba(122,48,63,0.6)] backdrop-blur"
      >
        {/* header */}
        <div className="flex items-center justify-between border-b border-dashed border-neutral-200 pb-3">
          <div className="flex items-center gap-3">
            <span className="relative flex size-10 items-center justify-center rounded-full bg-commerce text-white">
              <Blossom className="size-5" />
              <span className="absolute -bottom-0.5 -right-0.5 size-3 rounded-full border-2 border-white bg-emerald-500" />
            </span>
            <div>
              <p className="text-sm font-semibold text-neutral-900">Aveline</p>
              <p className="text-xs text-neutral-600">Concierge · online</p>
            </div>
          </div>
          <span className="text-xs text-neutral-400">now</span>
        </div>

        {/* thread */}
        <div className="space-y-3 pt-4">
          <div className="flex justify-end">
            <p className="max-w-[80%] rounded-2xl rounded-br-md bg-neutral-100 px-3.5 py-2 text-sm text-neutral-800">
              Hi! I have a wedding on Saturday — do you have anything blush in my size?
            </p>
          </div>
          <div className="flex justify-start">
            <p className="max-w-[85%] rounded-2xl rounded-bl-md bg-commerce px-3.5 py-2 text-sm text-white">
              Found three. Elle picked the one that matches your earrings — shall I reserve it?
            </p>
          </div>

          <div className="flex flex-wrap gap-1.5 pt-1">
            {[
              ['Ava', 'bg-memory'],
              ['Elle', 'bg-visual'],
              ['Lina', 'bg-commerce'],
            ].map(([label, chip]) => (
              <span
                key={label}
                className={cn('rounded-full px-2.5 py-1 text-[11px] font-semibold text-white', chip)}
              >
                {label}
              </span>
            ))}
          </div>

          {/* typing indicator */}
          <div className="flex w-fit items-center gap-1 rounded-2xl rounded-bl-md bg-neutral-100 px-3 py-2.5">
            {[0, 1, 2].map((i) => (
              <motion.span
                key={i}
                className="size-1.5 rounded-full bg-neutral-400"
                animate={reduce ? {} : { opacity: [0.3, 1, 0.3] }}
                transition={{ duration: 1.1, repeat: Infinity, delay: i * 0.2 }}
              />
            ))}
          </div>
        </div>
      </motion.div>
    </div>
  )
}

function MessageMarquee() {
  const items = [...QUERIES, ...QUERIES]
  return (
    <div className="relative overflow-hidden">
      <div className="marquee-track flex w-max items-center gap-10">
        {items.map((q, i) => (
          <span key={i} className="flex shrink-0 items-center gap-3 text-lg text-neutral-700">
            <Blossom className="size-4 text-commerce/50" />
            <span className="font-serif italic">“{q}”</span>
          </span>
        ))}
      </div>
    </div>
  )
}

export function LandingPage() {
  const reduce = useReducedMotion()

  return (
    <SitePage>
      {/* ------------------------------------------------ Hero */}
      <section className="relative overflow-hidden">
        <AuroraField />
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_70%_60%_at_38%_32%,rgba(253,250,248,0.94)_0%,rgba(253,250,248,0.6)_52%,rgba(253,250,248,0)_80%)]"
        />
        <div className="relative z-10 mx-auto w-full max-w-6xl px-5 pb-24 pt-16 lg:px-8 lg:pt-24">
          <div className="grid items-center gap-14 lg:grid-cols-[1.05fr_0.95fr]">
            <div className="text-center lg:text-left">
              <Reveal>
                <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-commerce/30 bg-white/80 px-4 py-2 text-xs font-semibold uppercase tracking-[0.18em] text-commerce shadow-sm backdrop-blur">
                  <Sparkles className="size-3.5" aria-hidden />
                  The assistant that remembers
                </span>
              </Reveal>

              <Reveal delay={0.08}>
                <h1 className="mt-7 max-w-xl font-serif text-5xl font-medium leading-[1.05] tracking-tight text-neutral-900 sm:text-6xl lg:text-7xl [text-shadow:0_2px_34px_rgba(255,255,255,0.5)]">
                  She remembers, so{' '}
                  <span className="bg-gradient-to-r from-[#7a303f] via-[#b0566b] to-[#8a6a14] bg-clip-text text-transparent">
                    you don’t have to.
                  </span>
                </h1>
              </Reveal>

              <Reveal delay={0.16}>
                <p className="mx-auto mt-6 max-w-xl text-lg leading-relaxed text-neutral-600 lg:mx-0">
                  Aveline keeps every customer, product and deal in mind — and
                  nudges you only when a decision is truly yours to make.
                </p>
              </Reveal>

              <Reveal delay={0.24}>
                <div className="mt-9 flex flex-wrap items-center justify-center gap-3 lg:justify-start">
                  <Button asChild size="lg" className="h-12 px-8">
                    <Link to="/sign-up">
                      Create account
                      <ArrowRight className="size-4" aria-hidden />
                    </Link>
                  </Button>
                  <Button asChild size="lg" variant="outline" className="h-12 border-neutral-300 bg-white/80 px-8 shadow-sm backdrop-blur">
                    <Link to="/download">Download app</Link>
                  </Button>
                </div>
              </Reveal>

              <Reveal delay={0.32}>
                <p className="mt-8 text-xs uppercase tracking-[0.22em] text-neutral-400">
                  For boutiques in Colombo &amp; across Sri Lanka · Clerk-secured
                </p>
              </Reveal>
            </div>

            <Reveal delay={0.18}>
              <ConversationPreview />
            </Reveal>
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Marquee */}
      <section className="border-y-2 border-dashed border-neutral-200 bg-white/70 backdrop-blur">
        <div className="mx-auto w-full max-w-6xl px-5 py-7 lg:px-8">
          <MessageMarquee />
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
        <AuroraField className="opacity-55" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              Features
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              Everything your boutique does, in one calm place.
            </h2>
          </Reveal>

          <div className="mt-16 grid gap-6 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map(({ icon: Icon, title, copy }, i) => (
              <Reveal key={title} delay={i * 0.05} className="h-full">
                <div className="group h-full rounded-2xl border border-neutral-200 bg-white/85 p-8 shadow-[0_24px_60px_-50px_rgba(122,48,63,0.45)] backdrop-blur transition-transform duration-300 hover:-translate-y-1">
                  <span className="flex size-12 items-center justify-center rounded-2xl bg-commerce/10 text-commerce transition-all duration-300 group-hover:scale-110 group-hover:bg-commerce group-hover:text-white">
                    <Icon className="size-6" aria-hidden />
                  </span>
                  <h3 className="mt-6 font-serif text-3xl font-medium text-neutral-900">{title}</h3>
                  <p className="mt-3 text-base leading-relaxed text-neutral-600">{copy}</p>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Three of her — diagram */}
      <section className="relative overflow-hidden border-y-2 border-dashed border-neutral-200 bg-white">
        <AuroraField className="opacity-40" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="mx-auto max-w-2xl text-center">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              The three of her
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              One Aveline, three minds.
            </h2>
            <p className="mt-4 text-lg text-neutral-600">
              A specialist for the customer, the product and the deal — branching
              from a single concierge.
            </p>
          </Reveal>

          <AgentDiagram reduce={reduce} />

          <Reveal delay={0.15}>
            <p className="mx-auto mt-14 max-w-2xl text-center text-[15px] leading-relaxed text-neutral-400">
              Every high-impact action pauses for human approval — your owner,
              your call. Aveline completes the rest.
            </p>
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ How it works */}
      <section className="relative overflow-hidden">
        <AuroraField className="opacity-45" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              How it works
            </p>
            <h2 className="mt-3 font-serif text-5xl font-medium tracking-tight text-neutral-900">
              From first message to final delivery.
            </h2>
          </Reveal>

          <div className="relative mt-16">
            <div className="pointer-events-none absolute left-[12%] right-[12%] top-[88px] hidden h-[3px] lg:block">
              <div className="absolute inset-0 rounded-full border-t-2 border-dashed border-neutral-300/60" />
              <motion.span
                className="absolute top-[-2px] h-[7px] w-40 rounded-full bg-gradient-to-r from-transparent via-memory to-transparent blur-[1px]"
                animate={{ left: ['-18%', '104%'] }}
                transition={{ duration: 4.5, repeat: Infinity, ease: 'easeInOut' }}
              />
            </div>

            <div className="grid gap-8 md:grid-cols-3">
              {STEPS.map((step, i) => (
                <motion.div
                  key={step.n}
                  initial={{ opacity: 0, y: 34 }}
                  whileInView={{ opacity: 1, y: 0 }}
                  viewport={{ once: true, margin: '-60px' }}
                  transition={{ duration: 0.7, delay: i * 0.16, ease: [0.22, 1, 0.36, 1] }}
                  className="relative flex h-full flex-col overflow-hidden rounded-2xl border border-neutral-200 bg-white shadow-[0_24px_60px_-50px_rgba(122,48,63,0.4)]"
                >
                  <StepImage step={step} index={i} />
                  <div className="flex flex-1 flex-col p-6">
                    <h3 className="font-serif text-2xl font-medium text-neutral-900">{step.title}</h3>
                    <p className="mt-2 text-[15px] leading-relaxed text-neutral-600">{step.copy}</p>
                  </div>
                </motion.div>
              ))}
            </div>
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Enterprise */}
      <section className="px-5 py-20 lg:px-8">
        <Reveal className="mx-auto max-w-6xl">
          <div className="relative overflow-hidden rounded-3xl border-2 border-dashed border-lavender/40 bg-lavender-soft/80 shadow-[0_40px_90px_-60px_rgba(142,124,195,0.5)]">
            <AuroraField className="opacity-35" />
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
      <section className="relative overflow-hidden px-5 pb-28 lg:px-8">
        <AuroraField />
        <div
          aria-hidden
          className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_60%_70%_at_50%_50%,rgba(253,250,248,0.85)_0%,rgba(253,250,248,0)_70%)]"
        />
        <Reveal className="relative z-10 mx-auto max-w-2xl text-center">
          <h2 className="font-serif text-5xl font-medium leading-tight tracking-tight text-neutral-900 [text-shadow:0_2px_30px_rgba(255,255,255,0.5)]">
            Ready to be remembered?
          </h2>
          <p className="mx-auto mt-4 max-w-md text-lg text-neutral-600">
            Join the boutiques letting Ava, Elle and Lina tend the details.
          </p>
          <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
            <Button asChild size="lg" className="h-12 px-8">
              <Link to="/sign-up">Create account</Link>
            </Button>
            <Button asChild size="lg" variant="outline" className="h-12 border-neutral-300 bg-white/80 px-8 shadow-sm">
              <Link to="/download">Download app</Link>
            </Button>
          </div>
        </Reveal>
      </section>
    </SitePage>
  )
}
