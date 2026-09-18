import { useState } from 'react'
import { Link } from 'react-router-dom'
import {
  AlertTriangle,
  ArrowRight,
  Check,
  ChevronDown,
  Minus,
  MessageCircle,
  Sparkles,
  Shirt,
  Wallet,
  Zap,
  Users,
  BarChart3,
  Globe,
  Code2,
  Headphones,
} from 'lucide-react'
import { motion, AnimatePresence } from 'motion/react'

import { Blossom } from '@/components/auth/Blossom'
import { AuroraField } from '@/components/site/AuroraField'
import { Reveal } from '@/components/site/Reveal'
import { SitePage } from '@/components/site/SitePage'
import { Button } from '@/components/ui/button'
import { CtaArrow } from '@/components/site/Icons'
import { cn } from '@/lib/utils'

// ─── Types ────────────────────────────────────────────────────────────────────

type FeatureValue = boolean | 'limited' | 'basic' | 'full' | 'advanced' | string

interface Tier {
  id: string
  name: string
  tagline: string
  price: { monthly: number | null; annual: number | null }
  currency: string
  flowers: number
  staff: number
  customers: number
  cta: string
  to: string
  badge?: string
  highlighted?: boolean
  color: string
  blossomColor: string
  features: Record<string, FeatureValue>
}

interface FAQ {
  q: string
  a: string
}

// ─── Data ─────────────────────────────────────────────────────────────────────

const TIERS: Tier[] = [
  {
    id: 'seed',
    name: 'Seed',
    tagline: 'Your first bloom.',
    price: { monthly: 0, annual: 0 },
    currency: 'LKR',
    flowers: 150,
    staff: 1,
    customers: 50,
    cta: 'Start for free',
    to: '/sign-up',
    color: 'from-emerald-50 via-green-50 to-white border-emerald-200',
    blossomColor: 'text-emerald-500',
    features: {
      memory: true,
      visual: 'limited',
      commerce: 'limited',
      whatsapp: 'limited',
      customContext: false,
      automation: false,
      analytics: 'basic',
      api: false,
      customAgents: false,
      support: 'Community',
    },
  },
  {
    id: 'bloom',
    name: 'Bloom',
    tagline: 'Daily boutique workflow.',
    price: { monthly: 3500, annual: 2916 },
    currency: 'LKR',
    flowers: 750,
    staff: 3,
    customers: 250,
    cta: 'Start Bloom',
    to: '/sign-up',
    badge: 'Most loved',
    highlighted: true,
    color: 'from-rose-50 via-[#fff1f4] to-white border-commerce',
    blossomColor: 'text-memory',
    features: {
      memory: true,
      visual: true,
      commerce: 'limited',
      whatsapp: true,
      customContext: 'basic',
      automation: 'basic',
      analytics: 'basic',
      api: false,
      customAgents: false,
      support: 'Email',
    },
  },
  {
    id: 'orchid',
    name: 'Orchid',
    tagline: 'Established boutique intelligence.',
    price: { monthly: 9000, annual: 7500 },
    currency: 'LKR',
    flowers: 2000,
    staff: 10,
    customers: 1000,
    cta: 'Start Orchid',
    to: '/sign-up',
    color: 'from-purple-50 via-violet-50 to-white border-lavender',
    blossomColor: 'text-lavender',
    features: {
      memory: true,
      visual: true,
      commerce: true,
      whatsapp: true,
      customContext: 'full',
      automation: 'advanced',
      analytics: 'advanced',
      api: false,
      customAgents: false,
      support: 'Priority',
    },
  },
  {
    id: 'rose',
    name: 'Rose',
    tagline: 'Aveline at scale.',
    price: { monthly: 20000, annual: 16666 },
    currency: 'LKR',
    flowers: 5000,
    staff: 25,
    customers: 5000,
    cta: 'Start Rose',
    to: '/sign-up',
    color: 'from-amber-50 via-orange-50 to-white border-visual',
    blossomColor: 'text-visual',
    features: {
      memory: true,
      visual: true,
      commerce: true,
      whatsapp: true,
      customContext: 'full',
      automation: 'advanced',
      analytics: 'advanced',
      api: true,
      customAgents: true,
      support: 'Dedicated',
    },
  },
]

const COMPARISON_ROWS = [
  { key: 'memory', label: 'Concierge memory', icon: Sparkles },
  { key: 'visual', label: 'Visual agent', icon: Shirt },
  { key: 'commerce', label: 'Commerce agent', icon: Wallet },
  { key: 'whatsapp', label: 'WhatsApp integration', icon: MessageCircle },
  { key: 'customContext', label: 'Custom AI context', icon: Zap },
  { key: 'automation', label: 'Automation', icon: Zap },
  { key: 'analytics', label: 'Analytics', icon: BarChart3 },
  { key: 'api', label: 'API access', icon: Code2 },
  { key: 'customAgents', label: 'Custom agents', icon: Globe },
  { key: 'support', label: 'Support', icon: Headphones },
]

const FLOWER_PACKS = [
  { flowers: 100, price: 500 },
  { flowers: 500, price: 2000 },
  { flowers: 1000, price: 3500 },
]

const FAQS: FAQ[] = [
  {
    q: 'What exactly is a Blossom?',
    a: "A Blossom is Aveline's simple unit of AI work. Instead of exposing confusing technical metrics like API tokens or model calls, we convert all underlying AI usage into Blossoms. Think of your monthly Blossom allowance as a budget of AI effort — every recommendation Aveline makes, every outfit it suggests, every customer profile it analyses, draws a small number of Blossoms.",
  },
  {
    q: 'How many Blossoms does a typical action use?',
    a: "A simple lookup or quick reply might use under 1 Blossom. A full customer intelligence workflow — pulling the customer's history, searching inventory, reasoning through a personalised recommendation and composing a reply — typically uses around 3–5 Blossoms. Your usage dashboard will always show your current balance so there are no surprises.",
  },
  {
    q: 'What happens when I run out of Blossoms?',
    a: "Aveline will pause new AI workflows and notify you. Your existing data, customer profiles, and order records remain completely safe and accessible. You can top up with a Blossom Pack immediately, or upgrade to a higher plan for a larger monthly allowance.",
  },
  {
    q: 'Do unused Blossoms roll over to the next month?',
    a: "Currently, monthly Blossom allowances reset at the start of each billing cycle. Blossom Packs you purchase separately may carry over — the final rollover policy will be confirmed before launch.",
  },
  {
    q: 'What is the difference between Limited and full feature access?',
    a: '"Limited" access means the capability is available so you can experience it, but with volume constraints suitable for a smaller boutique. For example, the Visual Agent on Seed can analyse a handful of images per month; on Bloom and above the limit is significantly higher.',
  },
  {
    q: 'Can I add more staff seats without upgrading my plan?',
    a: 'Additional staff seat add-ons are planned for a future release. For now, upgrading to the next plan is the way to expand your team capacity.',
  },
  {
    q: 'What counts as an active customer?',
    a: "An active customer is one your boutique has interacted with recently — via a message, order, or consultation. Historical records for inactive customers remain accessible and do not count toward your plan's active limit.",
  },
  {
    q: 'Is annual billing available?',
    a: 'Yes. Paying annually gives you two months free — equivalent to 10 months of price for 12 months of Aveline. The annual price shown already reflects this discount.',
  },
  {
    q: 'Can I switch plans mid-month?',
    a: "Upgrades take effect immediately, and your Blossom allowance increases right away. Downgrades take effect at the start of your next billing cycle. We use your billing provider's standard proration to handle mid-cycle cost calculations.",
  },
  {
    q: 'Is there a refund policy?',
    a: "We offer a full refund within 7 days of your first subscription. After the 7-day window, we don't offer standard refunds, but exceptional cases are always reviewed individually.",
  },
  {
    q: 'What is the Enterprise plan?',
    a: "Enterprise is for boutique groups and multi-branch businesses that need more than 25 staff members, more than 5,000 active customers, custom integrations, SLAs, or dedicated infrastructure. Contact us to discuss your requirements.",
  },
  {
    q: 'Does Aveline ever expose tokens or model costs to me?',
    a: "No. Tokens, model names, and provider costs are entirely internal to Aveline's infrastructure. We monitor these to ensure Aveline remains profitable and to protect against unusual usage — but you will only ever see Blossoms.",
  },
]

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatPrice(lkr: number | null) {
  if (lkr === null) return null
  if (lkr === 0) return 'Free'
  return `LKR ${lkr.toLocaleString()}`
}

function FeatureBadge({ value }: { value: FeatureValue }) {
  if (value === true)
    return <Check className="mx-auto size-4 text-commerce" aria-label="Included" />
  if (value === false)
    return <Minus className="mx-auto size-4 text-neutral-300" aria-label="Not included" />
  if (typeof value === 'string') {
    const label = value.charAt(0).toUpperCase() + value.slice(1)
    const colorMap: Record<string, string> = {
      limited: 'bg-amber-50 text-amber-700',
      basic: 'bg-blue-50 text-blue-700',
      full: 'bg-emerald-50 text-emerald-700',
      advanced: 'bg-violet-50 text-violet-700',
    }
    const cls = colorMap[value] ?? 'bg-neutral-100 text-neutral-600'
    return (
      <span className={cn('rounded-full px-2 py-0.5 text-[10px] font-semibold', cls)}>
        {label}
      </span>
    )
  }
  return null
}

// ─── Blossom explainer section ────────────────────────────────────────────────

function BlossomExplainer() {
  return (
    <section className="relative mx-auto w-full max-w-7xl px-5 py-20 lg:px-8">
      <Reveal>
        <div className="flex flex-col gap-14 lg:flex-row lg:items-center lg:gap-20">
          {/* Animated medallion */}
          <div className="flex shrink-0 flex-col items-center gap-6 lg:w-72">
            <div className="relative flex items-center justify-center">
              <div className="absolute size-52 rounded-full bg-rose-100/60 blur-3xl" />
              <div className="absolute size-36 rounded-full bg-commerce/10 blur-xl" />
              <Blossom
                className="relative size-36 text-commerce drop-shadow-lg"
                animateCounter
                counterDuration={20}
              />
            </div>
            <p className="text-center font-serif text-5xl font-medium tracking-tight text-neutral-900">
              750
            </p>
            <p className="text-center text-sm font-medium uppercase tracking-[0.15em] text-commerce">
              Blossoms / month
            </p>
          </div>

          {/* Copy + workflow */}
          <div className="flex-1">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              The Blossom System
            </p>
            <h2 className="mt-3 font-serif text-3xl font-medium tracking-tight text-neutral-900 sm:text-4xl">
              AI effort, simply measured.
            </h2>
            <p className="mt-5 text-lg leading-relaxed text-neutral-600">
              A <strong className="text-neutral-900">Blossom</strong> is Aveline's customer-facing
              unit of AI work. Instead of exposing confusing technical metrics — input tokens, output
              tokens, model versions — we distil everything into one number you can actually
              understand.
            </p>
            <p className="mt-4 text-neutral-500">
              Every time Aveline recalls a customer's history, reasons through a product match,
              composes a WhatsApp reply, or sources an outfit reference, a small number of Blossoms
              are drawn from your monthly bouquet. Simple requests use fewer Blossoms; rich,
              multi-step workflows use more.
            </p>

            {/* Mini workflow diagram */}
            <div className="mt-8 rounded-2xl border border-dashed border-neutral-200 bg-neutral-50 p-5">
              <p className="mb-4 text-xs font-semibold uppercase tracking-widest text-neutral-400">
                Example workflow
              </p>
              <div className="flex flex-wrap items-center gap-3 text-sm text-neutral-600">
                {[
                  '"Find something for Maya"',
                  'Customer lookup',
                  'Inventory search',
                  'Recommendation',
                  'Reply drafted',
                ].map((step, i, arr) => (
                  <span key={step} className="flex items-center gap-3">
                    <span className="rounded-lg border border-neutral-200 bg-white px-3 py-1.5 shadow-sm">
                      {step}
                    </span>
                    {i < arr.length - 1 && (
                      <ArrowRight className="size-3.5 shrink-0 text-neutral-300" />
                    )}
                  </span>
                ))}
                <span className="flex items-center gap-2">
                  <span className="font-serif text-xl font-medium text-commerce">≈ 3.7</span>
                  <span className="flex items-center gap-1 text-xs text-neutral-500">
                    <Blossom className="size-4 text-commerce" />
                    Blossoms
                  </span>
                </span>
              </div>
            </div>

            <p className="mt-5 text-sm text-neutral-400">
              Internally, Aveline tracks tokens, model costs, and infrastructure usage — but you
              never need to think about any of that.{' '}
              <Link to="/contact" className="text-commerce hover:underline">
                Questions? Contact us.
              </Link>
            </p>
          </div>
        </div>
      </Reveal>
    </section>
  )
}

// ─── Tier cards ───────────────────────────────────────────────────────────────

function PricingCards({ annual }: { annual: boolean }) {
  return (
    <div className="grid gap-6 md:grid-cols-2 xl:grid-cols-4">
      {TIERS.map((tier, i) => {
        const price = annual ? formatPrice(tier.price.annual) : formatPrice(tier.price.monthly)
        const isFree = tier.price.monthly === 0

        return (
          <Reveal key={tier.id} delay={i * 0.06}>
            <div
              className={cn(
                'relative flex h-full flex-col rounded-2xl bg-gradient-to-b p-7 transition-shadow hover:shadow-xl',
                tier.color,
                tier.highlighted
                  ? 'border-2 shadow-[0_20px_60px_-20px_rgba(139,46,66,0.35)]'
                  : 'border',
              )}
            >
              {tier.badge && (
                <span className="absolute -top-3 left-1/2 -translate-x-1/2 rounded-full bg-commerce px-3 py-1 text-[11px] font-semibold text-white shadow-sm">
                  {tier.badge}
                </span>
              )}

              {/* Header */}
              <div className="flex items-center gap-3">
                <Blossom
                  className={cn('size-8 shrink-0', tier.blossomColor)}
                  animateCounter
                  counterDuration={18 + i * 4}
                />
                <div>
                  <h2 className="font-serif text-xl font-medium text-neutral-900">{tier.name}</h2>
                  <p className="text-xs text-neutral-500">{tier.tagline}</p>
                </div>
              </div>

              {/* Price */}
              <div className="mt-6">
                <div className="flex items-baseline gap-1.5">
                  <span className="font-serif text-3xl font-medium text-neutral-900">
                    {isFree ? 'Free' : price}
                  </span>
                  {!isFree && (
                    <span className="text-sm text-neutral-400">
                      {annual ? '/ mo, billed annually' : '/ month'}
                    </span>
                  )}
                </div>
                {!isFree && annual && (
                  <p className="mt-1 text-xs font-medium text-emerald-600">
                    2 months free with annual billing
                  </p>
                )}
              </div>

              {/* Scale limits */}
              <div className="mt-6 space-y-2 rounded-xl bg-white/60 p-4 text-sm">
                <div className="flex items-center justify-between">
                  <span className="flex items-center gap-1.5 text-neutral-600">
                    <Blossom className="size-3.5 text-commerce" />
                    Blossoms / month
                  </span>
                  <span className="font-medium text-neutral-900">
                    {tier.flowers.toLocaleString()}
                  </span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="flex items-center gap-1.5 text-neutral-600">
                    <Users className="size-3.5" />
                    Staff seats
                  </span>
                  <span className="font-medium text-neutral-900">{tier.staff}</span>
                </div>
                <div className="flex items-center justify-between">
                  <span className="flex items-center gap-1.5 text-neutral-600">
                    <Users className="size-3.5 opacity-60" />
                    Active customers
                  </span>
                  <span className="font-medium text-neutral-900">
                    {tier.customers.toLocaleString()}
                  </span>
                </div>
              </div>

              {/* Feature list */}
              <ul className="mt-6 flex-1 space-y-2.5 border-t border-dashed border-neutral-200 pt-5">
                {[
                  { key: 'memory', label: 'Concierge memory' },
                  { key: 'visual', label: 'Visual agent' },
                  { key: 'commerce', label: 'Commerce agent' },
                  { key: 'whatsapp', label: 'WhatsApp' },
                  { key: 'customContext', label: 'Custom AI context' },
                  { key: 'automation', label: 'Automation' },
                  { key: 'analytics', label: 'Analytics' },
                  { key: 'api', label: 'API access' },
                  { key: 'customAgents', label: 'Custom agents' },
                ].map(({ key, label }) => {
                  const val = tier.features[key]
                  if (val === false) return null
                  return (
                    <li key={key} className="flex items-center gap-2.5 text-sm text-neutral-700">
                      <Check
                        className={cn(
                          'size-3.5 shrink-0',
                          tier.highlighted ? 'text-commerce' : 'text-neutral-400',
                        )}
                        aria-hidden
                      />
                      <span>
                        {label}
                        {typeof val === 'string' && (
                          <span className="ml-1 text-xs text-neutral-400">({val})</span>
                        )}
                      </span>
                    </li>
                  )
                })}
                <li className="flex items-center gap-2.5 text-sm text-neutral-700">
                  <Headphones className="size-3.5 shrink-0 text-neutral-400" aria-hidden />
                  {tier.features.support as string} support
                </li>
              </ul>

              {/* CTA */}
              <Button
                asChild
                size="lg"
                className={cn('group mt-8 h-11 w-full')}
                variant={tier.highlighted ? 'default' : 'outline'}
              >
                <Link to={tier.to}>
                  {tier.cta}
                  <CtaArrow />
                </Link>
              </Button>
            </div>
          </Reveal>
        )
      })}
    </div>
  )
}

// ─── Comparison table ─────────────────────────────────────────────────────────

function ComparisonTable() {
  return (
    <section className="mx-auto w-full max-w-7xl px-5 py-16 lg:px-8">
      <Reveal>
        <h2 className="font-serif text-2xl font-medium text-neutral-900">Compare plans</h2>
        <p className="mt-2 text-neutral-500">Every feature, side by side.</p>
      </Reveal>

      <div className="mt-8 overflow-x-auto rounded-2xl border border-neutral-200 bg-white">
        <table className="w-full min-w-[640px] table-fixed text-sm">
          <thead>
            <tr className="border-b border-neutral-100">
              <th className="w-48 py-4 pl-6 text-left text-xs font-semibold uppercase tracking-widest text-neutral-400">
                Feature
              </th>
              {TIERS.map((t) => (
                <th
                  key={t.id}
                  className={cn(
                    'py-4 text-center font-medium',
                    t.highlighted ? 'text-commerce' : 'text-neutral-700',
                  )}
                >
                  <span className="flex flex-col items-center gap-1">
                    <Blossom className={cn('size-5', t.blossomColor)} />
                    {t.name}
                  </span>
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {/* Scale rows */}
            {[
              { label: 'Blossoms / month', values: TIERS.map((t) => t.flowers.toLocaleString()) },
              { label: 'Staff seats', values: TIERS.map((t) => String(t.staff)) },
              { label: 'Active customers', values: TIERS.map((t) => t.customers.toLocaleString()) },
            ].map(({ label, values }, ri) => (
              <tr
                key={label}
                className={cn('border-b border-neutral-50', ri % 2 === 0 ? 'bg-neutral-50/40' : '')}
              >
                <td className="py-3.5 pl-6 text-neutral-600">{label}</td>
                {values.map((v, i) => (
                  <td
                    key={i}
                    className={cn(
                      'py-3.5 text-center font-medium',
                      TIERS[i].highlighted ? 'text-commerce' : 'text-neutral-800',
                    )}
                  >
                    {v}
                  </td>
                ))}
              </tr>
            ))}

            {/* Feature rows */}
            {COMPARISON_ROWS.map(({ key, label }, ri) => (
              <tr
                key={key}
                className={cn(
                  'border-b border-neutral-50',
                  (ri + 3) % 2 === 0 ? 'bg-neutral-50/40' : '',
                )}
              >
                <td className="py-3.5 pl-6 text-neutral-600">{label}</td>
                {TIERS.map((t) => (
                  <td key={t.id} className="py-3.5 text-center">
                    <FeatureBadge value={t.features[key]} />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

// ─── Blossom Packs ────────────────────────────────────────────────────────────

function FlowerPacks() {
  return (
    <section className="mx-auto w-full max-w-7xl px-5 pb-16 lg:px-8">
      <Reveal>
        <div className="rounded-2xl border border-dashed border-neutral-200 bg-gradient-to-br from-rose-50 to-white p-8">
          <div className="flex flex-col gap-8 lg:flex-row lg:items-center lg:gap-16">
            <div className="lg:w-80">
              <div className="flex items-center gap-3">
                <Blossom className="size-7 text-commerce" />
                <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
                  Blossom Packs
                </p>
              </div>
              <h2 className="mt-3 font-serif text-2xl font-medium text-neutral-900">
                Need a little more bloom?
              </h2>
              <p className="mt-3 text-sm leading-relaxed text-neutral-500">
                Top up your Blossom balance any time without changing plans. Available on Bloom and
                above.
              </p>
            </div>

            <div className="grid flex-1 gap-4 sm:grid-cols-3">
              {FLOWER_PACKS.map(({ flowers, price }) => (
                <div
                  key={flowers}
                  className="flex flex-col items-center gap-3 rounded-xl border border-neutral-200 bg-white p-5 text-center shadow-sm"
                >
                  <Blossom className="size-8 text-commerce" />
                  <p className="font-serif text-2xl font-medium text-neutral-900">
                    {flowers.toLocaleString()}
                  </p>
                  <p className="text-xs text-neutral-500">Blossoms</p>
                  <p className="text-sm font-semibold text-neutral-900">
                    LKR {price.toLocaleString()}
                  </p>
                </div>
              ))}
            </div>
          </div>

          <p className="mt-6 text-xs text-neutral-400">
            Blossom Packs are one-time purchases and do not renew. If you consistently need more
            Blossoms, upgrading your plan offers better long-term value.{' '}
            <Link to="/contact" className="text-commerce hover:underline">
              Check pricing &rarr;
            </Link>
          </p>
        </div>
      </Reveal>
    </section>
  )
}

// ─── FAQ section ──────────────────────────────────────────────────────────────

function FaqSection() {
  const [open, setOpen] = useState<number | null>(null)

  return (
    <section className="mx-auto w-full max-w-3xl px-5 pb-24 lg:px-8">
      <Reveal>
        <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">FAQ</p>
        <h2 className="mt-3 font-serif text-3xl font-medium tracking-tight text-neutral-900">
          Questions, answered.
        </h2>
      </Reveal>

      <div className="mt-10 divide-y divide-neutral-100">
        {FAQS.map((faq, i) => (
          <div key={i}>
            <button
              id={`faq-btn-${i}`}
              onClick={() => setOpen(open === i ? null : i)}
              className="flex w-full items-start justify-between gap-4 py-5 text-left"
              aria-expanded={open === i}
              aria-controls={`faq-panel-${i}`}
            >
              <span className="text-base font-medium text-neutral-900">{faq.q}</span>
              <ChevronDown
                className={cn(
                  'mt-0.5 size-5 shrink-0 text-neutral-400 transition-transform duration-300',
                  open === i ? 'rotate-180' : '',
                )}
                aria-hidden
              />
            </button>
            <AnimatePresence initial={false}>
              {open === i && (
                <motion.div
                  id={`faq-panel-${i}`}
                  role="region"
                  aria-labelledby={`faq-btn-${i}`}
                  initial={{ height: 0, opacity: 0 }}
                  animate={{ height: 'auto', opacity: 1 }}
                  exit={{ height: 0, opacity: 0 }}
                  transition={{ duration: 0.28, ease: [0.4, 0, 0.2, 1] }}
                  className="overflow-hidden"
                >
                  <p className="pb-5 text-sm leading-relaxed text-neutral-500">{faq.a}</p>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        ))}
      </div>
    </section>
  )
}

// ─── Enterprise banner ────────────────────────────────────────────────────────

function EnterpriseBanner() {
  return (
    <section className="mx-auto w-full max-w-7xl px-5 pb-24 lg:px-8">
      <Reveal>
        <div className="relative overflow-hidden rounded-3xl bg-neutral-900 px-8 py-14 text-center">
          <div className="pointer-events-none absolute inset-0 bg-[radial-gradient(ellipse_80%_60%_at_50%_100%,rgba(139,46,66,0.35),transparent)]" />
          <Blossom
            className="relative mx-auto mb-6 size-12 text-commerce opacity-80"
            animateCounter
            counterDuration={22}
          />
          <h2 className="relative font-serif text-3xl font-medium text-white">
            Running multiple branches?
          </h2>
          <p className="relative mx-auto mt-4 max-w-xl text-neutral-400">
            Enterprise pricing is available for boutique groups and larger operations that need
            custom integrations, dedicated infrastructure, SLAs, and specialised support.
          </p>
          <Button
            asChild
            size="lg"
            className="group relative mt-8 bg-commerce text-white hover:bg-commerce/90"
          >
            <Link to="/contact">
              Talk to us
              <CtaArrow />
            </Link>
          </Button>
        </div>
      </Reveal>
    </section>
  )
}

// ─── Page root ────────────────────────────────────────────────────────────────

export function PlansPage() {
  const [annual, setAnnual] = useState(false)

  return (
    <SitePage>
      {/* Hero + tier cards */}
      <section className="relative overflow-hidden">
        <AuroraField className="opacity-40" />
        <div className="relative mx-auto w-full max-w-7xl px-5 pb-8 pt-20 lg:px-8">
          <Reveal className="max-w-2xl">
            <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
              Pricing
            </p>
            <h1 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
              Every boutique starts as a seed.
            </h1>
            <p className="mt-5 text-lg text-neutral-500">
              Choose the level of intelligence that fits your boutique. Upgrade when you are ready to
              bloom.
            </p>
          </Reveal>

          {/* Billing toggle */}
          <Reveal delay={0.08}>
            <div className="mt-10 flex items-center gap-4">
              <span
                className={cn(
                  'text-sm font-medium',
                  !annual ? 'text-neutral-900' : 'text-neutral-400',
                )}
              >
                Monthly
              </span>
              <button
                id="billing-cycle-toggle"
                role="switch"
                aria-checked={annual}
                onClick={() => setAnnual((v) => !v)}
                className={cn(
                  'relative inline-flex h-6 w-11 items-center rounded-full transition-colors duration-300 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-commerce',
                  annual ? 'bg-commerce' : 'bg-neutral-200',
                )}
              >
                <span
                  className={cn(
                    'inline-block size-4 rounded-full bg-white shadow transition-transform duration-300',
                    annual ? 'translate-x-6' : 'translate-x-1',
                  )}
                />
              </button>
              <span
                className={cn(
                  'text-sm font-medium',
                  annual ? 'text-neutral-900' : 'text-neutral-400',
                )}
              >
                Annual
              </span>
              <span className="rounded-full bg-emerald-50 px-2.5 py-0.5 text-xs font-semibold text-emerald-700">
                2 months free
              </span>
            </div>
          </Reveal>

          <div className="mt-10">
            <PricingCards annual={annual} />
          </div>

          <Reveal delay={0.1}>
            <p className="mt-8 text-center text-sm text-neutral-400">
              Prices in Sri Lankan Rupees (LKR). All plans include a 7-day refund window.{' '}
              <Link to="/contact" className="text-commerce hover:underline">
                Questions? Contact us.
              </Link>
            </p>
          </Reveal>

          <Reveal delay={0.12}>
            <div className="mt-8 flex items-start gap-3 rounded-2xl border border-dashed border-amber-300/70 bg-amber-50/70 px-5 py-4">
              <AlertTriangle className="mt-0.5 size-4 shrink-0 text-amber-600" aria-hidden />
              <p className="text-sm leading-relaxed text-amber-900">
                <span className="font-semibold">Pricing and features are subject to change.</span>{' '}
                Aveline is in alpha development, so plan prices, Blossom allowances, and included
                features shown here are provisional and may be adjusted before the full launch.
              </p>
            </div>
          </Reveal>
        </div>
      </section>

      <BlossomExplainer />
      <ComparisonTable />
      <FlowerPacks />
      <FaqSection />
      <EnterpriseBanner />
    </SitePage>
  )
}
