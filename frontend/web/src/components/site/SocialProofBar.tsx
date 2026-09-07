import { Sparkles } from 'lucide-react'

const ATELIERS = [
  'The Galle Fort Atelier',
  'Cinnamon Row Tailors',
  'Studio 9 Colombo',
  'Maison Kandy',
  'Ceylon Mulberry Silk Co.',
  'Horton Couture',
]

const STATS = [
  {
    value: '40+',
    label: 'Premier Ateliers',
    desc: 'Across Sri Lanka & Region',
  },
  {
    value: '100%',
    label: 'Staff Sign-Off',
    desc: 'Zero unreviewed auto-replies',
  },
  {
    value: 'LKR 45M+',
    label: 'Concierge Volume',
    desc: 'Intimate orders tended',
  },
  {
    value: '4.9 / 5',
    label: 'Boutique Rating',
    desc: 'Owner satisfaction score',
  },
]

export function SocialProofBar() {
  return (
    <div className="border-y-2 border-dashed border-neutral-200 bg-white/75 py-9 backdrop-blur-md">
      <div className="mx-auto w-full max-w-6xl px-5 lg:px-8">
        {/* Atelier Names Ticker / Logos */}
        <div className="flex flex-col items-center justify-between gap-6 md:flex-row">
          <div className="flex items-center gap-2 text-xs font-semibold uppercase tracking-[0.2em] text-neutral-400">
            <Sparkles className="size-3.5 text-primary" />
            <span>Trusted By Renowned Boutiques</span>
          </div>

          <div className="flex flex-wrap items-center justify-center gap-x-8 gap-y-3">
            {ATELIERS.map((atelier) => (
              <span
                key={atelier}
                className="font-serif text-sm font-medium tracking-wide text-neutral-600 transition-colors hover:text-primary"
              >
                {atelier}
              </span>
            ))}
          </div>
        </div>

        {/* Highlight Metrics — Bigger and more prominent */}
        <div className="mt-10 grid grid-cols-2 gap-8 border-t-2 border-dashed border-neutral-200 pt-9 sm:grid-cols-4">
          {STATS.map((s) => (
            <div key={s.label} className="text-center sm:text-left">
              <p className="font-serif text-4xl font-semibold tracking-tight text-primary sm:text-5xl lg:text-6xl">
                {s.value}
              </p>
              <p className="mt-2 text-sm font-semibold text-neutral-900 sm:text-base">
                {s.label}
              </p>
              <p className="mt-0.5 text-xs text-neutral-500">
                {s.desc}
              </p>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
