import { Reveal } from '@/components/site/Reveal'
import { SitePage } from '@/components/site/SitePage'

const DOCS = [
  {
    title: 'Getting started',
    body: 'Owners create a boutique and set their business rules. Staff create an '
      .concat('account and join with an invitation code from their owner.'),
  },
  {
    title: 'Roles & permissions',
    body: 'Aveline team roles (owner, manager, staff) pair with per-boutique roles. '
      .concat('Boutique owners manage memberships; high-value actions pause for the '
        + 'owner’s approval before they complete.'),
  },
  {
    title: 'Ava, Elle & Lina',
    body: 'Ava (memory) keeps every customer close. Elle (visual) understands products '
      .concat('and composes outfits. Lina (commerce) handles pricing, payments and '
        + 'delivery — always pausing for approval on big decisions.'),
  },
  {
    title: 'Admin access',
    body: 'Administrator access is requested through the unassuming admin sign-up and '
      .concat('granted only after review by the Aveline team.'),
  },
  {
    title: 'Privacy & security',
    body: 'Sessions are managed by Clerk. Customer data stays within your boutique. '
      .concat('See the Terms & Conditions for the full privacy policy.'),
  },
]

export function DocsPage() {
  return (
    <SitePage>
      <section className="mx-auto w-full max-w-4xl px-5 py-20 lg:px-8">
        <Reveal className="max-w-2xl">
          <p className="text-xs font-semibold uppercase tracking-[0.2em] text-commerce">
            Docs
          </p>
          <h1 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
            How Aveline works.
          </h1>
          <p className="mt-4 text-lg text-neutral-500">
            A quick guide to getting the most from your assistant.
          </p>
        </Reveal>

        <div className="mt-12 flex flex-col gap-4">
          {DOCS.map((doc, i) => (
            <Reveal key={doc.title} delay={i * 0.05}>
              <section className="rounded-3xl border-2 border-dashed border-neutral-200 bg-white p-7">
                <h2 className="font-serif text-2xl font-medium text-neutral-900">
                  {doc.title}
                </h2>
                <p className="mt-2 text-[15px] leading-relaxed text-neutral-600">{doc.body}</p>
              </section>
            </Reveal>
          ))}
        </div>

        <Reveal delay={0.1}>
          <p className="mt-10 text-sm text-neutral-400">
            Something not covered?{' '}
            <a href="mailto:support@aveline.lk" className="text-commerce hover:underline">
              support@aveline.lk
            </a>
          </p>
        </Reveal>
      </section>
    </SitePage>
  )
}
