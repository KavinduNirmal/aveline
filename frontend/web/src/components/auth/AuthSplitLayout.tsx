import type { ReactNode } from 'react'
import {
  Bot,
  type LucideIcon,
  ShieldCheck,
  Sparkles,
  UsersRound,
} from 'lucide-react'
import { motion } from 'motion/react'

import { Blossom } from './Blossom'
import { FlowerAuroraBackground } from './FlowerAuroraBackground'

export type AuthMode = 'signin' | 'signup'

const COPY: Record<
  AuthMode,
  { kicker: string; title: string; sub: string }
> = {
  signin: {
    kicker: 'Welcome back',
    title: 'Sign in to Aveline',
    sub: 'Aveline remembers — pick up right where you left off.',
  },
  signup: {
    kicker: 'Join the ecosystem',
    title: 'Create your account',
    sub: 'Owners open their boutique. Staff join with an invitation code.',
  },
}

const PERKS: { icon: LucideIcon; k: string; v: string }[] = [
  { icon: Bot, k: 'Boutique AI', v: 'Orders, sourcing & replies in one place.' },
  { icon: UsersRound, k: 'People-first', v: 'Roles and permissions for owners & staff.' },
  { icon: Sparkles, k: 'Relational memory', v: 'Every client remembered, warmly.' },
  { icon: ShieldCheck, k: 'Secure by design', v: 'Clerk-managed sessions, always.' },
]

const STORY =
  'Aveline is the assistant that remembers, so you don’t have to. It keeps '
    .concat('orders, WhatsApp threads, sourcing and client history moving — ' +
      'so you can stay close and personal with every customer, without ' +
      'worrying about the little things.')

/** Two-panel auth layout (Next.js-style dashed borders): left brand/art +
 *  story panel, right edge-to-edge form column with a docked footer. */
export function AuthSplitLayout({
  mode,
  children,
  footer,
}: {
  mode: AuthMode
  children: ReactNode
  footer?: ReactNode
}) {
  const copy = COPY[mode]

  return (
    <div className="dark relative isolate min-h-screen overflow-hidden bg-background text-foreground">
      <FlowerAuroraBackground />

      <div className="relative z-10 grid min-h-screen lg:grid-cols-[1.1fr_minmax(430px,0.9fr)]">
        {/* Left — brand + story */}
        <motion.section
          initial={{ opacity: 0, x: -24 }}
          animate={{ opacity: 1, x: 0 }}
          transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
          className="relative hidden flex-col justify-between p-10 xl:p-14 lg:flex"
        >
          <div className="flex items-center gap-3">
            <span className="flex size-10 items-center justify-center rounded-full border border-dashed border-rose-200/40 bg-white/5 text-rose-100 backdrop-blur">
              <Blossom className="size-6" />
            </span>
            <div className="leading-tight">
              <p className="font-serif text-lg font-medium text-white">Aveline</p>
              <p className="text-[10px] uppercase tracking-[0.3em] text-white/40">
                The assistant that remembers
              </p>
            </div>
          </div>

          <div className="max-w-xl">
            <p className="mb-4 text-[13px] font-medium uppercase tracking-[0.3em] text-rose-200/80">
              Aveline remembers
            </p>
            <h1 className="font-serif text-4xl font-medium leading-[1.15] tracking-tight text-white xl:text-[2.9rem]">
              So you don’t have to. Every client,{' '}
              <span className="bg-gradient-to-r from-rose-100 via-rose-200 to-[#e0b64c] bg-clip-text text-transparent">
                remembered personally.
              </span>
            </h1>
            <p className="mt-6 max-w-md text-base leading-relaxed text-white/65">
              {STORY}
            </p>
          </div>

          {/* Feature list (no cards — dividers between rows) */}
          <div className="flex max-w-md flex-col">
            {PERKS.map(({ icon: Icon, k, v }) => (
              <div
                key={k}
                className="flex items-center gap-4 py-3.5 [&:not(:last-child)]:border-b-2 [&:not(:last-child)]:border-dashed [&:not(:last-child)]:border-white/15"
              >
                <span className="flex size-9 shrink-0 items-center justify-center rounded-full bg-white/[0.06] text-rose-200/90">
                  <Icon className="size-[18px]" aria-hidden />
                </span>
                <span className="flex min-w-0 flex-col">
                  <span className="text-[15px] font-semibold text-white">{k}</span>
                  <span className="text-sm leading-snug text-white/55">{v}</span>
                </span>
              </div>
            ))}
          </div>

          <p className="text-[13px] text-white/40">
            Owner &amp; staff workspaces · Sri Lanka · Clerk-secured
          </p>
        </motion.section>

        {/* Right — full-height form column */}
        <section className="relative flex min-h-screen flex-col bg-[#140b0e]/75 backdrop-blur-2xl lg:border-l-2 lg:border-dashed lg:border-white/15">
          {/* Mobile brand */}
          <div className="flex items-center justify-between border-b border-dashed border-white/10 px-6 py-4 lg:hidden">
            <div className="flex items-center gap-2">
              <span className="flex size-8 items-center justify-center rounded-full border border-dashed border-rose-200/40 text-rose-100">
                <Blossom className="size-5" />
              </span>
              <span className="font-serif text-lg font-medium text-white">Aveline</span>
            </div>
            <p className="text-[10px] uppercase tracking-[0.3em] text-white/40">
              The assistant that remembers
            </p>
          </div>

          {/* Scrollable form area */}
          <div className="flex-1 overflow-y-auto">
            <div className="mx-auto flex w-full max-w-[440px] flex-col px-6 py-8 sm:py-12">
              <p className="text-[13px] font-medium uppercase tracking-[0.3em] text-rose-200/80">
                {copy.kicker}
              </p>
              <h2 className="mt-2 font-serif text-4xl font-medium tracking-tight text-white">
                {copy.title}
              </h2>
              <p className="mt-3 text-base leading-relaxed text-white/65">{copy.sub}</p>

              <div className="mt-7">{children}</div>
            </div>
          </div>

          {/* Docked footer */}
          {footer && (
            <div className="border-t-2 border-dashed border-white/15 bg-[#0f080a]/40 px-6 py-4 backdrop-blur">
              <div className="mx-auto w-full max-w-[440px]">{footer}</div>
            </div>
          )}
        </section>
      </div>
    </div>
  )
}
