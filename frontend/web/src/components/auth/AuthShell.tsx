import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { motion } from 'motion/react'

import { Blossom } from './Blossom'
import { FlowerAuroraBackground } from './FlowerAuroraBackground'

export type AuthMode = 'signin' | 'signup'

const HEADLINE: Record<AuthMode, { title: string; kicker: string; blurb: string }> = {
  signin: {
    kicker: 'Welcome back',
    title: 'The concierge remembers you.',
    blurb:
      'Sign in to Aveline to keep tending your boutique, your people, and every quiet detail in between.',
  },
  signup: {
    kicker: 'Join the ecosystem',
    title: 'Cultivate your boutique with an AI concierge.',
    blurb:
      'Create your Aveline account — owners open their boutique, staff join with an invitation code.',
  },
}

/** Glass card used by both auth pages, kept behind a backdrop blur. */
export function AuthShell({ mode, children }: { mode: AuthMode; children: ReactNode }) {
  const copy = HEADLINE[mode]
  const isSignUp = mode === 'signup'

  return (
    <div className="dark relative isolate min-h-screen overflow-hidden bg-background text-foreground">
      <FlowerAuroraBackground />

      <div className="relative z-10 flex min-h-screen items-center justify-center px-4 py-12 sm:px-6">
        <motion.div
          initial={{ opacity: 0, y: 22, scale: 0.99 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ duration: 0.7, ease: [0.22, 1, 0.36, 1] }}
          className="w-full max-w-md"
        >
          {/* Brand */}
          <div className="mb-7 flex flex-col items-center text-center">
            <div className="relative mb-4">
              <div className="absolute inset-0 -m-3 rounded-full bg-[#b0566b]/25 blur-2xl" aria-hidden />
              <div className="relative flex size-16 items-center justify-center rounded-full border border-white/15 bg-white/5 text-[#ffb2bc] backdrop-blur">
                <Blossom className="size-9 drop-shadow-[0_2px_12px_rgba(255,178,188,0.6)]" />
              </div>
            </div>
            <h1 className="font-serif text-3xl font-medium tracking-tight text-white">
              Aveline
            </h1>
            <p className="mt-1 text-xs uppercase tracking-[0.35em] text-white/40">
              Boutique Concierge
            </p>
          </div>

          {/* Card */}
          <div className="relative">
            <div className="pointer-events-none absolute -inset-px rounded-3xl bg-gradient-to-b from-white/20 via-white/5 to-transparent [mask:linear-gradient(black,black)]" aria-hidden />
            <div className="relative rounded-3xl border border-white/10 bg-[#1a1114]/80 p-7 shadow-[0_30px_80px_-20px_rgba(0,0,0,0.7)] backdrop-blur-xl sm:p-8">
              <div className="mb-6 text-center">
                <p className="text-xs font-medium uppercase tracking-[0.3em] text-rose-200/70">
                  {copy.kicker}
                </p>
                <h2 className="mt-2 font-serif text-2xl font-medium leading-snug text-white">
                  {copy.title}
                </h2>
                <p className="mt-2 text-sm leading-relaxed text-white/50">{copy.blurb}</p>
              </div>

              {children}

              <div className="mt-6 border-t border-white/10 pt-5 text-center text-sm text-white/50">
                {isSignUp ? (
                  <>
                    Already have an account?{' '}
                    <Link
                      to="/sign-in"
                      className="font-medium text-rose-200/90 transition-colors hover:text-rose-100"
                    >
                      Sign in
                    </Link>
                  </>
                ) : (
                  <>
                    New to Aveline?{' '}
                    <Link
                      to="/sign-up"
                      className="font-medium text-rose-200/90 transition-colors hover:text-rose-100"
                    >
                      Create an account
                    </Link>
                  </>
                )}
              </div>
            </div>
          </div>

          <p className="mt-6 text-center text-xs text-white/30">
            Protected by Clerk · End-to-end encrypted sessions
          </p>
        </motion.div>
      </div>
    </div>
  )
}
