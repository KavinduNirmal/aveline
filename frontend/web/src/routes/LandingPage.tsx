import { Link } from 'react-router-dom'
import {
  ArrowRight,
  HeartHandshake,
  MessageCircle,
  ShieldCheck,
  Shirt,
  Sparkles,
  Wallet,
} from 'lucide-react'

import { AuroraField } from '@/components/site/Reveal'
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

const AGENTS = [
  {
    name: 'Ava',
    color: 'memory',
    ring: 'text-memory',
    tag: 'The memory agent',
    line: 'She remembers every customer.',
    copy: 'Parses messages, builds warm profiles and recalls each preference, occasion and past purchase.',
    points: ['Facts & preferences', 'Conversation briefs', 'Personal drafts'],
  },
  {
    name: 'Elle',
    color: 'visual',
    ring: 'text-visual',
    tag: 'The visual agent',
    line: 'She sees what suits you.',
    copy: 'Reads a reference photo, searches the shelf and composes outfits that fit the moment.',
    points: ['Image understanding', 'Customer matching', 'Supplier sourcing'],
  },
  {
    name: 'Lina',
    color: 'commerce',
    ring: 'text-commerce',
    tag: 'The commerce agent',
    line: 'She closes with care.',
    copy: 'Checks margins, issues deposits, and pauses big decisions for your approval before delivery.',
    points: ['Pricing & margins', 'Payments', 'Approvals & delivery'],
  },
]

const STEPS = [
  {
    n: '01',
    title: 'Create your account',
    copy: 'Owners open a boutique. Staff join with an invitation code.',
  },
  {
    n: '02',
    title: 'Connect your day',
    copy: 'Bring in WhatsApp, your catalogue and your business rules.',
  },
  {
    n: '03',
    title: 'Let Aveline tend the details',
    copy: 'Ava, Elle and Lina keep customers, products and deals moving — you approve the big calls.',
  },
]

export function LandingPage() {
  return (
    <SitePage>
      {/* ------------------------------------------------ Hero */}
      <section className="relative overflow-hidden">
        <AuroraField className="opacity-70" />
        <div className="relative mx-auto flex w-full max-w-5xl flex-col items-center px-5 pb-24 pt-20 text-center lg:px-8 lg:pt-28">
          <Reveal>
            <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-commerce/25 bg-commerce/[0.04] px-3.5 py-1.5 text-xs font-medium uppercase tracking-[0.18em] text-commerce">
              <Sparkles className="size-3.5" aria-hidden />
              Aveline — the assistant that remembers
            </span>
          </Reveal>

          <Reveal delay={0.08}>
            <h1 className="mt-7 max-w-3xl font-serif text-5xl font-medium leading-[1.08] tracking-tight text-neutral-900 sm:text-6xl lg:text-7xl">
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
              <Button asChild size="lg" className="h-12 px-7">
                <Link to="/sign-up">
                  Create account
                  <ArrowRight className="size-4" aria-hidden />
                </Link>
              </Button>
              <Button asChild size="lg" variant="outline" className="h-12 px-7 border-neutral-300 bg-white/70">
                <Link to="/download">Download app</Link>
              </Button>
            </div>
          </Reveal>

          <Reveal delay={0.32}>
            <p className="mt-7 text-xs uppercase tracking-[0.22em] text-neutral-400">
              For boutique owners, managers &amp; staff · Sri Lanka · Clerk-secured
            </p>
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ Persona */}
      <section className="border-y-2 border-dashed border-neutral-200 bg-white">
        <Reveal className="mx-auto max-w-3xl px-5 py-20 text-center lg:px-8">
          <p className="font-serif text-3xl font-medium italic leading-snug text-neutral-800 sm:text-4xl">
            “She remembers, so you don’t have to — every client, every detail,
            warmly.”
          </p>
          <p className="mt-5 text-sm text-neutral-500">
            Meet the three who keep your boutique moving.
          </p>
        </Reveal>
      </section>

      {/* ------------------------------------------------ Features */}
      <section id="features" className="scroll-mt-20">
        <div className="mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              Features
            </p>
            <h2 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
              Everything your boutique does, in one calm place.
            </h2>
          </Reveal>

          <div className="mt-14 grid gap-x-10 gap-y-12 sm:grid-cols-2 lg:grid-cols-3">
            {FEATURES.map(({ icon: Icon, title, copy }, i) => (
              <Reveal key={title} delay={i * 0.05}>
                <div className="group">
                  <span className="flex size-11 items-center justify-center rounded-full bg-commerce/10 text-commerce transition-colors group-hover:bg-commerce group-hover:text-white">
                    <Icon className="size-5" aria-hidden />
                  </span>
                  <h3 className="mt-5 font-serif text-2xl font-medium text-neutral-900">
                    {title}
                  </h3>
                  <p className="mt-2 text-[15px] leading-relaxed text-neutral-500">
                    {copy}
                  </p>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Three agents */}
      <section className="relative overflow-hidden border-y-2 border-dashed border-neutral-200 bg-white">
        <AuroraField className="opacity-40" />
        <div className="relative mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="mx-auto max-w-2xl text-center">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              The three of her
            </p>
            <h2 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
              Ava, Elle &amp; Lina.
            </h2>
            <p className="mt-4 text-lg text-neutral-500">
              Three specialists, one Aveline — working together behind every
              conversation.
            </p>
          </Reveal>

          <div className="mt-16 grid gap-6 lg:grid-cols-3">
            {AGENTS.map((agent, i) => (
              <Reveal key={agent.name} delay={i * 0.08}>
                <div
                  className={cn(
                    'h-full rounded-3xl border-2 border-dashed p-7 transition-transform hover:-translate-y-1',
                    agent.color === 'memory' && 'border-memory/25 bg-memory/[0.05]',
                    agent.color === 'visual' && 'border-visual/30 bg-visual/[0.05]',
                    agent.color === 'commerce' && 'border-commerce/25 bg-commerce/[0.04]',
                  )}
                >
                  <div className="flex items-center gap-3">
                    <span
                      className={cn(
                        'flex size-12 items-center justify-center rounded-full font-serif text-xl font-medium text-white shadow-sm',
                        agent.color === 'memory' && 'bg-memory',
                        agent.color === 'visual' && 'bg-visual',
                        agent.color === 'commerce' && 'bg-commerce',
                      )}
                    >
                      {agent.name.charAt(0)}
                    </span>
                    <div>
                      <p className="text-sm font-semibold text-neutral-800">{agent.name}</p>
                      <p className="text-xs uppercase tracking-[0.16em] text-neutral-400">
                        {agent.tag}
                      </p>
                    </div>
                  </div>

                  <p className={cn('mt-5 font-serif text-xl font-medium', agent.ring)}>
                    {agent.line}
                  </p>
                  <p className="mt-2 text-sm leading-relaxed text-neutral-500">{agent.copy}</p>

                  <ul className="mt-6 space-y-2 border-t-2 border-dashed border-neutral-200 pt-5">
                    {agent.points.map((point) => (
                      <li key={point} className="flex items-center gap-2 text-sm text-neutral-600">
                        <span
                          className={cn(
                            'size-1.5 rounded-full',
                            agent.color === 'memory' && 'bg-memory',
                            agent.color === 'visual' && 'bg-visual',
                            agent.color === 'commerce' && 'bg-commerce',
                          )}
                        />
                        {point}
                      </li>
                    ))}
                  </ul>
                </div>
              </Reveal>
            ))}
          </div>

          <Reveal delay={0.15}>
            <p className="mx-auto mt-12 max-w-2xl text-center text-sm leading-relaxed text-neutral-400">
              Every high-impact action pauses for human approval — your owner,
              your call. Aveline completes the rest.
            </p>
          </Reveal>
        </div>
      </section>

      {/* ------------------------------------------------ How it works */}
      <section>
        <div className="mx-auto w-full max-w-6xl px-5 py-24 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              How it works
            </p>
            <h2 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900">
              From first message to final delivery.
            </h2>
          </Reveal>

          <div className="mt-14 grid gap-10 md:grid-cols-3">
            {STEPS.map((step, i) => (
              <Reveal key={step.n} delay={i * 0.08}>
                <div className="border-t-2 border-dashed border-neutral-300 pt-6">
                  <p className="font-serif text-4xl font-medium text-neutral-300">{step.n}</p>
                  <h3 className="mt-4 text-lg font-semibold text-neutral-900">{step.title}</h3>
                  <p className="mt-2 text-sm leading-relaxed text-neutral-500">{step.copy}</p>
                </div>
              </Reveal>
            ))}
          </div>
        </div>
      </section>

      {/* ------------------------------------------------ Enterprise */}
      <section className="px-5 lg:px-8">
        <Reveal className="mx-auto max-w-6xl">
          <div className="flex flex-col items-start justify-between gap-6 rounded-3xl border-2 border-dashed border-lavender/40 bg-lavender-soft/60 p-10 lg:flex-row lg:items-center">
            <div className="max-w-xl">
              <p className="text-xs font-semibold uppercase tracking-[0.2em] text-lavender">
                Aveline for groups
              </p>
              <h2 className="mt-3 font-serif text-3xl font-medium tracking-tight text-neutral-900 sm:text-4xl">
                Running several boutiques?
              </h2>
              <p className="mt-3 text-[15px] leading-relaxed text-neutral-600">
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
              <Button asChild size="lg" variant="outline" className="h-12 px-6 border-neutral-300 bg-white/70">
                <Link to="/plans">See plans</Link>
              </Button>
            </div>
          </div>
        </Reveal>
      </section>

      {/* ------------------------------------------------ Final CTA */}
      <section className="relative overflow-hidden px-5 pb-28 pt-24 lg:px-8">
        <AuroraField className="opacity-60" />
        <Reveal className="relative mx-auto max-w-2xl text-center">
          <h2 className="font-serif text-4xl font-medium leading-tight tracking-tight text-neutral-900 sm:text-5xl">
            Ready to be remembered?
          </h2>
          <p className="mx-auto mt-4 max-w-md text-lg text-neutral-500">
            Join the boutiques letting Ava, Elle and Lina tend the details.
          </p>
          <div className="mt-8 flex flex-wrap items-center justify-center gap-3">
            <Button asChild size="lg" className="h-12 px-7">
              <Link to="/sign-up">Create account</Link>
            </Button>
            <Button asChild size="lg" variant="outline" className="h-12 px-7 border-neutral-300 bg-white/70">
              <Link to="/download">Download app</Link>
            </Button>
          </div>
        </Reveal>
      </section>
    </SitePage>
  )
}
