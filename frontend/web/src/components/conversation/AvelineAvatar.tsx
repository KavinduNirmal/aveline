import { motion } from 'motion/react'

import { Blossom } from '@/components/auth/Blossom'
import { cn } from '@/lib/utils'
import { avelineStateConfig, type AvelineState } from './avelineStates'

interface AvelineAvatarProps {
  /** The agentic-workflow state driving the animation. Defaults to `idle`. */
  state?: AvelineState
  className?: string
  /** Blossom size in Tailwind size units (e.g. `size-5`). */
  blossomClassName?: string
}

/**
 * Aveline's avatar: the Blossom mark, always animated (colour cycle + slow rotation).
 * No background circle - the blossom is the icon itself. The animation behaviour is driven
 * by {@link AvelineState} via `avelineStates.ts`; the non-idle states are stubs for future
 * agentic-workflow events.
 */
export function AvelineAvatar({
  state = 'idle',
  className,
  blossomClassName,
}: AvelineAvatarProps) {
  const config = avelineStateConfig(state)

  return (
    <motion.div
      className={cn('flex items-center justify-center', className)}
      animate={{ rotate: 360 }}
      transition={{
        duration: config.spinSeconds,
        repeat: Infinity,
        ease: 'linear',
      }}
    >
      <Blossom
        className={cn(
          config.colourCycle && 'aveline-waiting',
          blossomClassName,
        )}
        animateCounter={config.sway}
        counterDuration={2.4}
      />
    </motion.div>
  )
}
