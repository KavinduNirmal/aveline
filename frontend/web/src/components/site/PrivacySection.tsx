import { Link } from 'react-router-dom'
import { FileText, ShieldCheck, SlidersHorizontal } from 'lucide-react'

import { Reveal } from '@/components/site/Reveal'
import { Button } from '@/components/ui/button'

interface Commitment {
  icon: typeof ShieldCheck
  title: string
  copy: string
  tint: string
  ink: string
  chip: string
}

const COMMITMENTS: Commitment[] = [
  {
    icon: ShieldCheck,
    title: 'The truth, first',
    copy: 'Every first message names the boutique, says plainly that an AI assistant is helping, and says that a real member of the team reads every conversation and can step in.',
    tint: 'from-rose-50 via-[#fff1f4] to-white',
    ink: 'text-memory',
    chip: 'bg-memory/10 text-memory',
  },
  {
    icon: SlidersHorizontal,
    title: 'Opt out in one tap',
    copy: 'A permanent, signed link lets a customer stop processing for the boutique that messaged them, or for every Aveline boutique holding their number. No account, no phone call, no email.',
    tint: 'from-[#f6f2fd] via-white to-white',
    ink: 'text-lavender',
    chip: 'bg-lavender/10 text-lavender',
  },
  {
    icon: FileText,
    title: 'Copy it, or erase it',
    copy: 'A verified, self-service flow returns everything a boutique holds about a customer, or erases it. The customer proves the number once, with a one-time code, and we never ask for an account.',
    tint: 'from-[#fef8ed] via-white to-white',
    ink: 'text-visual',
    chip: 'bg-visual/10 text-visual',
  },
]

/**
 * The landing page's privacy commitment (plan §8.5.5 item 1). It sits immediately after
 * `ProblemSection`, so the narrative runs "here is the problem" into "here is our commitment".
 *
 * The copy makes no claim about Instagram: no Instagram provider ships, and the policy states that
 * Instagram is not yet covered (plan §15 Q-5). Do not add parity language here.
 */
export function PrivacySection() {
  return (
    <section id="privacy" className="relative overflow-hidden border-y-2 border-dashed border-neutral-200 bg-white py-24 sm:py-28">
      <div className="relative mx-auto w-full max-w-6xl px-5 lg:px-8">
        <div className="mx-auto max-w-2xl text-center">
          <Reveal>
            <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-primary/30 bg-primary/5 px-4 py-1.5 text-xs font-semibold uppercase tracking-[0.2em] text-primary">
              <ShieldCheck className="size-3.5" aria-hidden="true" />
              Privacy &amp; Transparency
            </span>
          </Reveal>

          <Reveal delay={0.08}>
            <h2 className="mt-5 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
              Their data, on{' '}
              <span className="bg-gradient-to-r from-primary via-[#b0566b] to-amber-700 bg-clip-text text-transparent italic">
                their terms.
              </span>
            </h2>
          </Reveal>

          <Reveal delay={0.16}>
            <p className="mt-5 text-base leading-relaxed text-neutral-600 sm:text-lg">
              Remembering someone is only a kindness if they know it is happening and can say stop.
              These are the three promises behind every message Aveline helps a boutique send.
            </p>
          </Reveal>
        </div>

        <ul className="mt-16 grid gap-8 md:grid-cols-3">
          {COMMITMENTS.map((item, index) => {
            const Icon = item.icon
            return (
              <li key={item.title} className="h-full">
                <Reveal delay={0.1 + index * 0.08} className="h-full">
                  <div
                    className={`flex h-full flex-col rounded-2xl border-2 border-dashed border-neutral-200/80 bg-gradient-to-b p-8 shadow-[0_20px_50px_-30px_rgba(139,46,66,0.12)] ${item.tint}`}
                  >
                    <span className="flex size-12 items-center justify-center rounded-xl border border-neutral-200/60 bg-white shadow-xs">
                      <Icon className={`size-6 ${item.ink}`} aria-hidden="true" />
                    </span>
                    <h3 className="mt-6 font-serif text-2xl font-medium leading-snug text-neutral-900">
                      {item.title}
                    </h3>
                    <p className="mt-3.5 text-sm leading-relaxed text-neutral-600 sm:text-base">
                      {item.copy}
                    </p>
                    <span
                      aria-hidden="true"
                      className={`mt-auto pt-6 text-xs font-semibold uppercase tracking-[0.18em] ${item.chip} w-fit rounded-full px-3 py-1`}
                    >
                      0{index + 1}
                    </span>
                  </div>
                </Reveal>
              </li>
            )
          })}
        </ul>

        <Reveal delay={0.3}>
          <div className="mt-12 flex flex-col items-center justify-center gap-3 sm:flex-row">
            <Button asChild size="lg">
              <Link to="/privacy">Read Our Data Policy</Link>
            </Button>
            <Button asChild variant="outline" size="lg">
              <Link to="/privacy/consent-flow">View Consent Flow</Link>
            </Button>
          </div>
        </Reveal>
      </div>
    </section>
  )
}
