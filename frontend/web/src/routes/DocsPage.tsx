import { useMemo } from 'react'
import { useParams, Navigate } from 'react-router-dom'
import { DocsLayout } from '@/components/docs/DocsLayout'
import { DocsContent } from '@/components/docs/DocsContent'
import { type TocItem } from '@/components/docs/DocsToc'
import { DEFAULT_SLUG, getDocPageBySlug } from '@/docs/config'

// Import all markdown documents statically via Vite's ?raw feature
import gettingStartedMd from '@/docs/getting-started.md?raw'
import joiningABoutiqueMd from '@/docs/joining-a-boutique.md?raw'
import rolesPermissionsMd from '@/docs/roles-permissions.md?raw'
import overviewMd from '@/docs/overview.md?raw'
import customersMd from '@/docs/customers.md?raw'
import catalogMd from '@/docs/catalog.md?raw'
import salonMd from '@/docs/salon.md?raw'
import incomeMd from '@/docs/income.md?raw'
import approvalsMd from '@/docs/approvals.md?raw'
import teamMd from '@/docs/team.md?raw'
import usageMd from '@/docs/usage.md?raw'
import billingMd from '@/docs/billing.md?raw'
import integrationsMd from '@/docs/integrations.md?raw'
import settingsMd from '@/docs/settings.md?raw'
import privacySecurityMd from '@/docs/privacy-security.md?raw'

const DOC_CONTENTS: Record<string, string> = {
  'getting-started': gettingStartedMd,
  'joining-a-boutique': joiningABoutiqueMd,
  'roles-permissions': rolesPermissionsMd,
  overview: overviewMd,
  customers: customersMd,
  catalog: catalogMd,
  salon: salonMd,
  income: incomeMd,
  approvals: approvalsMd,
  team: teamMd,
  usage: usageMd,
  billing: billingMd,
  integrations: integrationsMd,
  settings: settingsMd,
  'privacy-security': privacySecurityMd,
}

function parseHeadings(markdown: string): TocItem[] {
  const lines = markdown.split('\n')
  const toc: TocItem[] = []

  for (const line of lines) {
    const trimmed = line.trim()
    const match = trimmed.match(/^(#{2,3})\s+(.+)$/)
    if (match) {
      const level = match[1].length
      const rawText = match[2]
      // Strip markdown links, bold, code ticks
      const cleanText = rawText
        .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
        .replace(/[*_`]/g, '')
        .trim()

      const id = cleanText
        .toLowerCase()
        .replace(/[^\w\s-]/g, '')
        .replace(/\s+/g, '-')

      toc.push({ id, text: cleanText, level })
    }
  }

  return toc
}

export function DocsPage() {
  const { slug } = useParams<{ slug?: string }>()
  const activeSlug = slug || DEFAULT_SLUG

  // Validate slug exists in catalog
  const pageMeta = getDocPageBySlug(activeSlug)
  const markdown = DOC_CONTENTS[activeSlug]

  const toc = useMemo(() => {
    if (!markdown) return []
    return parseHeadings(markdown)
  }, [markdown])

  if (!pageMeta || !markdown) {
    return <Navigate to={`/docs/${DEFAULT_SLUG}`} replace />
  }

  return (
    <DocsLayout currentSlug={activeSlug} toc={toc}>
      <DocsContent markdown={markdown} currentSlug={activeSlug} />
    </DocsLayout>
  )
}
