import { Link } from 'react-router-dom'

const SECTIONS: { id?: string; title: string; body: string }[] = [
  {
    title: '1. The service',
    body: 'Aveline provides boutique concierge software to owners, managers and '
        .concat('their staff. Your account is personal: do not share credentials, and keep '
          + 'client information confidential and compliant with applicable law.'),
  },
  {
    title: '2. Accounts & roles',
    body: 'You may create an owner or staff account, or be provisioned one by an '
        .concat('organization or by Aveline. Access is tied to the account type and the '
          + 'organization you belong to; roles may be changed by boutique owners.'),
  },
  {
    title: '3. Acceptable use',
    body: 'Use Aveline only for lawful boutique operations. You may not attempt to '
        .concat('circumvent authentication, access another organization’s data, or interfere '
          + 'with the service.'),
  },
  {
    id: 'privacy',
    title: '4. Privacy',
    body: 'Clerk manages your sign-in identity and session. Aveline stores the profile '
        .concat('and organization data needed to run the service and does not sell personal '
          + 'data. Email us at privacy@aveline.lk for access or deletion requests.'),
  },
  {
    title: '5. Termination',
    body: 'Accounts that are suspended or removed lose access immediately. You may '
        .concat('stop using the service at any time; ask an owner or contact support to remove '
          + 'your data.'),
  },
]

/** Public terms & privacy reference page (linked from sign-up). */
export function TermsPage() {
  return (
    <div className="min-h-screen bg-[#faf7f6] px-6 py-12">
      <div className="mx-auto w-full max-w-2xl">
        <Link to="/sign-up" className="text-sm text-[#7a303f] hover:underline">
          ← Back to sign up
        </Link>
        <h1 className="mt-4 font-serif text-3xl font-medium text-neutral-900">
          Terms &amp; Conditions
        </h1>
        <p className="mt-2 text-sm text-neutral-500">
          Last updated September 2026 · Also covers the Privacy Policy.
        </p>

        <div className="mt-8 flex flex-col gap-6">
          {SECTIONS.map(({ id, title, body }) => (
            <section
              key={title}
              id={id}
              className="rounded-2xl border-2 border-dashed border-neutral-300 bg-white p-6"
            >
              <h2 className="text-base font-semibold text-neutral-900">{title}</h2>
              <p className="mt-2 text-sm leading-relaxed text-neutral-600">{body}</p>
            </section>
          ))}
        </div>
      </div>
    </div>
  )
}
