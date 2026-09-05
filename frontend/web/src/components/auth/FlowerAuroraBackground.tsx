import { useMemo } from 'react'
import { motion, useReducedMotion } from 'motion/react'

import { Blossom } from './Blossom'

interface FlowerSeed {
  x: number // percent
  y: number
  size: number // px
  duration: number
  delay: number
  drift: number
  color: string
  rotate: number
}

function randomSeeds(count: number, palette: string[]): FlowerSeed[] {
  const seeds: FlowerSeed[] = []
  for (let i = 0; i < count; i++) {
    seeds.push({
      x: Math.random() * 100,
      y: Math.random() * 100,
      size: 14 + Math.random() * 34,
      duration: 22 + Math.random() * 26,
      delay: -Math.random() * 30,
      drift: 8 + Math.random() * 22,
      color: palette[Math.floor(Math.random() * palette.length)],
      rotate: Math.random() * 360,
    })
  }
  return seeds
}

const FLOWER_PALETTE = ['#ffb2bc', '#b0566b', '#c9a227', '#d9a7b0', '#8f5b63']
const ORB_PALETTE = ['#7a303f', '#b0566b', '#5d1a29', '#c9a227']

/**
 * Full-bleed animated background for the auth screens: deep warm base, drifting
 * aurora orbs, slowly swaying/falling blossoms and a faint film grain.
 * Motion is disabled for users who prefer reduced motion.
 */
export function FlowerAuroraBackground() {
  const reduceMotion = useReducedMotion()
  const flowers = useMemo(() => randomSeeds(14, FLOWER_PALETTE), [])

  const base = {
    opacity: 1,
    y: 0,
    x: 0,
    rotate: 0,
  }

  return (
    <div aria-hidden className="pointer-events-none absolute inset-0 overflow-hidden">
      {/* Warm dark base */}
      <div className="absolute inset-0 bg-[#140c0e]" />

      {/* Aurora orbs */}
      {ORB_PALETTE.map((color, i) => (
        <motion.div
          key={color}
          className="absolute rounded-full blur-[110px]"
          style={{
            background: color,
            width: `${34 + i * 8}vw`,
            height: `${34 + i * 8}vw`,
            left: `${[10, 55, 30, 70][i] ?? 40}%`,
            top: `${[5, 40, 78, 25][i] ?? 50}%`,
            opacity: 0.28,
          }}
          animate={
            reduceMotion
              ? base
              : {
                  y: [-40, 60, -30, 0],
                  x: [-30, 40, 20, -10],
                  scale: [1, 1.18, 0.96, 1.06],
                }
          }
          transition={{
            duration: 26 + i * 7,
            repeat: Infinity,
            ease: 'easeInOut',
          }}
        />
      ))}

      {/* Flowers */}
      {!reduceMotion &&
        flowers.map((f, i) => (
          <motion.div
            key={i}
            className="absolute"
            style={{
              left: `${f.x}%`,
              top: `${f.y}%`,
              width: f.size,
              height: f.size,
              color: f.color,
              filter: 'blur(0.4px)',
              opacity: 0.85,
            }}
            animate={{
              y: [0, -f.drift, 0],
              x: [0, Math.sin(i) * f.drift * 0.6, 0],
              rotate: [f.rotate, f.rotate + 18, f.rotate],
              scale: [1, 1.04, 1],
            }}
            transition={{
              duration: f.duration,
              delay: f.delay,
              repeat: Infinity,
              ease: 'easeInOut',
            }}
          >
            <Blossom className="size-full drop-shadow-[0_2px_10px_rgba(176,86,107,0.35)]" />
          </motion.div>
        ))}

      {/* Slow-rising sparkle petals */}
      {!reduceMotion &&
        flowers.slice(0, 6).map((_, i) => (
          <motion.span
            key={`petal-${i}`}
            className="absolute block rounded-full"
            style={{
              left: `${20 + i * 14}%`,
              width: 6 + i,
              height: 6 + i,
              background: FLOWER_PALETTE[i % FLOWER_PALETTE.length],
              opacity: 0.5,
            }}
            animate={{ y: ['110vh', '-10vh'], x: [0, (i - 3) * 40] }}
            transition={{
              duration: 26 + i * 5,
              delay: -i * 7,
              repeat: Infinity,
              ease: 'linear',
            }}
          />
        ))}

      {/* Film grain */}
      <div className="absolute inset-0 opacity-[0.05] mix-blend-overlay">
        <svg className="size-full">
          <filter id="noise">
            <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" stitchTiles="stitch" />
          </filter>
          <rect width="100%" height="100%" filter="url(#noise)" />
        </svg>
      </div>

      {/* Vignette for the form column */}
      <div className="absolute inset-0 bg-[radial-gradient(120%_90%_at_100%_0%,transparent_35%,rgba(20,8,10,0.65)_100%)]" />
    </div>
  )
}
