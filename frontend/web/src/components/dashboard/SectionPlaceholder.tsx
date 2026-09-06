import type { LucideIcon } from 'lucide-react'
import { Hammer } from 'lucide-react'

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'

interface SectionPlaceholderProps {
  title: string
  description?: string
  icon?: LucideIcon
}

/**
 * Renders a "coming soon" panel for dashboard sections that depend on slices that are
 * not built yet (Customers, Catalog, Approvals, etc.).
 */
export function SectionPlaceholder({
  title,
  description,
  icon: Icon = Hammer,
}: SectionPlaceholderProps) {
  return (
    <div className="space-y-6">
      <div>
        <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
          {title}
        </p>
        <h1 className="mt-2 font-serif text-3xl font-medium tracking-tight">{title}</h1>
      </div>

      <Card className="shadow-[0_4px_20px_rgba(122,48,63,0.06)]">
        <CardHeader>
          <div className="flex items-center justify-between gap-4">
            <CardTitle className="font-serif text-2xl font-medium">{title}</CardTitle>
            <span className="text-muted-foreground" aria-hidden>
              <Icon className="size-6" />
            </span>
          </div>
          <CardDescription>
            {description ?? 'This section will light up once the corresponding Aveline slice is built.'}
          </CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">
            Nothing here yet — watch this space.
          </p>
        </CardContent>
      </Card>
    </div>
  )
}
