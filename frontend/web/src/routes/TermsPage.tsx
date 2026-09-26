import { Link } from 'react-router-dom'

type Part = 'terms' | 'privacy'

interface Section {
  /** Anchor id. Every section is linkable. */
  id: string
  part: Part
  title: string
  body: string
  /** Optional list rendered under the paragraph. */
  bullets?: string[]
}

/**
 * Terms & Conditions and the Privacy Notice, in one document.
 *
 * `#privacy` is the anchor the rest of the product links to (the site footer, the sign-up
 * page and the sign-in page), so the Privacy Notice's part heading carries that id and it must
 * keep it if this page is ever split into a separate `/privacy` route.
 *
 * The content is a legal document, not a description of the code. Where it states what the
 * product does, that statement was checked against the shipped behaviour; where it states a
 * policy decision (retention, liability, governing law), it is the company's position and needs
 * counsel's sign-off. Phase 7 of
 * `.agents/plans/privacy-consent-data-deletion-implementation.ignore.md` adds the self-service
 * opt-out, export and deletion surfaces this notice promises under "What we are adding".
 */
const SECTIONS: Section[] = [
  // ---------------------------------------------------------------- Part A
  {
    id: 'terms-who-we-are',
    part: 'terms',
    title: '1. Who we are and what these terms cover',
    body: 'Aveline ("we", "us") provides boutique concierge software: a workspace where a fashion boutique keeps its client book, its catalogue, its customer conversations and its takings. These terms govern your use of that software and of this website. Part B of this page is our Privacy Notice, and it forms part of these terms.',
    bullets: [
      'By creating an account, accepting an invitation, or using the service, you agree to these terms. If you do not agree, do not use the service.',
      'If you accept on behalf of a boutique, you confirm that you are authorised to bind that boutique to these terms.',
      'Accounts are for adults. You must be at least 18 years old to hold an owner or staff account.',
    ],
  },
  {
    id: 'terms-service',
    part: 'terms',
    title: '2. The service',
    body: 'The service keeps a memory of the clients a boutique serves, reads garments from photographs, drafts styling suggestions and customer replies, holds client conversations in one place, routes discounts and orders to a person for approval, and reports what the shop took. Aveline is an assistant: it drafts and suggests, and a person at the boutique decides.',
    bullets: [
      'Aveline does not give legal, tax, accounting or financial advice, and its output is not a substitute for your own judgement.',
      'The service is under active development. Features may change, and a feature described in our documentation may be adjusted or withdrawn as the product matures.',
      'We may set and change fair-use limits, including the Blossom allowance described in section 6, to keep the service available to everyone.',
    ],
  },
  {
    id: 'terms-accounts',
    part: 'terms',
    title: '3. Accounts, roles and access',
    body: 'A boutique owner creates the workspace and invites staff. Every person who joins holds one role, and the role decides what they can open and do. Access is tied to the account and to the boutique it belongs to.',
    bullets: [
      'Keep your credentials to yourself. You are responsible for what is done through your account.',
      'A boutique owner is responsible for the people they invite, the roles they assign, and removing access when someone leaves.',
      'Role changes take effect on the member’s next request, and we may suspend an account that we believe is being misused.',
      'Tell us promptly at contact@aveline.lk if you believe an account has been compromised.',
    ],
  },
  {
    id: 'terms-acceptable-use',
    part: 'terms',
    title: '4. Acceptable use',
    body: 'Use Aveline for lawful boutique operations, and only for the boutique you belong to. In particular, you must not:',
    bullets: [
      'circumvent or attempt to circumvent authentication or authorisation, or reach data belonging to another boutique;',
      'copy, scrape, resell or provide the service to anyone outside your boutique without our written agreement;',
      'send unlawful, misleading or unsolicited messages through a channel you have connected, or breach the terms of that channel;',
      'upload content you have no right to upload, or use the service to harass, discriminate against or deceive anyone;',
      'reverse engineer, probe or disrupt the service, or use it to build a competing product.',
    ],
  },
  {
    id: 'terms-client-data',
    part: 'terms',
    title: '5. Your clients, and who is responsible for their data',
    body: 'For the personal data of your clients, your boutique is the data controller and Aveline is the processor: we handle that data on your instructions and for the purpose of running the service for you. For your own account data as an owner or staff member, Aveline is the controller.',
    bullets: [
      'Your boutique is responsible for having a lawful basis to hold and use its clients’ data, and for giving them the information the law requires.',
      'Aveline will not use one boutique’s client data to serve another boutique, and we do not sell personal data.',
      'If a client asks your boutique to stop processing, to correct their record, or to delete it, tell us and we will help you act on it. Part B sets out how requests are handled.',
      'Where a message is sent to a client through a channel you have connected, the boutique is the sender and must honour any opt-out the client gives.',
    ],
  },
  {
    id: 'terms-plans',
    part: 'terms',
    title: '6. Plans, Blossoms and payment',
    body: 'The service is offered on plans, each with a monthly allowance of Blossoms, a number of staff seats and a limit on active clients. A Blossom is our unit of AI work: it is not money, it has no cash value, and it cannot be transferred or exchanged.',
    bullets: [
      'Plan list prices are quoted in Sri Lankan Rupees and are shown on the plans page.',
      'No payment provider is connected to the service today, so nothing is charged through it. Where a plan is not free, we will contact you to arrange payment before any charge applies.',
      'Plan changes are arranged with us rather than applied instantly, and we confirm the effective date with you before anything changes.',
      'We may change plans, allowances and prices. If a change materially reduces what your plan includes, we will give you reasonable notice.',
    ],
  },
  {
    id: 'terms-third-parties',
    part: 'terms',
    title: '7. Third-party services you connect',
    body: 'You can connect your own WhatsApp Business, Instagram and payment-gateway accounts. You supply your own credentials, and you remain the account holder with that provider.',
    bullets: [
      'Your provider’s own terms apply to that account, and you are responsible for complying with them.',
      'Fees charged by a provider (for example Meta’s messaging rates or gateway transaction fees) are yours. We are not responsible for third-party charges.',
      'We are not responsible for a provider’s outage, policy decision or change to its interface, though we will tell you when one affects the service.',
      'We use the credentials you store only to provide the service to your boutique. See section 8 of Part B for how they are protected.',
    ],
  },
  {
    id: 'terms-ip',
    part: 'terms',
    title: '8. Intellectual property',
    body: 'We and our licensors own the service, its software and its design. Your boutique owns its own business records, the content your team uploads, and the personal data of its clients. Nothing in these terms transfers ownership of your data to us.',
    bullets: [
      'You grant us the licence we need to host, process and display your content so that we can run the service for you.',
      'Where Aveline generates a draft, a description or a styling suggestion from your content, you may use it in your boutique.',
      'We may use aggregated and de-identified information (for example, how many boutiques used a feature) to operate and improve the service. We do not use the personal data of your clients for that purpose.',
    ],
  },
  {
    id: 'terms-availability',
    part: 'terms',
    title: '9. Availability, changes and support',
    body: 'We work to keep the service available, but we do not promise uninterrupted or error-free operation, and we may need to suspend it briefly for maintenance or to protect the service.',
    bullets: [
      'We may add, change or remove features. We will not change these terms to reduce your rights without notice.',
      'Support is by email at contact@aveline.lk. We do not currently offer a guaranteed response time.',
      'You are responsible for your own internet connection, devices and browser.',
    ],
  },
  {
    id: 'terms-termination',
    part: 'terms',
    title: '10. Suspension and termination',
    body: 'You may stop using the service at any time. We may suspend or end your access if you breach these terms, if we are required to by law, if your account is being used in a way that risks other boutiques or the service, or if a plan that is not free remains unpaid after notice.',
    bullets: [
      'When access ends, the boutique loses access to the workspace. We keep the data as described in section 7 of Part B, and we act on a deletion request as described in section 9.',
      'Sections that by their nature should survive termination (including intellectual property, liability and governing law) continue to apply.',
    ],
  },
  {
    id: 'terms-liability',
    part: 'terms',
    title: '11. Disclaimers and limits on liability',
    body: 'Aveline’s output is generated and can be wrong. You must check a figure, a price, a commitment or a suggestion before you act on it, and a person at your boutique remains responsible for every decision made with the service.',
    bullets: [
      'The service is provided on an "as is" basis during its preview period. To the extent Sri Lankan law allows, we exclude implied warranties of merchantability, fitness for a particular purpose and non-infringement.',
      'To the extent the law allows, we are not liable for lost profit, lost opportunity, or indirect or consequential loss.',
      'Where liability cannot be excluded, our total liability for all claims relating to the service is limited to the amounts you paid us for the service in the twelve months before the claim arose.',
      'Nothing here limits liability that cannot be limited by law.',
    ],
  },
  {
    id: 'terms-law',
    part: 'terms',
    title: '12. Governing law, changes and contact',
    body: 'These terms are governed by the laws of Sri Lanka, and the courts of Sri Lanka have exclusive jurisdiction over any dispute arising from them.',
    bullets: [
      'We may update these terms. When we do, we change the date at the top of this page, and a material change is announced in the product or by email to the boutique owner.',
      'Questions about these terms: contact@aveline.lk. Sales and multi-store enquiries: sales@aveline.lk. Aveline, Colombo, Sri Lanka.',
    ],
  },

  // ---------------------------------------------------------------- Part B
  {
    id: 'privacy-scope',
    part: 'privacy',
    title: '1. Scope, and who is responsible',
    body: 'This notice explains what personal data Aveline handles, why, who we share it with, how long we keep it, and how you can exercise your rights. It covers this website, the boutique dashboard and the mobile app.',
    bullets: [
      'For your clients’ data: your boutique is the data controller and decides why that data is processed. Aveline is the processor, and we act on the boutique’s instructions.',
      'For your own data as an owner or staff member (your account, your role, your activity in the workspace): Aveline is the controller.',
      'If you are a boutique’s client and want to exercise a right, contact the boutique first. We support the boutique in answering you, and you can also write to us at privacy@aveline.lk.',
    ],
  },
  {
    id: 'privacy-collect',
    part: 'privacy',
    title: '2. What we collect',
    body: 'We collect what is needed to run the service, and no more than that.',
    bullets: [
      'Client records your team creates: name, nickname, phone number, email, the level your team set, visit and spend figures, tags and notes.',
      'Client conversations and their content: messages sent to and from your boutique’s channels, photographs and documents your team or your clients send, voice and text notes, and the WhatsApp identifiers that carry them.',
      'Client memories: the preferences, occasions and observations your team records, and the derived attributes Aveline reads from a garment photograph.',
      'Your account data: name, username, email address, sign-in credentials and second-factor method (held by our identity provider), your boutique membership, your role, and your session.',
      'Integration credentials: the WhatsApp, Instagram and payment-gateway credentials you store, encrypted.',
      'Technical and security records: a hashed IP address and a user agent for audited actions, error and request identifiers, usage counts in Blossoms, and the audit trail of actions taken in your workspace.',
    ],
  },
  {
    id: 'privacy-why',
    part: 'privacy',
    title: '3. Why we use it, and our lawful basis',
    body: 'We process personal data to provide the service the boutique has asked for, to keep it secure, and to meet our legal duties.',
    bullets: [
      'To run the service: storing the client book, powering the concierge, delivering messages, generating drafts, and reporting usage. Our basis is the contract with the boutique.',
      'To keep the service safe: authentication, authorisation, rate limiting, abuse prevention and audit logs. Our basis is our legitimate interest in a secure service, and the contract.',
      'To bill and account for usage, where a plan is paid: our basis is the contract and our legal obligations.',
      'To meet legal duties: responding to a lawful request, or keeping a record we are required to keep. Our basis is a legal obligation.',
      'To send a customer a message on the boutique’s behalf: the boutique’s instruction and, where the law requires it, the client’s consent, which the client may withdraw at any time.',
    ],
  },
  {
    id: 'privacy-ai',
    part: 'privacy',
    title: '4. AI assistance, and automated decisions',
    body: 'Aveline uses AI models to read garment photographs, remember what your team tells it, draft replies and compose styling suggestions. To do that, the content needed for a request is sent to our AI providers so that they can return a result.',
    bullets: [
      'We do not make solely automated decisions that produce legal or similarly significant effects about a person. Aveline drafts and suggests; a person at the boutique decides, and where a discount or an order breaks a limit, the product pauses for a human approval.',
      'AI output can be wrong. Check it before you rely on it, particularly a price, a measurement or a commitment made to a client.',
      'We send the content of a request to an AI provider only to answer that request, under the provider’s API terms.',
    ],
  },
  {
    id: 'privacy-sharing',
    part: 'privacy',
    title: '5. Who we share it with',
    body: 'We do not sell personal data, and we do not share one boutique’s data with another. We share personal data with the service providers that make the product work, and only as far as each one needs it:',
    bullets: [
      'Clerk, our identity provider, which holds sign-in credentials and sessions.',
      'Cloudinary, which stores and delivers images and documents.',
      'Google, whose Gemini models produce embeddings for client memory and read visual attributes from garment photographs.',
      'Our language-model provider, which generates drafts and answers inside the concierge.',
      'Meta, for WhatsApp messages your boutique sends and receives through its own WhatsApp Business account.',
      'Infrastructure and observability providers that host and monitor the service.',
      'A purchaser of the business, if the service is ever sold, with notice to you; or a public authority, where the law requires disclosure.',
    ],
  },
  {
    id: 'privacy-location',
    part: 'privacy',
    title: '6. Where your data is processed',
    body: 'Aveline is operated from Sri Lanka, and some of the providers listed above process data outside Sri Lanka. Where that happens, we rely on the safeguards the provider offers for transfers of personal data, and we can tell you which provider holds what on request.',
    bullets: [
      'Sri Lanka’s Personal Data Protection Act, No. 9 of 2022, regulates transfers of personal data outside Sri Lanka. Those provisions are part of the commencement order that takes effect on 1 January 2027, and we are putting the transfer safeguards in place before that date.',
      'Our providers do not receive the whole client book. Each receives the part of the service it operates.',
    ],
  },
  {
    id: 'privacy-retention',
    part: 'privacy',
    title: '7. How long we keep it',
    body: 'Client data is kept while the boutique uses Aveline, so that the boutique’s record of a client is complete, and it is deleted or de-identified when the boutique asks us to delete it or when it is no longer needed for the purpose it was collected for.',
    bullets: [
      'We do not currently apply an automatic retention period to client records. Automatic retention is part of the work described in section 11.',
      'Integration credentials are kept until you disconnect the integration, which removes them.',
      'Security and audit records are kept for as long as we need them to investigate misuse and to meet our legal duties; they are written to keep personal data out of them where they can.',
      'When a deletion request is honoured, some records survive without personal content: for example the log that a message was received. That record is what lets us prove a client’s opt-out is being honoured.',
    ],
  },
  {
    id: 'privacy-security',
    part: 'privacy',
    title: '8. How we protect it',
    body: 'Access to your boutique’s data is decided by the server on every request, against your membership and your role, not by what the browser chooses to display.',
    bullets: [
      'Integration credentials are encrypted with AES-256-GCM and scoped to your boutique. Provider secrets are never returned to the browser; the dashboard shows a masked preview only.',
      'API keys are stored as a hash. The secret is shown once, when the key is created.',
      'Every request is authorised against the boutique it names and the permission it needs, so a session cannot reach a boutique it is no longer a member of.',
      'Actions that change consent, roles and money are recorded in an audit trail with the actor and, in hashed form, the network address.',
      'The Aveline web app does not load advertising or third-party analytics trackers. Our identity provider sets the cookies needed to keep you signed in.',
    ],
  },
  {
    id: 'privacy-rights',
    part: 'privacy',
    title: '9. Your rights',
    body: 'Under Sri Lanka’s Personal Data Protection Act you have rights over your personal data, and we apply them now, ahead of the dates on which each part of the Act comes into operation. You may ask us to:',
    bullets: [
      'tell you what personal data we hold about you, and give you a copy;',
      'correct data that is wrong or out of date;',
      'delete your data, where we have no lawful reason to keep it;',
      'stop processing you have objected to, or withdraw consent you gave;',
      'review a decision made about you by a fully automated process (we do not make decisions of that kind, as section 4 explains).',
      'Write to privacy@aveline.lk and tell us who you are, which boutique you are dealing with, what you want, and when. We may ask you to verify your identity and, where you are a boutique’s client, we may need to involve the boutique as the controller. We answer as quickly as we fairly can, and we will tell you if we need more time.',
    ],
  },
  {
    id: 'privacy-messaging',
    part: 'privacy',
    title: '10. WhatsApp messages, and opting out',
    body: 'When a client messages a boutique’s WhatsApp Business number, the message reaches the boutique’s workspace and Aveline may draft an answer. Aveline identifies itself as an assistant working for the boutique, and a member of the boutique’s team can take over the conversation at any time.',
    bullets: [
      'A client can stop these messages at any time by asking the boutique or by writing to privacy@aveline.lk. We record the request and stop processing their messages.',
      'An opt-out is honoured for the boutique it was made to. A client may also ask us to apply it everywhere Aveline holds a record for their number.',
      'We do not send marketing to a client. Only the boutique’s replies and service messages about the conversation are sent.',
    ],
  },
  {
    id: 'privacy-coming',
    part: 'privacy',
    title: '11. What we are adding',
    body: 'We are building the self-service side of this notice. Until each piece ships, requests are handled by our team through privacy@aveline.lk. When they ship, they will not widen the data we collect; they will make the rights in section 9 direct.',
    bullets: [
      'A first-contact WhatsApp message that names the boutique, says that Aveline is an AI assistant, and carries a permanent opt-out link.',
      'A verified opt-out page where a client enters their number, receives a one-time code, and stops processing for one boutique or for every Aveline boutique holding their number.',
      'Self-service export, returning everything we hold about a client as a machine-readable document.',
      'Self-service deletion, which removes the client record, its memory entries, its notes and its stored images, subject to the records we must keep.',
      'Automatic retention, so that client data is deleted or de-identified after a stated period of inactivity instead of being kept indefinitely.',
    ],
  },
  {
    id: 'privacy-children',
    part: 'privacy',
    title: '12. Children',
    body: 'The service is a tool for boutique staff and is not directed at children. We do not knowingly create accounts for anyone under 18. If you believe a child’s data has reached us, write to privacy@aveline.lk and we will remove it.',
  },
  {
    id: 'privacy-changes',
    part: 'privacy',
    title: '13. Changes to this notice',
    body: 'We update this notice when what we do changes. The date at the top of the page always shows the current version, and we announce a material change in the product or by email to the boutique owner before it takes effect.',
  },
  {
    id: 'privacy-contact',
    part: 'privacy',
    title: '14. How to contact us, and how to complain',
    body: 'Write to privacy@aveline.lk for any question or request about personal data, including a request under section 9. Our postal address is Aveline, Colombo, Sri Lanka.',
    bullets: [
      'If you are not satisfied with our answer, you can complain to the Data Protection Authority of Sri Lanka: First Floor, Block 5, BMICH, Bauddhaloka Mawatha, Colombo 07; info@dpa.gov.lk; +94 011 269 7241.',
      'Sri Lanka’s Personal Data Protection Act, No. 9 of 2022 is being brought into operation in stages. Extraordinary Gazette No. 2498/16 of 22 July 2026 appoints 1 January 2027 for the Act’s core processing provisions (Parts I and III), while the data-subject-rights provisions (Part II) and the penalty provisions (Part VII) have not yet been given an operational date.',
      'We apply the standards in this notice now, rather than waiting for those dates.',
    ],
  },
]

const PART_TITLES: Record<Part, string> = {
  terms: 'Part A. Terms & Conditions',
  privacy: 'Part B. Privacy Notice',
}

/**
 * Public terms & privacy reference page (linked from sign-up, sign-in and the site footer).
 * `#privacy` is the anchor those links use, so the Privacy Notice's heading keeps that id.
 */
export function TermsPage() {
  const renderSection = ({ id, title, body, bullets }: Section) => (
    <section
      key={id}
      id={id}
      className="scroll-mt-6 rounded-2xl border-2 border-dashed border-neutral-300 bg-white p-6"
    >
      <h3 className="text-base font-semibold text-neutral-900">{title}</h3>
      <p className="mt-2 text-sm leading-relaxed text-neutral-600">{body}</p>
      {bullets ? (
        <ul className="mt-3 flex flex-col gap-2">
          {bullets.map((point, index) => (
            <li key={`${id}-${index}`} className="flex gap-2 text-sm leading-relaxed text-neutral-600">
              <span aria-hidden className="mt-2 size-1 shrink-0 rounded-full bg-neutral-400" />
              <span>{point}</span>
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  )

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
          Last updated 23 September 2026 · Part A is our agreement with you, and Part B is our
          Privacy Notice.
        </p>

        <nav aria-label="Contents" className="mt-6 rounded-2xl bg-white/70 p-4">
          <h2 className="text-sm font-semibold text-neutral-900">Contents</h2>
          {(['terms', 'privacy'] as Part[]).map((part) => (
            <div key={part} className="mt-3">
              <p className="text-xs font-semibold uppercase tracking-wider text-neutral-500">
                {PART_TITLES[part]}
              </p>
              <ul className="mt-1 flex flex-col gap-1">
                {SECTIONS.filter((section) => section.part === part).map((section) => (
                  <li key={section.id}>
                    <a href={`#${section.id}`} className="text-sm text-[#7a303f] hover:underline">
                      {section.title}
                    </a>
                  </li>
                ))}
              </ul>
            </div>
          ))}
        </nav>

        <h2 className="mt-10 font-serif text-2xl font-medium text-neutral-900">
          {PART_TITLES.terms}
        </h2>
        <div className="mt-4 flex flex-col gap-6">
          {SECTIONS.filter((section) => section.part === 'terms').map(renderSection)}
        </div>

        <h2 id="privacy" className="mt-12 scroll-mt-6 font-serif text-2xl font-medium text-neutral-900">
          {PART_TITLES.privacy}
        </h2>
        <div className="mt-4 flex flex-col gap-6">
          {SECTIONS.filter((section) => section.part === 'privacy').map(renderSection)}
        </div>
      </div>
    </div>
  )
}
