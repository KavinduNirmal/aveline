export interface DocPage {
  slug: string
  title: string
  description?: string
}

export interface DocSection {
  heading: string
  pages: DocPage[]
}

export const DOCS_SECTIONS: DocSection[] = [
  {
    heading: 'Overview',
    pages: [
      {
        slug: 'getting-started',
        title: 'Getting Started',
        description: 'First steps with Aveline, inviting staff, and boutique onboarding.',
      },
      {
        slug: 'roles-permissions',
        title: 'Roles & Permissions',
        description: 'Boutique owners, managers, associates, and role governance.',
      },
    ],
  },
  {
    heading: 'AI Concierge & Agents',
    pages: [
      {
        slug: 'ava',
        title: 'Ava — Memory',
        description: 'Client profile retention, preferences, notes, and visit history.',
      },
      {
        slug: 'elle',
        title: 'Elle — Visual Sourcing',
        description: 'Garment and jewelry intelligence, lookbooks, and visual composition.',
      },
      {
        slug: 'lina',
        title: 'Lina — Commerce & Orders',
        description: 'Pricing validation, payment intent, boutique approvals, and checkout.',
      },
    ],
  },
  {
    heading: 'Platform & Security',
    pages: [
      {
        slug: 'admin-access',
        title: 'Admin Access',
        description: 'Tenant administration, elevated access requests, and audits.',
      },
      {
        slug: 'privacy-security',
        title: 'Privacy & Security',
        description: 'Data isolation, Clerk identity, RBAC guarantees, and compliance.',
      },
    ],
  },
]

export const DEFAULT_SLUG = 'getting-started'

export const ALL_DOC_PAGES: DocPage[] = DOCS_SECTIONS.flatMap((s) => s.pages)

export function getDocPageBySlug(slug: string): DocPage | undefined {
  return ALL_DOC_PAGES.find((p) => p.slug === slug)
}
