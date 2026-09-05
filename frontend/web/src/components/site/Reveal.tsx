import type { CSSProperties, ReactNode } from 'react'
import { motion, useReducedMotion } from 'motion/react'

/** Scroll-reveal wrapper for landing sections. */
export function Reveal({
  children,
  className,
  delay = 0,
}: {
  children: ReactNode
  className?: string
  delay?: number
}) {
  const reduce = useReducedMotion()
  if (reduce) {
    return <div className={className}>{children}</div>
  }
  return (
    <motion.div
      className={className}
      initial={{ opacity: 0, y: 28 }}
      whileInView={{ opacity: 1, y: 0 }}
      viewport={{ once: true, margin: '-80px' }}
      transition={{ duration: 0.7, delay, ease: [0.22, 1, 0.36, 1] }}
    >
      {children}
    </motion.div>
  )
}

interface Blob {
  color: string
  left: number
  top: number
  size: number
  duration: number
  delay: number
}

const BLOBS: Blob[] = [
  { color: '#ffd9dd', left: 8, top: -6, size: 620, duration: 24, delay: 0 },
  { color: '#f0eafa', left: 58, top: 4, size: 560, duration: 30, delay: 2 },
  { color: '#ffe3d6', left: 40, top: 46, size: 520, duration: 26, delay: 1 },
  { color: '#fdeed2', left: 82, top: 30, size: 420, duration: 22, delay: 3 },
  { color: '#fce3ec', left: 12, top: 58, size: 460, duration: 28, delay: 4 },
]

const PETAL_COLORS = ['#b0566b', '#c9972b', '#8e7cc3', '#ef7a68']

/** Colorful animated aurora (light theme) for marketing hero/CTA sections. */
export function AuroraField({ className }: { className?: string }) {
  const reduce = useReducedMotion()

  const style = (b: Blob): CSSProperties => ({
    background: b.color,
    width: b.size,
    height: b.size,
    left: `${b.left}%`,
    top: `${b.top}%`,
  })

  return (
    <div aria-hidden className={`pointer-events-none absolute inset-0 overflow-hidden ${className ?? ''}`}>
      {BLOBS.map((b, i) => (
        <motion.div
          key={i}
          className="absolute rounded-full blur-[90px]"
          style={style(b)}
          animate={
            reduce
              ? { opacity: 0.9 }
              : { opacity: [0.85, 1, 0.9], x: [0, 40, -20, 0], y: [0, -30, 20, 0], scale: [1, 1.08, 0.98, 1] }
          }
          transition={{ duration: b.duration, delay: b.delay, repeat: Infinity, ease: 'easeInOut' }}
        />
      ))}

      {!reduce &&
        Array.from({ length: 10 }).map((_, i) => (
          <motion.span
            key={`petal-${i}`}
            className="absolute block rounded-full"
            style={{
              left: `${(i * 11 + 4) % 96}%`,
              top: `${(i * 17) % 92}%`,
              width: 5 + (i % 4),
              height: 5 + (i % 4),
              background: PETAL_COLORS[i % PETAL_COLORS.length],
              opacity: 0.4,
            }}
            animate={{ y: [0, -18, 0], opacity: [0.2, 0.6, 0.2], scale: [1, 1.25, 1] }}
            transition={{
              duration: 9 + (i % 6),
              delay: i * 0.7,
              repeat: Infinity,
              ease: 'easeInOut',
            }}
          />
        ))}
    </div>
  )
}
