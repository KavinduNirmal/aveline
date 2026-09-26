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
    heading: 'Get started',
    pages: [
      {
        slug: 'getting-started',
        title: 'Getting Started',
        description: 'Create your boutique account and complete the setup guide.',
      },
      {
        slug: 'joining-a-boutique',
        title: 'Joining a Boutique',
        description: 'Join a boutique with an invitation code from your owner.',
      },
      {
        slug: 'roles-permissions',
        title: 'Roles & Permissions',
        description: 'Owners, managers, supervisors, staff, and what each one may open.',
      },
    ],
  },
  {
    heading: 'Everyday work',
    pages: [
      {
        slug: 'overview',
        title: 'Your Dashboard',
        description: 'Find your way around the sidebar, the header and the Overview figures.',
      },
      {
        slug: 'customers',
        title: 'Customers',
        description: 'The client book: find a client, log a visit, add or remove a record.',
      },
      {
        slug: 'catalog',
        title: 'Catalog',
        description: 'Pieces, lookbooks, sourcing, ateliers and printable floor tags.',
      },
      {
        slug: 'salon',
        title: 'Salon',
        description: 'Client conversations, the Aveline concierge, and decisions it asks for.',
      },
      {
        slug: 'income',
        title: 'Income',
        description: 'Your takings, the register, and the two bases it never adds together.',
      },
      {
        slug: 'approvals',
        title: 'Approvals',
        description: 'Discounts and orders waiting on a decision, and what each verb does.',
      },
    ],
  },
  {
    heading: 'Running your boutique',
    pages: [
      {
        slug: 'team',
        title: 'Team',
        description: 'Members, roles, and generating onboarding codes for new staff.',
      },
      {
        slug: 'usage',
        title: 'Blossoms and Usage',
        description: 'What a Blossom is, your balance, and how the allowance is being spent.',
      },
      {
        slug: 'billing',
        title: 'Plan & Billing',
        description: 'Your plan, its entitlements, the period history and the Blossom statement.',
      },
      {
        slug: 'integrations',
        title: 'Integrations',
        description: 'Connect WhatsApp Business, Instagram and your payment gateway.',
      },
      {
        slug: 'settings',
        title: 'Settings',
        description: 'Your boutique profile, plan entitlements and API keys.',
      },
    ],
  },
  {
    heading: 'Trust and data',
    pages: [
      {
        slug: 'privacy-security',
        title: 'Privacy & Security',
        description: 'Tenant isolation, identity, encrypted credentials and what Aveline never does.',
      },
    ],
  },
]

export const DEFAULT_SLUG = 'getting-started'

export const ALL_DOC_PAGES: DocPage[] = DOCS_SECTIONS.flatMap((s) => s.pages)

export function getDocPageBySlug(slug: string): DocPage | undefined {
  return ALL_DOC_PAGES.find((p) => p.slug === slug)
}
