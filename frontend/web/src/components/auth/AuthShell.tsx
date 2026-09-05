import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'
import { motion } from 'motion/react'

import { Blossom } from './Blossom'
import { FlowerAuroraBackground } from './FlowerAuroraBackground'

export type AuthMode = 'signin' | 'signup'

const HEADLINE: Record<AuthMode, { kicker: string; title: string }> = {
  signin: {
    kicker: 'Welcome back',
    title: 'The concierge remembers you.',
  },
  signup: {
    kicker: 'Join the ecosystem',
    title: 'Cultivate your boutique with an AI concierge.',
  },
}

/**
 * Shared auth layout: flower/aurora art on the right-ish glow, compact glass
 * card. `accent` toggles between the dark brand art (public self-service) and a
 * quieter treatment (admin sign-up).
 */
export function AuthShell({
  mode,
  children,
  quiet = false,
}: {
  mode: AuthMode
  children: ReactNode
  quiet?: boolean
}) {
  const copy = HEADLINE[mode]
  const isSignUp = mode === 'signup'

  return (
    <div
      className={
        quiet
          ? 'relative min-h-screen bg-[#faf7f6] text-neutral-900'
          : 'dark relative isolate min-h-screen overflow-hidden bg-background text-foreground'
      }
    >
      {!quiet && <FlowerAuroraBackground />}

      <div
        className={
          quiet
            ? 'relative z-10 flex min-h-screen items-center justify-center px-4 py-10 sm:px-6'
            : 'relative z-10 flex min-h-screen items-center justify-center px-4 py-8 sm:px-6'
        }
      >
        <motion.div
          initial={{ opacity: 0, y: 18, scale: 0.995 }}
          animate={{ opacity: 1, y: 0, scale: 1 }}
          transition={{ duration: 0.55, ease: [0.22, 1, 0.36, 1] }}
          className="w-full max-w-[400px]"
        >
          {/* Brand */}
          <div className="mb-5 flex flex-col items-center text-center">
            <div className="relative mb-3">
              {!quiet && (
                <div
                  className="absolute inset-0 -m-2 rounded-full bg-[#b0566b]/25 blur-xl"
                  aria-hidden
                />
              )}
              <div
                className={
                  quiet
                    ? 'relative flex size-12 items-center justify-center rounded-full bg-[#7a303f]/10 text-[#7a303f]'
                    : 'relative flex size-12 items-center justify-center rounded-full border border-white/15 bg-white/5 text-[#ffb2bc] backdrop-blur'
                }
              >
                <Blossom
                  className={
                    quiet
                      ? 'size-7'
                      : 'size-7 drop-shadow-[0_2px_10px_rgba(255,178,188,0.5)]'
                  }
                />
              </div>
            </div>
            <h1
              className={
                quiet
                  ? 'font-serif text-2xl font-medium tracking-tight text-neutral-900'
                  : 'font-serif text-2xl font-medium tracking-tight text-white'
              }
            >
              Aveline
            </h1>
            <p
              className={
                quiet
                  ? 'mt-0.5 text-[11px] uppercase tracking-[0.32em] text-neutral-500'
                  : 'mt-0.5 text-[11px] uppercase tracking-[0.32em] text-white/40'
              }
            >
              Atelier Concierge
            </p>
          </div>

          {/* Card */}
          <div className="relative">
            {!quiet && (
              <div
                className="pointer-events-none absolute -inset-px rounded-2xl bg-gradient-to-b from-white/20 via-white/5 to-transparent [mask:linear-gradient(black,black)]"
                aria-hidden
              />
            )}
            <div
              className={
                quiet
                  ? 'relative rounded-2xl border border-neutral-200 bg-white p-6 shadow-sm'
                  : 'relative rounded-2xl border border-white/10 bg-[#1a1114]/85 p-6 shadow-[0_30px_80px_-20px_rgba(0,0,0,0.7)] backdrop-blur-xl'
              }
            >
              <div className="mb-5 text-center">
                <p
                  className={
                    quiet
                      ? 'text-[11px] font-medium uppercase tracking-[0.28em] text-neutral-500'
                      : 'text-[11px] font-medium uppercase tracking-[0.28em] text-rose-200/70'
                  }
                >
                  {copy.kicker}
                </p>
                <h2
                  className={
                    quiet
                      ? 'mt-1.5 font-serif text-[22px] font-medium leading-snug text-neutral-900'
                      : 'mt-1.5 font-serif text-[22px] font-medium leading-snug text-white'
                  }
                >
                  {copy.title}
                </h2>
              </div>

              {children}

              <div
                className={
                  quiet
                    ? 'mt-5 border-t border-neutral-200 pt-4 text-center text-sm text-neutral-500'
                    : 'mt-5 border-t border-white/10 pt-4 text-center text-sm text-white/50'
                }
              >
                {isSignUp ? (
                  <>
                    Already have an account?{' '}
                    <Link
                      to="/sign-in"
                      className={
                        quiet
                          ? 'font-medium text-[#7a303f] hover:underline'
                          : 'font-medium text-rose-200/90 transition-colors hover:text-rose-100'
                      }
                    >
                      Sign in
                    </Link>
                  </>
                ) : (
                  <>
                    New to Aveline?{' '}
                    <Link
                      to="/sign-up"
                      className={
                        quiet
                          ? 'font-medium text-[#7a303f] hover:underline'
                          : 'font-medium text-rose-200/90 transition-colors hover:text-rose-100'
                      }
                    >
                      Create an account
                    </Link>
                  </>
                )}
              </div>

              {/* Unassuming admin entry */}
              <p
                className={
                  quiet
                    ? 'mt-4 text-center text-xs text-neutral-400'
                    : 'mt-4 text-center text-xs text-white/30'
                }
              >
                <Link
                  to="/sign-up/admin"
                  className="transition-opacity hover:opacity-80"
                >
                  Administrator sign-up
                </Link>
              </p>
            </div>
          </div>
        </motion.div>
      </div>
    </div>
  )
}
