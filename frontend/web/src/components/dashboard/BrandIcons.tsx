import { siInstagram, siStripe, siWhatsapp } from 'simple-icons'
import { cn } from '@/lib/utils'

/**
 * Official brand icon marks from simple-icons, rendered as inline SVGs matching the
 * existing site icon pattern (see `components/site/Icons.tsx`).
 */

function BrandIcon({
  icon,
  className,
  title,
}: {
  icon: { title: string; path: string }
  className?: string
  title: string
}) {
  return (
    <svg
      role="img"
      viewBox="0 0 24 24"
      fill="currentColor"
      className={cn('size-4 shrink-0', className)}
      aria-hidden="true"
    >
      <title>{title}</title>
      <path d={icon.path} />
    </svg>
  )
}

/** Official WhatsApp brand icon mark. */
export function WhatsAppIcon({ className }: { className?: string }) {
  return <BrandIcon icon={siWhatsapp} title={siWhatsapp.title} className={className} />
}

/** Official Instagram brand icon mark. */
export function InstagramIcon({ className }: { className?: string }) {
  return <BrandIcon icon={siInstagram} title={siInstagram.title} className={className} />
}

/** Payment gateway brand icon mark (Stripe). */
export function PaymentIcon({ className }: { className?: string }) {
  return <BrandIcon icon={siStripe} title={siStripe.title} className={className} />
}
