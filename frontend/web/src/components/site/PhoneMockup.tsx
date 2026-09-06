import { useState } from 'react'
import { motion, AnimatePresence } from 'motion/react'
import {
  Check,
  X,
  Sparkles,
  Wifi,
  Battery,
  ShieldCheck,
  ChevronRight,
} from 'lucide-react'
import { Blossom } from '@/components/auth/Blossom'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'

interface Scenario {
  customer: {
    name: string
    avatarUrl: string
    tier: string
    spend: string
    preferences: string
    source: string
    time: string
  }
  inquiry: string
  avaNote: string
  elleNote: string
  linaNote: string
  recommendation: {
    item: string
    price: string
    deposit: string
    size: string
    inventory: string
  }
}

const SCENARIOS: Scenario[] = [
  {
    customer: {
      name: 'Shanali Perera',
      avatarUrl: 'https://images.pexels.com/photos/1239291/pexels-photo-1239291.jpeg?auto=compress&cs=tinysrgb&w=150',
      tier: 'VIP Patron',
      spend: 'LKR 185,000',
      preferences: 'Pastels & raw silks, UK 10, prefers tea length',
      source: 'WhatsApp',
      time: 'Just now',
    },
    inquiry: '“Hi! I have a garden wedding this Saturday — do you have anything blush in my size?”',
    avaNote: 'Prefers blush & lavender tones; purchased Rose Silk Maxi in May. Sister’s wedding attendee.',
    elleNote: 'Matched 3 atelier gowns: Ceylon Peony Drape Gown perfectly matches her requested palette.',
    linaNote: 'Hold placed (24 hrs). Drafted invoice: LKR 28,500. VIP deposit terms applied.',
    recommendation: {
      item: 'Peony Drape Raw-Silk Gown',
      price: 'LKR 28,500',
      deposit: 'LKR 10,000 reserved',
      size: 'Size UK 10 · 1 in atelier stock',
      inventory: 'Atelier Colombo Showroom',
    },
  },
  {
    customer: {
      name: 'Dinithi Wickramasinghe',
      avatarUrl: 'https://images.pexels.com/photos/774909/pexels-photo-774909.jpeg?auto=compress&cs=tinysrgb&w=150',
      tier: 'Connoisseur',
      spend: 'LKR 320,000',
      preferences: 'Handloom linens & artisanal brass jewellery',
      source: 'WhatsApp',
      time: '3m ago',
    },
    inquiry: '“Could I reserve the Cinnamon Linen Blazer I saw on your runway reel yesterday?”',
    avaNote: 'Sizes run true-to-fit for her UK 12 frame. Loves tailored structured lapels.',
    elleNote: 'Identified runway reel look #4: Terracotta Cinnamon hand-spun organic linen.',
    linaNote: 'Margin safe at 42%. Verified 2 units remaining before next artisan batch.',
    recommendation: {
      item: 'Terracotta Handloom Linen Blazer',
      price: 'LKR 34,000',
      deposit: 'LKR 15,000 reserved',
      size: 'Size UK 12 · 2 in stock',
      inventory: 'Galle Fort Boutique',
    },
  },
]

export function PhoneMockup() {
  const [scenarioIndex, setScenarioIndex] = useState(0)
  const [decisionState, setDecisionState] = useState<'idle' | 'approved' | 'declined'>('idle')

  const current = SCENARIOS[scenarioIndex]

  const handleNextScenario = () => {
    setDecisionState('idle')
    setScenarioIndex((prev) => (prev + 1) % SCENARIOS.length)
  }

  return (
    <div className="relative mx-auto w-full max-w-[375px] select-none sm:max-w-[400px]">
      {/* Ambient background bloom behind phone */}
      <div
        className="absolute -inset-4 -z-10 rounded-[3rem] bg-gradient-to-tr from-commerce/25 via-primary/20 to-visual/25 blur-2xl opacity-70"
        aria-hidden
      />

      {/* Outer Phone Shell */}
      <div className="relative overflow-hidden rounded-[2.75rem] border-[7px] border-neutral-900 bg-neutral-950 p-2.5 shadow-[0_35px_80px_-25px_rgba(20,10,15,0.65),0_0_0_1px_rgba(255,255,255,0.15)_inset]">
        {/* Dynamic Island / Notch */}
        <div className="absolute left-1/2 top-3 z-30 flex h-4.5 -translate-x-1/2 items-center gap-2 rounded-full bg-neutral-950 px-3.5 shadow-sm">
          <span className="size-2 rounded-full bg-neutral-800" />
          <span className="h-1.5 w-8 rounded-full bg-neutral-800" />
        </div>

        {/* Screen Bezel and Display Area */}
        <div className="relative flex min-h-[670px] flex-col overflow-hidden rounded-[2.25rem] bg-[#fffaf8] text-neutral-900">
          {/* iOS Status Bar */}
          <div className="relative z-20 flex items-center justify-between px-6 pt-3 text-[11px] font-semibold text-neutral-800">
            <span>9:41</span>
            <div className="flex items-center gap-1.5 opacity-80">
              <span className="text-[10px]">5G</span>
              <Wifi className="size-3" />
              <Battery className="size-3.5" />
            </div>
          </div>

          {/* Top Bar: Aveline Atelier Staff Header */}
          <div className="relative z-10 border-b border-neutral-200/80 bg-white/85 px-4.5 py-3 backdrop-blur-md">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2.5">
                <div className="relative flex size-9 items-center justify-center rounded-xl bg-gradient-to-br from-primary via-primary-container to-commerce text-white shadow-sm">
                  <Blossom animateCounter counterDuration={16} className="size-5.5 text-white" />
                  <span className="absolute -bottom-0.5 -right-0.5 size-2.5 rounded-full border-2 border-white bg-emerald-500" />
                </div>
                <div>
                  <div className="flex items-center gap-1.5">
                    <p className="font-serif text-sm font-semibold leading-tight text-neutral-900">
                      Aveline Staff
                    </p>
                    <Badge variant="outline" className="h-4.5 rounded-md border-primary/25 bg-primary/5 px-1.5 text-[9px] font-medium text-primary">
                      Live
                    </Badge>
                  </div>
                  <p className="text-[10px] text-neutral-500">Colombo Atelier · Staff Feed</p>
                </div>
              </div>

              {/* Toggle scenario button */}
              <button
                type="button"
                onClick={handleNextScenario}
                className="flex items-center gap-1 rounded-full border border-neutral-200 bg-neutral-100/90 px-2.5 py-1 text-[10px] font-medium text-neutral-600 transition-colors hover:bg-neutral-200"
              >
                <span>Next inquiry</span>
                <ChevronRight className="size-3" />
              </button>
            </div>
          </div>

          {/* Scrollable Staff Feed */}
          <div className="flex-1 space-y-3.5 overflow-y-auto px-4 py-3.5 text-xs">
            {/* Banner: Human Approval Gate */}
            <div className="flex items-center gap-2 rounded-xl border border-dashed border-amber-300 bg-amber-50/80 px-3 py-2 text-[11px] text-amber-900">
              <ShieldCheck className="size-4 shrink-0 text-amber-700" />
              <span>
                <strong className="font-semibold">Workflow ready:</strong> Aveline drafted replies. Review below.
              </span>
            </div>

            <AnimatePresence mode="wait">
              <motion.div
                key={scenarioIndex}
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                exit={{ opacity: 0 }}
                transition={{ duration: 0.2 }}
                className="space-y-3"
              >
                {/* 1. Step 1 in workflow: Embedded WhatsApp Customer Inquiry */}
                <motion.div
                  initial={{ opacity: 0, y: 12 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.35, delay: 0.1 }}
                  className="relative overflow-hidden rounded-2xl border border-neutral-200 bg-white p-3.5 shadow-sm"
                >
                  <div className="flex items-center justify-between border-b border-neutral-100 pb-2.5">
                    <div className="flex items-center gap-2.5">
                      <img
                        src={current.customer.avatarUrl}
                        alt={current.customer.name}
                        className="size-8 rounded-full object-cover ring-2 ring-emerald-500/30"
                      />
                      <div>
                        <div className="flex items-center gap-1.5">
                          <p className="text-xs font-semibold text-neutral-900">{current.customer.name}</p>
                          <Badge className="h-4 rounded-full bg-emerald-600 px-1.5 text-[9px] text-white">
                            WhatsApp
                          </Badge>
                        </div>
                        <p className="text-[10px] text-neutral-400">
                          {current.customer.tier} · Spent {current.customer.spend}
                        </p>
                      </div>
                    </div>
                    <span className="text-[10px] text-neutral-400">{current.customer.time}</span>
                  </div>

                  {/* Customer WhatsApp message speech bubble */}
                  <div className="mt-2.5 rounded-xl bg-emerald-50/60 p-2.5 text-neutral-800 border border-emerald-200/50">
                    <p className="text-[12px] leading-relaxed italic text-neutral-800">
                      {current.inquiry}
                    </p>
                  </div>
                </motion.div>

                {/* 2. Step 2 in workflow: Specialist Agents collaboration */}
                <div className="space-y-2 rounded-2xl border border-dashed border-neutral-200 bg-white/70 p-3">
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-semibold uppercase tracking-wider text-neutral-400">
                      Specialist Workflow
                    </span>
                    <span className="flex items-center gap-1 text-[10px] font-medium text-primary">
                      <Sparkles className="size-3" />
                      Sequence active
                    </span>
                  </div>

                  {/* Ava: Memory Note (different colored blossom: rose-pink) */}
                  <motion.div
                    initial={{ opacity: 0, x: -12 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ duration: 0.4, delay: 0.4 }}
                    className="flex items-start gap-2.5 rounded-xl bg-[#fff2f5] p-2.5 border border-rose-100"
                  >
                    <span className="flex size-6 shrink-0 items-center justify-center rounded-lg bg-memory text-white shadow-xs">
                      <Blossom className="size-3.5 text-white" />
                    </span>
                    <div className="leading-snug">
                      <span className="font-semibold text-memory">Ava (Memory): </span>
                      <span className="text-neutral-700">{current.avaNote}</span>
                    </div>
                  </motion.div>

                  {/* Elle: Visual Note (different colored blossom: gold-amber) */}
                  <motion.div
                    initial={{ opacity: 0, x: -12 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ duration: 0.4, delay: 0.8 }}
                    className="flex items-start gap-2.5 rounded-xl bg-[#fef8ed] p-2.5 border border-amber-100"
                  >
                    <span className="flex size-6 shrink-0 items-center justify-center rounded-lg bg-visual text-white shadow-xs">
                      <Blossom className="size-3.5 text-white" />
                    </span>
                    <div className="leading-snug">
                      <span className="font-semibold text-visual">Elle (Visual): </span>
                      <span className="text-neutral-700">{current.elleNote}</span>
                    </div>
                  </motion.div>

                  {/* Lina: Commerce Note (different colored blossom: wine-rose) */}
                  <motion.div
                    initial={{ opacity: 0, x: -12 }}
                    animate={{ opacity: 1, x: 0 }}
                    transition={{ duration: 0.4, delay: 1.2 }}
                    className="flex items-start gap-2.5 rounded-xl bg-[#fbf0f2] p-2.5 border border-primary/15"
                  >
                    <span className="flex size-6 shrink-0 items-center justify-center rounded-lg bg-commerce text-white shadow-xs">
                      <Blossom className="size-3.5 text-white" />
                    </span>
                    <div className="leading-snug">
                      <span className="font-semibold text-commerce">Lina (Commerce): </span>
                      <span className="text-neutral-700">{current.linaNote}</span>
                    </div>
                  </motion.div>
                </div>

                {/* 3. Step 3 in workflow: Recommended Item & Invoice card */}
                <motion.div
                  initial={{ opacity: 0, y: 10 }}
                  animate={{ opacity: 1, y: 0 }}
                  transition={{ duration: 0.35, delay: 1.6 }}
                  className="rounded-xl border border-neutral-200 bg-white p-3 shadow-xs"
                >
                  <div className="flex items-center justify-between">
                    <div>
                      <p className="font-medium text-neutral-900">{current.recommendation.item}</p>
                      <p className="text-[10px] text-neutral-500">{current.recommendation.size}</p>
                    </div>
                    <div className="text-right">
                      <p className="font-semibold text-primary">{current.recommendation.price}</p>
                      <span className="inline-block rounded-md bg-rose-50 px-1.5 py-0.5 text-[9px] font-medium text-rose-700">
                        {current.recommendation.deposit}
                      </span>
                    </div>
                  </div>
                </motion.div>
              </motion.div>
            </AnimatePresence>
          </div>

          {/* Bottom Staff Action Deck */}
          <div className="border-t border-neutral-200 bg-white/90 p-3.5 backdrop-blur-md">
            {decisionState === 'idle' ? (
              <div>
                <div className="flex items-center gap-2">
                  <Button
                    size="sm"
                    className="flex-1 h-9.5 gap-1.5 rounded-full bg-primary text-[11px] font-medium text-white shadow-sm hover:bg-primary/90"
                    onClick={() => setDecisionState('approved')}
                  >
                    <Check className="size-3.5" />
                    Approve &amp; Send to WhatsApp
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    className="h-9.5 rounded-full border-neutral-200 text-[11px] font-medium text-neutral-700 hover:bg-neutral-50"
                    onClick={() => setDecisionState('declined')}
                  >
                    <X className="size-3.5" />
                    Decline
                  </Button>
                </div>
                <p className="mt-2 text-center text-[10px] text-neutral-400">
                  Staff has final word · Customer only receives approved replies
                </p>
              </div>
            ) : (
              <div className="flex items-center justify-between rounded-xl bg-neutral-50 px-3 py-2">
                <div className="flex items-center gap-2">
                  {decisionState === 'approved' ? (
                    <>
                      <span className="flex size-5 items-center justify-center rounded-full bg-emerald-100 text-emerald-700">
                        <Check className="size-3" />
                      </span>
                      <span className="text-[11px] font-medium text-emerald-900">
                        Sent to {current.customer.name} via WhatsApp
                      </span>
                    </>
                  ) : (
                    <>
                      <span className="flex size-5 items-center justify-center rounded-full bg-neutral-200 text-neutral-700">
                        <X className="size-3" />
                      </span>
                      <span className="text-[11px] font-medium text-neutral-700">
                        Draft dismissed by associate
                      </span>
                    </>
                  )}
                </div>
                <button
                  type="button"
                  onClick={() => setDecisionState('idle')}
                  className="text-[10px] font-semibold text-primary underline"
                >
                  Reset
                </button>
              </div>
            )}

            {/* iOS Home Bar Indicator */}
            <div className="mx-auto mt-2.5 h-1 w-28 rounded-full bg-neutral-300" />
          </div>
        </div>
      </div>
    </div>
  )
}
