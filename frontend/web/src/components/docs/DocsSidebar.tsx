import { Link } from 'react-router-dom'
import { DOCS_SECTIONS } from '@/docs/config'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

interface DocsSidebarProps {
  currentSlug: string
  onItemClick?: () => void
}

export function DocsSidebar({ currentSlug, onItemClick }: DocsSidebarProps) {
  return (
    <aside className="w-full">
      <div className="flex flex-col gap-6 py-2">
        {DOCS_SECTIONS.map((section) => (
          <div key={section.heading} className="flex flex-col gap-1.5">
            <h3 className="px-3 text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
              {section.heading}
            </h3>
            <div className="flex flex-col gap-0.5">
              {section.pages.map((page) => {
                const isActive = page.slug === currentSlug
                return (
                  <Link
                    key={page.slug}
                    to={`/docs/${page.slug}`}
                    onClick={onItemClick}
                    className={cn(
                      'group flex items-center justify-between rounded-lg px-3 py-2 text-sm font-medium transition-colors',
                      isActive
                        ? 'bg-commerce/10 text-commerce font-semibold'
                        : 'text-neutral-600 hover:bg-neutral-100/80 hover:text-neutral-950 dark:text-neutral-400 dark:hover:bg-neutral-800/60 dark:hover:text-neutral-100'
                    )}
                  >
                    <span>{page.title}</span>
                    {isActive && (
                      <span className="size-1.5 rounded-full bg-commerce" aria-hidden="true" />
                    )}
                  </Link>
                )
              })}
            </div>
          </div>
        ))}

        <div className="mt-4 rounded-xl border border-dashed border-border bg-card/60 p-4">
          <div className="flex items-center justify-between gap-2">
            <span className="text-xs font-medium text-foreground">Aveline Suite</span>
            <Badge variant="outline" className="text-[10px] px-1.5 py-0 text-commerce border-commerce/30">
              v1.0-preview
            </Badge>
          </div>
          <p className="mt-2 text-xs text-muted-foreground leading-relaxed">
            Architected for luxury fashion boutiques & atelier clienteling.
          </p>
        </div>
      </div>
    </aside>
  )
}
