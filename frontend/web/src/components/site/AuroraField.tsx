import type { CSSProperties } from 'react'
import { useReducedMotion } from 'motion/react'

import { Blossom } from '@/components/auth/Blossom'
import { useConstrainedDevice } from '@/hooks/useConstrainedDevice'
import { cn } from '@/lib/utils'

interface Blob {
  gradient: string
  left: number
  top: number
  size: number
  duration: number
  delay: number
  opacity: number
}

/** Soft aurora gradient blobs in the landing palette. */
const BLOBS: Blob[] = [
  { gradient: 'radial-gradient(circle at 30% 30%, #ffd9dd, #ffe9f0 60%, transparent 75%)', left: 0, top: -10, size: 720, duration: 22, delay: 0, opacity: 0.9 },
  { gradient: 'radial-gradient(circle at 60% 40%, #f0eafa, #e6dcf7 55%, transparent 75%)', left: 50, top: -4, size: 700, duration: 28, delay: 2, opacity: 0.9 },
  { gradient: 'radial-gradient(circle at 40% 50%, #ffe9e2, #ffdcc9 55%, transparent 75%)', left: 30, top: 40, size: 640, duration: 25, delay: 1, opacity: 0.85 },
  { gradient: 'radial-gradient(circle at 30% 60%, #fdf0d8, #fae6b8 55%, transparent 75%)', left: 70, top: 34, size: 560, duration: 24, delay: 3, opacity: 0.8 },
  { gradient: 'radial-gradient(circle at 50% 50%, #fce3ec, #f7cfe0 55%, transparent 75%)', left: 10, top: 60, size: 620, duration: 30, delay: 4, opacity: 0.85 },
  { gradient: 'radial-gradient(circle at 40% 30%, #eef6ff, #e0efff 60%, transparent 75%)', left: 82, top: 58, size: 480, duration: 26, delay: 2.5, opacity: 0.7 },
]

interface Flower {
  left: number
  top: number
  size: number
  color: string
  drift: number
  duration: number
  delay: number
  opacity: number
}

const FLOWER_COLORS = ['#b0566b', '#c9972b', '#8e7cc3', '#ef7a68', '#d46a8b']

const FLOWERS: Flower[] = Array.from({ length: 14 }).map((_, i) => ({
  left: (i * 37 + 9) % 96,
  top: (i * 53 + 13) % 92,
  size: 18 + ((i * 7) % 30),
  color: FLOWER_COLORS[i % FLOWER_COLORS.length],
  drift: 14 + ((i * 5) % 18),
  duration: 20 + ((i * 3) % 18),
  delay: -((i * 2.3) % 16),
  opacity: 0.5 + ((i % 4) * 0.1),
}))

/**
 * Colorful, animated aurora + drifting blossoms used behind marketing sections.
 *
 * The motion is **CSS**, not JavaScript. These were `motion.div`s — 6 blurred layers and 14
 * blossoms per instance, and the landing page mounts seven of them, so ~140 JavaScript animations
 * ran forever and drove every frame from the main thread. Measured on the Lighthouse mobile
 * profile with the animation switched off, that budget accounted for 2,610 ms of the landing
 * page's 3,240 ms of main-thread busy and 71 of its 80 long tasks. The same keyframes now live in
 * `src/index.css` and run on the compositor.
 *
 * Motion is dropped — the blobs still render, the blossoms do not — for a reader who prefers
 * reduced motion and for a device that cannot afford it (`useConstrainedDevice`: `saveData`, the
 * entry-level memory band, or a 2G-class connection). This is a brake on the animation budget,
 * not a removal of the page's colour.
 */
export function AuroraField({ className }: { className?: string }) {
  const prefersReduced = useReducedMotion()
  const constrained = useConstrainedDevice()
  const staticOnly = prefersReduced || constrained

  return (
    <div aria-hidden className={`pointer-events-none absolute inset-0 overflow-hidden ${className ?? ''}`}>
      {BLOBS.map((b, i) => (
        <div
          key={i}
          // `cn`, not a template literal: `blur-[70px]${...}` glues the candidate to the
          // interpolation, Tailwind cannot extract it, and the class is silently never emitted —
          // which is exactly how this shipped as an unblurred circle once. The utility must stay
          // a standalone string literal.
          className={cn('absolute rounded-full blur-[70px]', !staticOnly && 'aveline-aurora-blob')}
          style={
            {
              background: b.gradient,
              width: b.size,
              height: b.size,
              left: `${b.left}%`,
              top: `${b.top}%`,
              opacity: b.opacity,
              '--aurora-duration': `${b.duration}s`,
              '--aurora-delay': `${b.delay}s`,
            } as CSSProperties
          }
        />
      ))}

      {!staticOnly &&
        FLOWERS.map((f, i) => (
          <div
            key={i}
            className="aveline-blossom-drift absolute"
            style={
              {
                left: `${f.left}%`,
                top: `${f.top}%`,
                width: f.size,
                height: f.size,
                color: f.color,
                opacity: f.opacity,
                // Same per-flower values Framer was handed, now carried as custom properties.
                '--blossom-drift-x': `${Math.round(Math.sin(i) * 26)}px`,
                '--blossom-drift-y': `${f.drift}px`,
                '--blossom-duration': `${f.duration}s`,
                '--blossom-delay': `${f.delay}s`,
              } as CSSProperties
            }
          >
            <Blossom animateCounter counterDuration={14 + (i % 8) * 2} className="size-full drop-shadow-[0_4px_12px_rgba(176,86,107,0.25)]" />
          </div>
        ))}
    </div>
  )
}
