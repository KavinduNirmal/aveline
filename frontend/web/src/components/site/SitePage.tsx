import type { ReactNode } from 'react'

import { SiteFooter } from './SiteFooter'
import { SiteNav } from './SiteNav'

/** Light wrapper shared by the public marketing pages. */
export function SitePage({ children }: { children: ReactNode }) {
  return (
    <div className="min-h-screen bg-[#fdfaf8] font-sans text-neutral-900 antialiased">
      <SiteNav />
      {children}
      <SiteFooter />
    </div>
  )
}
