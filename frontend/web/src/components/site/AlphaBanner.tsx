import { useState } from 'react'
import { Link } from 'react-router-dom'
import { AlertTriangle, X } from 'lucide-react'

import { cn } from '@/lib/utils'

/**
 * Full-width alpha-development notice shown just below the site navigation on
 * every public marketing page. Dismissible for the current session.
 */
export function AlphaBanner() {
  const [dismissed, setDismissed] = useState(
    () => sessionStorage.getItem('aveline:alpha-banner-dismissed') === '1',
  )

  if (dismissed) return null

  const dismiss = () => {
    sessionStorage.setItem('aveline:alpha-banner-dismissed', '1')
    setDismissed(true)
  }

  return (
    <div
      role="note"
      className="relative border-b border-dashed border-amber-300/70 bg-gradient-to-r from-amber-50 via-[#fff7e6] to-amber-50"
    >
      <div className="mx-auto flex w-full max-w-6xl items-center gap-3 px-5 py-2.5 lg:px-8">
        <AlertTriangle className="size-4 shrink-0 text-amber-600" aria-hidden />
        <p className="text-[13px] leading-snug text-amber-900">
          <span className="font-semibold">Aveline AI is currently in alpha development</span> and
          this is only a preview build. There might be unexpected changes and mistakes. We hope for
          your understanding. If you notice any bugs, please{' '}
          <Link to="/contact" className="font-medium underline underline-offset-2 hover:text-amber-700">
            report them
          </Link>
          .
        </p>
        <button
          type="button"
          onClick={dismiss}
          aria-label="Dismiss alpha notice"
          className={cn(
            'ml-auto flex size-6 shrink-0 items-center justify-center rounded-full',
            'text-amber-700/70 transition-colors hover:bg-amber-100 hover:text-amber-900',
          )}
        >
          <X className="size-3.5" aria-hidden />
        </button>
      </div>
    </div>
  )
}
