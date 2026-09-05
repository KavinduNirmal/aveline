import type { CSSProperties } from 'react'

/** Minimal five-petal blossom used across the auth art. */
export function Blossom({
  className,
  style,
  petalClassName,
}: {
  className?: string
  style?: CSSProperties
  petalClassName?: string
}) {
  return (
    <svg
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden
      className={className}
      style={style}
    >
      {[0, 72, 144, 216, 288].map((angle) => (
        <ellipse
          key={angle}
          cx="12"
          cy="5.6"
          rx="3.1"
          ry="5.2"
          fill="currentColor"
          transform={`rotate(${angle} 12 12)`}
          className={petalClassName}
        />
      ))}
      <circle cx="12" cy="12" r="2.4" fill="currentColor" opacity="0.9" />
    </svg>
  )
}
