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
 * Supports optional counter-sway (`animateCounter`) which gently rocks the base
 * layer clockwise and the top layer counter-clockwise. The sway is intentionally
 * kept within ±12 deg so the two layers never fully overlap and collapse to 4
 * visible petals.
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
  /** If true, gently sways base and top layers in opposite directions */
  animateCounter?: boolean
  /** Duration in seconds for one full sway cycle */
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
            @keyframes aveline-sway-base {
              0%   { transform: rotate(0deg); }
              25%  { transform: rotate(12deg); }
              50%  { transform: rotate(0deg); }
              75%  { transform: rotate(-12deg); }
              100% { transform: rotate(0deg); }
            }
            @keyframes aveline-sway-top {
              0%   { transform: rotate(0deg); }
              25%  { transform: rotate(-10deg); }
              50%  { transform: rotate(0deg); }
              75%  { transform: rotate(10deg); }
              100% { transform: rotate(0deg); }
            }
            .aveline-petal-layer-base {
              transform-origin: 12px 12px;
              animation: aveline-sway-base ${counterDuration}s ease-in-out infinite;
              animation-delay: -${(counterDuration * 0.25).toFixed(1)}s;
            }
            .aveline-petal-layer-top {
              transform-origin: 12px 12px;
              animation: aveline-sway-top ${counterDuration}s ease-in-out infinite;
              animation-delay: 0s;
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
