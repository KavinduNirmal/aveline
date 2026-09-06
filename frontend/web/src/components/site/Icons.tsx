import { ArrowRight } from 'lucide-react'
import { siApple, siGoogleplay } from 'simple-icons'
import { cn } from '@/lib/utils'

/**
 * CTA arrow from lucide-react with smooth, purely horizontal hover animation.
 */
export function CtaArrow({ className }: { className?: string }) {
  return (
    <ArrowRight
      className={cn(
        'size-4 shrink-0 transition-transform duration-300 ease-out group-hover:translate-x-1.5',
        className,
      )}
      aria-hidden="true"
    />
  )
}

/**
 * Backwards compatibility alias for CTA arrow.
 */
export const CurvedArrow = CtaArrow

/**
 * Official Google Play Store brand icon mark from simple-icons.
 */
export function PlayStoreIcon({ className }: { className?: string }) {
  return (
    <svg
      role="img"
      viewBox="0 0 24 24"
      fill="currentColor"
      className={cn('size-4 shrink-0', className)}
      aria-hidden="true"
    >
      <title>{siGoogleplay.title}</title>
      <path d={siGoogleplay.path} />
    </svg>
  )
}

/**
 * Official Apple brand icon mark from simple-icons.
 */
export function AppleIcon({ className }: { className?: string }) {
  return (
    <svg
      role="img"
      viewBox="0 0 24 24"
      fill="currentColor"
      className={cn('size-4 shrink-0', className)}
      aria-hidden="true"
    >
      <title>{siApple.title}</title>
      <path d={siApple.path} />
    </svg>
  )
}
