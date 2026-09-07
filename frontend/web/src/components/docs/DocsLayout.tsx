import { useState, type ReactNode } from 'react'
import { Menu, X, BookOpen } from 'lucide-react'
import { SiteNav } from '@/components/site/SiteNav'
import { SiteFooter } from '@/components/site/SiteFooter'
import { DocsSidebar } from '@/components/docs/DocsSidebar'
import { DocsToc, type TocItem } from '@/components/docs/DocsToc'
import { Button } from '@/components/ui/button'

interface DocsLayoutProps {
  currentSlug: string
  toc: TocItem[]
  children: ReactNode
}

export function DocsLayout({ currentSlug, toc, children }: DocsLayoutProps) {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false)

  return (
    <div className="min-h-screen bg-background text-foreground antialiased flex flex-col">
      <SiteNav />

      {/* Mobile docs navigation trigger bar */}
      <div className="sticky top-16 z-30 flex items-center justify-between border-b border-dashed border-border bg-background/90 px-5 py-2.5 backdrop-blur-md lg:hidden">
        <div className="flex items-center gap-2">
          <BookOpen className="size-4 text-commerce" />
          <span className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            Documentation Menu
          </span>
        </div>
        <Button
          variant="outline"
          size="sm"
          onClick={() => setMobileMenuOpen(!mobileMenuOpen)}
          className="gap-1 text-xs"
        >
          {mobileMenuOpen ? <X className="size-3.5" /> : <Menu className="size-3.5" />}
          <span>{mobileMenuOpen ? 'Close' : 'Index'}</span>
        </Button>
      </div>

      {/* Mobile slide-down drawer */}
      {mobileMenuOpen && (
        <div className="border-b border-border bg-background/98 px-6 py-4 shadow-lg lg:hidden">
          <DocsSidebar
            currentSlug={currentSlug}
            onItemClick={() => setMobileMenuOpen(false)}
          />
        </div>
      )}

      {/* Main 3-column container */}
      <div className="mx-auto flex w-full max-w-7xl flex-1 px-4 sm:px-6 lg:px-8">
        {/* Left column: Sticky Sidebar */}
        <div className="hidden w-64 shrink-0 lg:block border-r border-dashed border-border/80 pr-6 py-8">
          <div className="sticky top-24 max-h-[calc(100vh-7rem)] overflow-y-auto pr-2">
            <DocsSidebar currentSlug={currentSlug} />
          </div>
        </div>

        {/* Center column: Markdown Content */}
        <main className="min-w-0 flex-1 px-2 py-8 sm:px-6 lg:px-12 lg:py-10">
          <div className="mx-auto max-w-3xl">
            {children}
          </div>
        </main>

        {/* Right column: Sticky Table of Contents */}
        <div className="hidden w-56 shrink-0 xl:block pl-6 py-8 border-l border-dashed border-border/60">
          <div className="sticky top-24 max-h-[calc(100vh-7rem)] overflow-y-auto">
            <DocsToc toc={toc} />
          </div>
        </div>
      </div>

      <SiteFooter />
    </div>
  )
}
