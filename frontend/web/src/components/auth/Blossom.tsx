import type { CSSProperties } from 'react'

/**
 * Legacy 5-petal blossom (kept for reference / easy restore if needed):
 * 
 * export function BlossomFivePetal({
 *   className,
 *   style,
 *   petalClassName,
 * }: {
 *   className?: string
 *   style?: CSSProperties
 *   petalClassName?: string
 * }) {
 *   return (
 *     <svg
 *       viewBox="0 0 24 24"
 *       fill="none"
 *       aria-hidden
 *       className={className}
 *       style={style}
 *     >
 *       {[0, 72, 144, 216, 288].map((angle) => (
 *         <ellipse
 *           key={angle}
 *           cx="12"
 *           cy="5.6"
 *           rx="3.1"
 *           ry="5.2"
 *           fill="currentColor"
 *           transform={`rotate(${angle} 12 12)`}
 *           className={petalClassName}
 *         />
 *       ))}
 *       <circle cx="12" cy="12" r="2.4" fill="currentColor" opacity="0.9" />
 *     </svg>
 *   )
 * }
 */

/**
 * 8-petal blossom composed of two 4-petal layers (base at 0, 90, 180, 270 deg;
 * top layer at 45, 135, 225, 315 deg).
 *
 * Supports optional counter-rotation (`animateCounter`) which smoothly spins
 * the base layer clockwise and the top layer counter-clockwise.
 */
export function Blossom({
  className,
  style,
  petalClassName,
  animateCounter = false,
  counterDuration = 18,
}: {
  className?: string
  style?: CSSProperties
  petalClassName?: string
  /** If true, rotates base and top layers in opposite directions */
  animateCounter?: boolean
  /** Duration in seconds for a full rotation loop */
  counterDuration?: number
}) {
  const baseAngles = [0, 90, 180, 270]
  const topAngles = [45, 135, 225, 315]

  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden
      className={className}
      style={style}
    >
      <defs>
        {animateCounter && (
          <style>{`
            @keyframes aveline-spin-base {
              0% { transform: rotate(0deg); }
              42% { transform: rotate(14deg); }
              78% { transform: rotate(-10deg); }
              100% { transform: rotate(0deg); }
            }
            @keyframes aveline-spin-top {
              0% { transform: rotate(0deg); }
              48% { transform: rotate(-15deg); }
              82% { transform: rotate(8deg); }
              100% { transform: rotate(0deg); }
            }
            .aveline-petal-layer-base {
              transform-origin: 12px 12px;
              animation: aveline-spin-base ${counterDuration}s ease-in-out infinite;
              animation-delay: -${(counterDuration * 0.35).toFixed(1)}s;
            }
            .aveline-petal-layer-top {
              transform-origin: 12px 12px;
              animation: aveline-spin-top ${counterDuration * 1.25}s ease-in-out infinite;
              animation-delay: -${(counterDuration * 0.85).toFixed(1)}s;
            }
          `}</style>
        )}
      </defs>

      {/* Base 4 petals */}
      <g className={animateCounter ? 'aveline-petal-layer-base' : undefined} opacity="0.92">
        {baseAngles.map((angle) => (
          <ellipse
            key={angle}
            cx="12"
            cy="5.4"
            rx="3.1"
            ry="5.3"
            fill="currentColor"
            transform={`rotate(${angle} 12 12)`}
            className={petalClassName}
          />
        ))}
      </g>

      {/* Top 4 petals (offset by 45 degrees) */}
      <g className={animateCounter ? 'aveline-petal-layer-top' : undefined} opacity="0.98">
        {topAngles.map((angle) => (
          <ellipse
            key={angle}
            cx="12"
            cy="5.4"
            rx="2.95"
            ry="5.1"
            fill="currentColor"
            transform={`rotate(${angle} 12 12)`}
            className={petalClassName}
          />
        ))}
      </g>

      {/* Center pistil */}
      <circle cx="12" cy="12" r="2.3" fill="currentColor" opacity="0.95" />
    </svg>
  )
}
