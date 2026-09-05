import { Link } from 'react-router-dom'

import { Blossom } from '@/components/auth/Blossom'

const COLUMNS: { title: string; links: { label: string; to: string }[] }[] = [
  {
    title: 'Product',
    links: [
      { label: 'Features', to: '/#features' },
      { label: 'Plans', to: '/plans' },
      { label: 'Docs', to: '/docs' },
      { label: 'Download app', to: '/download' },
    ],
  },
  {
    title: 'Company',
    links: [
      { label: 'Contact', to: '/contact' },
      { label: 'Terms', to: '/terms' },
      { label: 'Privacy', to: '/terms#privacy' },
      { label: 'Admin sign-up', to: '/sign-up/admin' },
    ],
  },
]

/** Shared footer for the public marketing pages. */
export function SiteFooter() {
  return (
    <footer className="border-t-2 border-dashed border-neutral-200 bg-white">
      <div className="mx-auto w-full max-w-6xl px-5 py-14 lg:px-8">
        <div className="grid gap-10 md:grid-cols-[1.4fr_1fr_1fr]">
          <div>
            <Link to="/" className="flex items-center gap-2">
              <span className="flex size-8 items-center justify-center rounded-full bg-commerce/10 text-commerce">
                <Blossom className="size-5" />
              </span>
              <span className="font-serif text-lg font-medium text-neutral-900">Aveline</span>
            </Link>
            <p className="mt-3 max-w-xs text-sm leading-relaxed text-neutral-500">
              The assistant that remembers. Ava, Elle and Lina tend your boutique —
              so you stay close and personal with every customer.
            </p>
          </div>

          {COLUMNS.map((col) => (
            <div key={col.title}>
              <p className="text-xs font-semibold uppercase tracking-[0.18em] text-neutral-400">
                {col.title}
              </p>
              <ul className="mt-3 space-y-2">
                {col.links.map((link) => (
                  <li key={link.label}>
                    <Link
                      to={link.to}
                      className="text-sm text-neutral-600 transition-colors hover:text-commerce"
                    >
                      {link.label}
                    </Link>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </div>

        <div className="mt-12 border-t-2 border-dashed border-neutral-200 pt-6 text-xs text-neutral-400">
          © 2026 Aveline · contact@aveline.lk · Colombo, Sri Lanka
        </div>
      </div>
    </footer>
  )
}
