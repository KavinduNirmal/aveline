import type { CSSProperties } from 'react'

import { cn } from '@/lib/utils'
import { AvelineBlossom } from './AvelineBlossom'
import { avelineStateConfig, type AvelineState } from './avelineStates'

interface AvelineAvatarProps {
  /** The agentic-workflow state driving the animation. Defaults to `idle`. */
  state?: AvelineState
  className?: string
  /** Blossom size in Tailwind size units (e.g. `size-5`). */
  blossomClassName?: string
}

/**
 * Maps an animation mode to the CSS class applied to the rotation wrapper. Rotation is
 * nested so it never conflicts with the transform wrapper's scale/sway/shake.
 */
function spinClass(mode: string, spinSeconds: number): { className?: string; style?: CSSProperties } {
  if (mode === 'rotationPulse') {
    // Mechanical: stepped rotation.
    return { className: 'aveline-spin-steps' }
  }
  if (mode === 'rotation' || (Number.isFinite(spinSeconds) && spinSeconds > 0)) {
    return { className: 'aveline-spin', style: { ['--aveline-spin' as string]: `${spinSeconds}s` } }
  }
  return {}
}

/** Maps an animation mode to the CSS class applied to the transform wrapper. */
function transformClass(mode: string): string | undefined {
  switch (mode) {
    case 'breathing':
      return 'aveline-breathing'
    case 'pulse':
    case 'rotationPulse':
      return 'aveline-pulse'
    case 'bloom':
      return 'aveline-bloom'
    case 'shake':
      return 'aveline-shake'
    default:
      return undefined
  }
}

/**
 * Aveline's avatar: the Blossom mark, animated to reflect her current agentic-workflow
 * state. The animation behaviour is driven by {@link AvelineState} via `avelineStates.ts`.
 * No background circle - the blossom is the icon itself.
 */
export function AvelineAvatar({
  state = 'idle',
  className,
  blossomClassName,
}: AvelineAvatarProps) {
  const config = avelineStateConfig(state)
  const spin = spinClass(config.mode, config.spinSeconds)
  const transform = transformClass(config.mode)

  return (
    <div className={cn('flex items-center justify-center', className)}>
      <div className={spin.className} style={spin.style}>
        <div className={cn('flex items-center justify-center', transform)}>
          <AvelineBlossom
            className={cn(
              config.colourCycle && 'aveline-waiting',
              blossomClassName,
            )}
            animateCounter={config.sway}
            counterDuration={6}
            ripple={config.mode === 'ripple'}
          />
        </div>
      </div>
    </div>
  )
}
