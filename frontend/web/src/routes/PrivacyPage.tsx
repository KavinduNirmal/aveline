import type { ReactNode } from 'react'

import { Link, useSearchParams } from 'react-router-dom'

import { SitePage } from '@/components/site/SitePage'

/** The date the current copy took effect. Bump it when the substance changes. */
const LAST_UPDATED = '25 September 2026'

/**
 * The canonical data policy (`/privacy`). This is the `{data_policy_url}` target the first-contact
 * WhatsApp disclosure links to, so it is written to stand on its own: it states the controller and
 * processor positions (plan §15 Q-8), lists what is collected and why, names the consent states,
 * and says plainly that Instagram is **not** covered (plan §15 Q-5 - no Instagram provider ships,
 * and parity is not promised).
 *
 * `/terms#privacy` remains the legal notice inside the Terms page; this route is the customer-facing
 * policy, and the two must not contradict each other.
 */
export function PrivacyPage() {
  const [searchParams] = useSearchParams()
  const org = searchParams.get('org')?.trim() ?? ''

  return (
    <SitePage>
      <main className="mx-auto w-full max-w-3xl px-5 py-16 lg:px-8">
        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-primary">
          Privacy &amp; Transparency
        </p>
        <h1 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
          Aveline Data Policy
        </h1>
        <p className="mt-4 text-base leading-relaxed text-neutral-600">
          How a boutique&rsquo;s use of Aveline handles the personal data of the people who message
          it. Last updated {LAST_UPDATED}.
        </p>

        {org ? (
          <p className="mt-5 rounded-2xl border-2 border-dashed border-neutral-200 bg-white p-4 text-sm leading-relaxed text-neutral-600">
            You followed this link from a boutique&rsquo;s message. The boutique it refers to uses
            the slug <code className="font-mono text-neutral-900">{org}</code>. If you are unsure
            which boutique that is, check the name in the WhatsApp message that sent you here.
          </p>
        ) : null}

        <nav aria-label="Contents" className="mt-8 rounded-2xl border-2 border-dashed border-neutral-200 bg-white/70 p-5">
          <h2 className="text-sm font-semibold text-neutral-900">On this page</h2>
          <ul className="mt-2 grid gap-1.5 sm:grid-cols-2">
            {[
              ['#who-is-responsible', 'Who is responsible'],
              ['#what-we-collect', 'What we collect'],
              ['#ai-assistant', 'AI assistance'],
              ['#channels', 'Which channels this covers'],
              ['#consent', 'Consent states'],
              ['#opt-out', 'Stop processing'],
              ['#copy-and-erase', 'Copy or erase'],
              ['#retention', 'How long we keep it'],
              ['#security', 'How we protect it'],
              ['#contact', 'Contact and complaints'],
            ].map(([href, label]) => (
              <li key={href}>
                <a href={href} className="text-sm text-primary hover:underline">
                  {label}
                </a>
              </li>
            ))}
          </ul>
        </nav>

        <div className="mt-10 flex flex-col gap-6">
          <Section id="who-is-responsible" title="Who is responsible for your data">
            <p>
              Your boutique is the controller of the personal data it keeps about you. The boutique
              decides what is collected, why it is kept, who inside the boutique can see it, and how
              long it is retained.
            </p>
            <p>
              Aveline is the processor. We operate the software the boutique uses, and we process
              your data only on the boutique&rsquo;s instructions and for the purposes described in
              this policy. We do not sell your data, and we do not use it for advertising.
            </p>
            <p>
              Because the boutique is the controller, the boutique is your first point of contact
              for a question or a request about your data. We help the boutique answer it, and we
              run the technical flows this policy describes.
            </p>
          </Section>

          <Section id="what-we-collect" title="What we collect, and why">
            <Bullets
              items={[
                'How to reach you: your name, your WhatsApp number, and the identifier WhatsApp gives the boutique for your conversation.',
                'What you tell us: the messages you send, including photographs, documents and voice notes you choose to share.',
                'What you prefer: sizes, colours, occasions, favourite pieces and other preferences you mention, so the boutique can remember them instead of asking again.',
                'What you bought or ordered: the items, deposits, payments and delivery details the boutique records for your order.',
                'When you contacted us: the time and channel of each message, which the boutique uses to answer you and to prove it honoured your choices.',
              ]}
            />
            <p>
              We use this data to answer your messages, to remember your preferences, to draft
              replies and suggestions for the boutique&rsquo;s staff, to prepare orders and
              deliveries, and to meet the boutique&rsquo;s legal and accounting duties. We keep the
              collection to what those purposes need.
            </p>
          </Section>

          <Section id="ai-assistant" title="When an AI assistant is involved">
            <p>
              Aveline is an AI assistant working on behalf of the boutique. When you first message
              the boutique on WhatsApp, Aveline says so: the message names the boutique, explains
              that an AI assistant helps answer, and makes clear that a real member of the
              boutique&rsquo;s team reads every conversation and can step in at any time.
            </p>
            <p>
              Aveline drafts and suggests. A person at the boutique decides what is sent and can
              take over the conversation. We do not use a fully automated process to make a decision
              that has a legal or similarly significant effect on you.
            </p>
          </Section>

          <Section id="channels" title="Which channels this covers">
            <p>
              These disclosures, the permanent opt-out link and the verified opt-out flow are built
              for WhatsApp today.
            </p>
            <p>
              Instagram is not yet covered. No Instagram messaging provider ships with Aveline, and
              the disclosure, opt-out and messaging-window rules for Instagram have not been
              confirmed. Until that work is done, we do not promise that Instagram direct messages
              carry the same disclosure, the same opt-out link or the same rules as WhatsApp. If
              your boutique messages you on Instagram, ask it directly about your choices, or write
              to us at the address below.
            </p>
          </Section>

          <Section id="consent" title="Consent states">
            <p>
              Each boutique keeps one consent record for your number. It is private to that boutique
              and it is one of the following:
            </p>
            <dl className="mt-2 flex flex-col gap-3">
              <div>
                <dt className="font-semibold text-neutral-900">Pending</dt>
                <dd className="text-neutral-600">
                  Nothing is processed until consent is given. A new number starts here.
                </dd>
              </div>
              <div>
                <dt className="font-semibold text-neutral-900">Granted</dt>
                <dd className="text-neutral-600">
                  Messages are answered and remembered for that boutique, as this policy describes.
                </dd>
              </div>
              <div>
                <dt className="font-semibold text-neutral-900">Revoked</dt>
                <dd className="text-neutral-600">
                  Processing stops and existing entries are not used to answer you.
                </dd>
              </div>
            </dl>
            <p>
              A choice is not a trap. If you later change your mind, a boutique can record that
              consent again with a fresh record, and processing resumes from that point.{' '}
              <Link to="/privacy/consent-flow" className="text-primary hover:underline">
                See the consent flow
              </Link>{' '}
              for the customer-facing view of these states.
            </p>
          </Section>

          <Section id="opt-out" title="Stopping processing (opt out)">
            <p>
              You can stop processing at any time. The WhatsApp message that introduced Aveline
              carries a permanent link, and it never expires:{' '}
              <Link to="/privacy/opt-out" className="text-primary hover:underline">
                the opt-out page
              </Link>
              .
            </p>
            <p>
              You choose how far it reaches: the boutique that messaged you, or every Aveline
              boutique that holds your number. You prove the number with a one-time code sent to it
              on WhatsApp, so an opt-out cannot be made for someone else. The link proves which
              boutique the request started from; the code proves which number.
            </p>
            <p>
              You do not need an Aveline account, and you do not need to speak to anyone. Once the
              request is confirmed, the boutique stops processing your messages.
            </p>
          </Section>

          <Section id="copy-and-erase" title="Getting a copy, or erasing your data">
            <p>
              You may ask for a machine-readable copy of everything a boutique holds about you, and
              you may ask for it to be erased. Both run through the same one-time-code proof as the
              opt-out, so we can be sure the request comes from the number concerned.
            </p>
            <p>
              The customer-facing pages for these two requests are being added. Until they ship,
              write to{' '}
              <a href="mailto:privacy@aveline.lk" className="text-primary hover:underline">
                privacy@aveline.lk
              </a>{' '}
              and we will run the request with you. Where the boutique is the controller, we may ask
              the boutique to confirm before erasing its record.
            </p>
          </Section>

          <Section id="retention" title="How long we keep it">
            <ul className="flex flex-col gap-2">
              <Bullet>
                A boutique&rsquo;s record of you is kept while the boutique uses Aveline, so its
                record of the relationship is complete. We do not currently apply an automatic
                retention period; automatic retention is part of the work described above.
              </Bullet>
              <Bullet>
                When a request to erase is honoured, some records survive without personal content,
                for example the log that a message was received. That log is what lets us prove that
                a choice is being honoured.
              </Bullet>
              <Bullet>
                Integration credentials are kept until the boutique disconnects the integration,
                which removes them.
              </Bullet>
              <Bullet>
                Security and audit records are kept for as long as we need them to investigate
                misuse and meet our legal duties, written to keep personal data out of them where
                they can.
              </Bullet>
            </ul>
          </Section>

          <Section id="security" title="How we protect it">
            <p>
              Access is decided by the server on every request, against the boutique&rsquo;s
              membership and the person&rsquo;s role, not by what a browser chooses to display.
            </p>
            <ul className="flex flex-col gap-2">
              <Bullet>
                Integration credentials are encrypted and scoped to the boutique. Provider secrets
                are never returned to a browser.
              </Bullet>
              <Bullet>
                Actions that change consent, roles or money are recorded in an audit trail with the
                actor and, in hashed form, the network address. The audit trail holds identifiers,
                not message content.
              </Bullet>
              <Bullet>
                One-time opt-out codes are stored only as a hash, expire quickly, and are limited in
                how many times they can be guessed.
              </Bullet>
              <Bullet>
                The Aveline web app does not load advertising or third-party analytics trackers. Our
                identity provider sets the cookies needed to keep a signed-in user signed in;
                customers who are not boutique users have no account and no such cookie.
              </Bullet>
            </ul>
          </Section>

          <Section id="contact" title="Contact and complaints">
            <p>
              Write to{' '}
              <a href="mailto:privacy@aveline.lk" className="text-primary hover:underline">
                privacy@aveline.lk
              </a>{' '}
              for any question or request about personal data, including a copy or an erasure. Our
              postal address is Aveline, Colombo, Sri Lanka.
            </p>
            <p>
              If you are not satisfied with our answer, you can complain to the Data Protection
              Authority of Sri Lanka: First Floor, Block 5, BMICH, Bauddhaloka Mawatha, Colombo 07;
              info@dpa.gov.lk; +94 011 269 7241.
            </p>
            <p>
              Sri Lanka&rsquo;s Personal Data Protection Act, No. 9 of 2022 is being brought into
              operation in stages. We apply the standards in this policy now, rather than waiting
              for those dates.
            </p>
          </Section>
        </div>

        <p className="mt-10 text-sm text-neutral-500">
          See also our{' '}
          <Link to="/terms" className="text-primary hover:underline">
            Terms &amp; Conditions
          </Link>{' '}
          and the{' '}
          <Link to="/privacy/consent-flow" className="text-primary hover:underline">
            consent states, explained
          </Link>
          .
        </p>
      </main>
    </SitePage>
  )
}

function Section({
  id,
  title,
  children,
}: {
  id: string
  title: string
  children: ReactNode
}) {
  return (
    <section
      id={id}
      className="scroll-mt-6 rounded-2xl border-2 border-dashed border-neutral-200 bg-white p-6"
    >
      <h2 className="font-serif text-2xl font-medium text-neutral-900">{title}</h2>
      <div className="mt-3 flex flex-col gap-3 text-sm leading-relaxed text-neutral-600">
        {children}
      </div>
    </section>
  )
}

function Bullets({ items }: { items: string[] }) {
  return (
    <ul className="flex flex-col gap-2">
      {items.map((item) => (
        <Bullet key={item}>{item}</Bullet>
      ))}
    </ul>
  )
}

function Bullet({ children }: { children: ReactNode }) {
  return (
    <li className="flex gap-2">
      <span aria-hidden="true" className="mt-2 size-1 shrink-0 rounded-full bg-neutral-400" />
      <span>{children}</span>
    </li>
  )
}
