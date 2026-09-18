import type { CSSProperties } from 'react'

/**
 * Aveline's 8-petal blossom mark for the in-app experience (dashboard, chat, etc.).
 *
 * This is a refined variant of the brand {@link Blossom} used on the auth and landing
 * pages. It keeps the same two-layer 8-petal geometry but tunes the rendering and motion
 * for the app chrome:
 *  - the top petal layer is rendered lighter so the two 4-petal layers read clearly;
 *  - the counter-sway is slower and eases in/out for a calmer, more organic feel.
 *
 * It is intentionally self-contained (its keyframes are injected via a scoped `<style>`)
 * so it never interferes with the brand blossom's animation on the marketing pages.
 * Continuous rotation is applied by the caller (e.g. {@link AvelineAvatar}) on a wrapper
 * so it can be eased and slowed independently of the petal sway.
 */
export function AvelineBlossom({
  className,
  style,
  animateCounter = false,
  counterDuration = 6,
  ripple = false,
}: {
  className?: string
  style?: CSSProperties
  /** If true, gently sways the base and top layers in opposite directions. */
  animateCounter?: boolean
  /** Duration in seconds for one full sway cycle. */
  counterDuration?: number
  /** If true, renders expanding ripple rings around the petals (searching state). */
  ripple?: boolean
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
      {animateCounter && (
        <defs>
          <style>{`
            @keyframes aveline-app-sway-base {
              0%   { transform: rotate(0deg); }
              25%  { transform: rotate(10deg); }
              50%  { transform: rotate(0deg); }
              75%  { transform: rotate(-10deg); }
              100% { transform: rotate(0deg); }
            }
            @keyframes aveline-app-sway-top {
              0%   { transform: rotate(0deg); }
              25%  { transform: rotate(-8deg); }
              50%  { transform: rotate(0deg); }
              75%  { transform: rotate(8deg); }
              100% { transform: rotate(0deg); }
            }
            .aveline-app-layer-base {
              transform-origin: 12px 12px;
              animation: aveline-app-sway-base ${counterDuration}s ease-in-out infinite;
              animation-delay: -${(counterDuration * 0.25).toFixed(1)}s;
            }
            .aveline-app-layer-top {
              transform-origin: 12px 12px;
              animation: aveline-app-sway-top ${counterDuration}s ease-in-out infinite;
            }
          `}</style>
        </defs>
      )}

      {/* Ripple rings (searching): concentric expanding circles behind the petals. */}
      {ripple && (
        <g fill="none" stroke="currentColor" strokeWidth="0.6">
          {[0, 1, 2].map((i) => (
            <circle
              key={i}
              className="aveline-ripple-ring"
              cx="12"
              cy="12"
              r="7"
            />
          ))}
        </g>
      )}

      {/* Base 4 petals */}
      <g
        className={animateCounter ? 'aveline-app-layer-base' : undefined}
        opacity="0.95"
      >
        {baseAngles.map((angle) => (
          <ellipse
            key={angle}
            cx="12"
            cy="5.4"
            rx="3.1"
            ry="5.3"
            fill="currentColor"
            transform={`rotate(${angle} 12 12)`}
          />
        ))}
      </g>

      {/* Top 4 petals (offset by 45 degrees), rendered lighter so the layers read clearly */}
      <g
        className={animateCounter ? 'aveline-app-layer-top' : undefined}
        opacity="0.72"
      >
        {topAngles.map((angle) => (
          <ellipse
            key={angle}
            cx="12"
            cy="5.4"
            rx="2.95"
            ry="5.1"
            fill="currentColor"
            transform={`rotate(${angle} 12 12)`}
          />
        ))}
      </g>

      {/* Center pistil */}
      <circle cx="12" cy="12" r="2.3" fill="currentColor" opacity="0.95" />
    </svg>
  )
}
