import type { CSSProperties } from 'react'
import { motion, useReducedMotion } from 'motion/react'

import { Blossom } from '@/components/auth/Blossom'

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
 * Motion is disabled for users who prefer reduced motion.
 */
export function AuroraField({ className }: { className?: string }) {
  const reduce = useReducedMotion()

  const blobStyle = (b: Blob): CSSProperties => ({
    background: b.gradient,
    width: b.size,
    height: b.size,
    left: `${b.left}%`,
    top: `${b.top}%`,
    opacity: b.opacity,
  })

  return (
    <div aria-hidden className={`pointer-events-none absolute inset-0 overflow-hidden ${className ?? ''}`}>
      {BLOBS.map((b, i) => (
        <motion.div
          key={i}
          className="absolute rounded-full blur-[70px]"
          style={blobStyle(b)}
          animate={
            reduce
              ? {}
              : {
                  x: [0, 70, -40, 0],
                  y: [0, -50, 30, 0],
                  scale: [1, 1.15, 0.95, 1],
                }
          }
          transition={{ duration: b.duration, delay: b.delay, repeat: Infinity, ease: 'easeInOut' }}
        />
      ))}

      {!reduce &&
        FLOWERS.map((f, i) => (
          <motion.div
            key={i}
            className="absolute"
            style={{
              left: `${f.left}%`,
              top: `${f.top}%`,
              width: f.size,
              height: f.size,
              color: f.color,
              opacity: f.opacity,
            }}
            animate={{
              y: [0, -f.drift, 0],
              x: [0, Math.sin(i) * 26, 0],
              rotate: [0, 40, 0],
              scale: [1, 1.08, 1],
            }}
            transition={{
              duration: f.duration,
              delay: f.delay,
              repeat: Infinity,
              ease: 'easeInOut',
            }}
          >
            <Blossom animateCounter counterDuration={14 + (i % 8) * 2} className="size-full drop-shadow-[0_4px_12px_rgba(176,86,107,0.25)]" />
          </motion.div>
        ))}
    </div>
  )
}
