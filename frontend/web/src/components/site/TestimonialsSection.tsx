import { Blossom } from '@/components/auth/Blossom'
import { Reveal } from '@/components/site/Reveal'
import { Star, CheckCircle } from 'lucide-react'

interface Testimonial {
  name: string
  role: string
  atelier: string
  avatarUrl: string
  quote: string
  metric: string
  metricLabel: string
  tag: string
}

const TESTIMONIALS: Testimonial[] = [
  {
    name: 'Nisansala Jayawardena',
    role: 'Founder & Creative Director',
    atelier: 'The Galle Fort Atelier',
    avatarUrl: 'https://images.pexels.com/photos/1181686/pexels-photo-1181686.jpeg?auto=compress&cs=tinysrgb&w=150',
    quote:
      'Before Aveline, my phone was a cemetery of unread WhatsApp inquiries. Now Ava summarizes each client’s history and Elle curates outfit options before staff even draft a reply.',
    metric: '3.4x',
    metricLabel: 'Faster client response rate',
    tag: 'Silk & Couture',
  },
  {
    name: 'Tariq Al-Mansoor',
    role: 'Principal Tailor & Partner',
    atelier: 'Cinnamon Row Tailors',
    avatarUrl: 'https://images.pexels.com/photos/2379004/pexels-photo-2379004.jpeg?auto=compress&cs=tinysrgb&w=150',
    quote:
      'Lina caught three unapproved discounts on bespoke linen suits in our first fortnight alone. Having every commerce proposal wait for my 1-tap sign-off protected our atelier’s margins.',
    metric: '18%',
    metricLabel: 'Margin leakage eliminated',
    tag: 'Bespoke Tailoring',
  },
  {
    name: 'Devindi Senanayake',
    role: 'Head of Styling',
    atelier: 'Studio Nine Colombo',
    avatarUrl: 'https://images.pexels.com/photos/1587009/pexels-photo-1587009.jpeg?auto=compress&cs=tinysrgb&w=150',
    quote:
      'Elle’s visual matching feels like genuine fashion intuition. A client sends a photo of an event invitation or Pinterest silhouette, and Elle pairs the right pieces in seconds.',
    metric: '88%',
    metricLabel: 'First-recommendation acceptance',
    tag: 'Luxury Ready-To-Wear',
  },
]

export function TestimonialsSection() {
  return (
    <section className="relative overflow-hidden py-24 sm:py-28">
      <div className="relative mx-auto w-full max-w-6xl px-5 lg:px-8">
        {/* Section Header */}
        <div className="mx-auto max-w-2xl text-center">
          <Reveal>
            <span className="inline-flex items-center gap-2 rounded-full border border-dashed border-primary/30 bg-primary/5 px-4 py-1.5 text-xs font-semibold uppercase tracking-[0.2em] text-primary">
              <Blossom animateCounter counterDuration={15} className="size-3.5 text-primary" />
              Stories From The Floor
            </span>
          </Reveal>

          <Reveal delay={0.08}>
            <h2 className="mt-5 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl lg:text-6xl">
              Loved by directors,{' '}
              <span className="bg-gradient-to-r from-primary via-[#b0566b] to-amber-700 bg-clip-text text-transparent italic">
                trusted by associates.
              </span>
            </h2>
          </Reveal>

          <Reveal delay={0.16}>
            <p className="mt-5 text-base leading-relaxed text-neutral-600 sm:text-lg">
              Hear how Sri Lanka’s premier fashion ateliers turned chaotic message threads
              into elevated, profitable personal shopping rituals.
            </p>
          </Reveal>
        </div>

        {/* Whimsical Testimonial Cards */}
        <div className="mt-16 grid gap-7 md:grid-cols-3">
          {TESTIMONIALS.map((t, i) => (
            <Reveal key={t.name} delay={0.12 + i * 0.1} className="h-full">
              <div className="group relative flex h-full flex-col overflow-hidden rounded-3xl border-2 border-dashed border-neutral-200/90 bg-white p-7 shadow-[0_25px_60px_-35px_rgba(139,46,66,0.15)] transition-all duration-300 hover:-translate-y-1 hover:border-primary/40 hover:shadow-[0_30px_70px_-30px_rgba(139,46,66,0.22)]">
                {/* Five gold stars */}
                <div className="flex items-center gap-1 text-amber-500">
                  {Array.from({ length: 5 }).map((_, starIndex) => (
                    <Star key={starIndex} className="size-4 fill-amber-400 text-amber-400" />
                  ))}
                </div>

                {/* Quote */}
                <p className="mt-5 text-sm leading-relaxed text-neutral-700 italic">
                  “{t.quote}”
                </p>

                {/* Stat Metric Pill */}
                <div className="mt-6 flex items-center justify-between rounded-2xl border border-dashed border-primary/25 bg-[#fff8f9] p-3">
                  <div>
                    <p className="font-serif text-2xl font-bold text-primary">{t.metric}</p>
                    <p className="text-[10px] uppercase tracking-wider text-neutral-500">
                      {t.metricLabel}
                    </p>
                  </div>
                  <span className="rounded-full bg-primary/10 px-2.5 py-1 text-[10px] font-semibold text-primary">
                    {t.tag}
                  </span>
                </div>

                {/* Author Info */}
                <div className="mt-6 flex items-center gap-3 border-t border-dashed border-neutral-200/80 pt-5">
                  <img
                    src={t.avatarUrl}
                    alt={t.name}
                    className="size-11 rounded-full object-cover ring-2 ring-primary/20"
                  />
                  <div>
                    <div className="flex items-center gap-1.5">
                      <p className="font-serif text-sm font-semibold text-neutral-900 leading-none">
                        {t.name}
                      </p>
                      <CheckCircle className="size-3.5 text-primary fill-primary/15" />
                    </div>
                    <p className="mt-1 text-[11px] text-neutral-500">{t.role}</p>
                    <p className="text-[10px] font-medium text-primary/80">{t.atelier}</p>
                  </div>
                </div>
              </div>
            </Reveal>
          ))}
        </div>
      </div>
    </section>
  )
}
