import { useEffect, useState } from 'react'
import { Link, NavLink, useLocation } from 'react-router-dom'

import { useAuth } from '@clerk/react'
import { Menu, X } from 'lucide-react'

import { Blossom } from '@/components/auth/Blossom'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

const TABS = [
  { label: 'Features', to: '/#features' },
  { label: 'Contact', to: '/contact' },
  { label: 'Plans', to: '/plans' },
  { label: 'Docs', to: '/docs' },
]

/** Sticky glass navigation used across the public marketing pages. */
export function SiteNav() {
  const { isLoaded, isSignedIn } = useAuth()
  const location = useLocation()
  const [open, setOpen] = useState(false)

  useEffect(() => {
    setOpen(false)
  }, [location])

  const handleAnchor = (e: React.MouseEvent, to: string) => {
    if (to.startsWith('/#') && location.pathname === '/') {
      e.preventDefault()
      document
        .getElementById(to.slice(2))
        ?.scrollIntoView({ behavior: 'smooth', block: 'start' })
    }
  }

  return (
    <header className="sticky top-0 z-50 border-b border-dashed border-neutral-200/80 bg-[#fdfaf8]/80 backdrop-blur-md">
      <div className="mx-auto flex h-16 w-full max-w-6xl items-center justify-between gap-4 px-5 lg:px-8">
        <Link to="/" className="flex items-center gap-2">
          <span className="flex size-8 items-center justify-center rounded-full bg-commerce/10 text-commerce">
            <Blossom className="size-5" />
          </span>
          <span className="font-serif text-lg font-medium text-neutral-900">Aveline</span>
        </Link>

        <nav className="hidden items-center gap-1 md:flex">
          {TABS.map((tab) => (
            <NavLink
              key={tab.label}
              to={tab.to}
              onClick={(e) => handleAnchor(e, tab.to)}
              className={({ isActive }) =>
                cn(
                  'rounded-full px-3.5 py-1.5 text-sm text-neutral-500 transition-colors hover:text-neutral-900',
                  isActive && tab.to !== '/#features' && 'text-neutral-900',
                )
              }
            >
              {tab.label}
            </NavLink>
          ))}
        </nav>

        <div className="hidden items-center gap-2 md:flex">
          {isLoaded && isSignedIn ? (
            <Button asChild variant="ghost" size="sm" className="text-neutral-700">
              <Link to="/app">Open dashboard</Link>
            </Button>
          ) : (
            <Button asChild variant="ghost" size="sm" className="text-neutral-700">
              <Link to="/sign-in">Sign in</Link>
            </Button>
          )}
          <Button asChild variant="outline" size="sm">
            <Link to="/download">Download app</Link>
          </Button>
          <Button asChild size="sm">
            <Link to="/sign-up">Create account</Link>
          </Button>
        </div>

        <button
          type="button"
          aria-label="Toggle navigation"
          onClick={() => setOpen((v) => !v)}
          className="flex size-9 items-center justify-center rounded-full border border-neutral-200 text-neutral-700 md:hidden"
        >
          {open ? <X className="size-4" /> : <Menu className="size-4" />}
        </button>
      </div>

      {open && (
        <div className="border-t border-dashed border-neutral-200 bg-[#fdfaf8]/95 px-5 py-4 md:hidden">
          <div className="flex flex-col gap-1">
            {TABS.map((tab) => (
              <NavLink
                key={tab.label}
                to={tab.to}
                onClick={(e) => handleAnchor(e, tab.to)}
                className="rounded-lg px-3 py-2 text-sm text-neutral-600 hover:bg-neutral-100"
              >
                {tab.label}
              </NavLink>
            ))}
          </div>
          <div className="mt-3 flex flex-col gap-2 border-t border-dashed border-neutral-200 pt-3">
            {isLoaded && isSignedIn ? (
              <Button asChild variant="outline">
                <Link to="/app">Open dashboard</Link>
              </Button>
            ) : (
              <Button asChild variant="outline">
                <Link to="/sign-in">Sign in</Link>
              </Button>
            )}
            <Button asChild>
              <Link to="/sign-up">Create account</Link>
            </Button>
          </div>
        </div>
      )}
    </header>
  )
}
