import { Mail, MapPin, MessageCircle } from 'lucide-react'

import { AuroraField } from '@/components/site/AuroraField'
import { Reveal } from '@/components/site/Reveal'
import { SitePage } from '@/components/site/SitePage'
import { Button } from '@/components/ui/button'

const CHANNELS = [
  {
    icon: Mail,
    label: 'Email us',
    value: 'contact@aveline.lk',
    href: 'mailto:contact@aveline.lk',
  },
  {
    icon: MessageCircle,
    label: 'WhatsApp',
    value: '+94 77 123 4567',
    href: 'https://wa.me/94771234567',
  },
  {
    icon: MapPin,
    label: 'Colombo, Sri Lanka',
    value: 'Serving boutiques across the island',
    href: null,
  },
]

export function ContactPage() {
  return (
    <SitePage>
      <section className="relative overflow-hidden">
        <AuroraField className="opacity-40" />
        <div className="relative mx-auto w-full max-w-4xl px-5 py-20 lg:px-8">
        <Reveal className="max-w-2xl">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
            Contact
          </p>
          <h1 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
            Let’s talk about your boutique.
          </h1>
          <p className="mt-4 text-lg text-neutral-500">
            Sales, support, or a quiet hello — we read everything.
          </p>
        </Reveal>

        <div className="mt-14 grid gap-4 sm:grid-cols-3">
          {CHANNELS.map(({ icon: Icon, label, value, href }, i) => (
            <Reveal key={label} delay={i * 0.06}>
              {href ? (
                <a
                  href={href}
                  className="block h-full rounded-3xl border-2 border-dashed border-neutral-200 bg-white p-6 transition-colors hover:border-commerce/40"
                >
                  <span className="flex size-10 items-center justify-center rounded-full bg-commerce/10 text-commerce">
                    <Icon className="size-5" aria-hidden />
                  </span>
                  <p className="mt-4 text-sm font-semibold text-neutral-900">{label}</p>
                  <p className="mt-1 text-sm text-commerce">{value}</p>
                </a>
              ) : (
                <div className="h-full rounded-3xl border-2 border-dashed border-neutral-200 bg-white p-6">
                  <span className="flex size-10 items-center justify-center rounded-full bg-commerce/10 text-commerce">
                    <Icon className="size-5" aria-hidden />
                  </span>
                  <p className="mt-4 text-sm font-semibold text-neutral-900">{label}</p>
                  <p className="mt-1 text-sm text-neutral-500">{value}</p>
                </div>
              )}
            </Reveal>
          ))}
        </div>

        <Reveal delay={0.1}>
          <div className="mt-14 rounded-3xl border-2 border-dashed border-lavender/40 bg-lavender-soft/60 p-8 text-center">
            <h2 className="font-serif text-2xl font-medium text-neutral-900">
              Running several boutiques?
            </h2>
            <p className="mx-auto mt-2 max-w-md text-sm text-neutral-600">
              Ask us about multi-store plans, dedicated support and onboarding for
              your whole team.
            </p>
            <Button asChild className="mt-6 h-11 px-6">
              <a href="mailto:sales@aveline.lk?subject=Multi-store%20enquiry">Email sales</a>
            </Button>
          </div>
        </Reveal>
        </div>
      </section>
    </SitePage>
  )
}
