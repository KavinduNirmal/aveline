import { Link } from 'react-router-dom'
import { ArrowRight, CheckCircle2, FileText, RotateCcw, ShieldOff } from 'lucide-react'

import { SitePage } from '@/components/site/SitePage'

interface State {
  name: string
  icon: typeof CheckCircle2
  summary: string
  detail: string
  tone: string
  ink: string
}

/**
 * The customer-facing consent states, in the order a person meets them. The names match the values
 * the API stores (`pending` / `granted` / `revoked`), and the copy says what each one means for
 * processing rather than describing the database.
 */
const STATES: State[] = [
  {
    name: 'Pending',
    icon: FileText,
    summary: 'Nothing is processed until consent is granted.',
    detail:
      'A new number starts here. The boutique cannot use the conversation to build a memory until the choice is recorded as a yes.',
    tone: 'border-neutral-200 bg-neutral-50',
    ink: 'text-neutral-600',
  },
  {
    name: 'Granted',
    icon: CheckCircle2,
    summary: 'Messages are answered and remembered.',
    detail:
      'The boutique may answer you, remember your preferences, and use them to help its staff. You can change this at any time.',
    tone: 'border-emerald-200 bg-emerald-50/60',
    ink: 'text-emerald-700',
  },
  {
    name: 'Revoked',
    icon: ShieldOff,
    summary: 'Processing stops and existing entries are not used.',
    detail:
      'The boutique will not answer you through Aveline and will not use what it remembered. You can still contact the boutique directly as a customer.',
    tone: 'border-rose-200 bg-rose-50/60',
    ink: 'text-rose-700',
  },
]

/**
 * The non-authenticated explainer at `/privacy/consent-flow` (plan §8.5.5 item 4). It is readable
 * by a customer who has no Aveline account and no boutique session, and it may be the page a
 * disclosure's `{data_policy_url}` points at. Nothing here requires a sign-in.
 */
export function ConsentFlowPage() {
  return (
    <SitePage>
      <main className="mx-auto w-full max-w-4xl px-5 py-16 lg:px-8">
        <p className="text-xs font-semibold uppercase tracking-[0.22em] text-primary">
          Privacy &amp; Transparency
        </p>
        <h1 className="mt-3 font-serif text-4xl font-medium tracking-tight text-neutral-900 sm:text-5xl">
          Consent, in plain language
        </h1>
        <p className="mt-4 max-w-2xl text-base leading-relaxed text-neutral-600">
          When a boutique uses Aveline to remember its clients, each number has one consent record
          per boutique. This page shows what each state means, how a customer moves between them,
          and where to change the choice. You do not need an account to read it or to act on it.
        </p>

        <ol className="mt-12 grid gap-6 md:grid-cols-3">
          {STATES.map((state, index) => {
            const Icon = state.icon
            return (
              <li key={state.name}>
                <div className={`flex h-full flex-col rounded-2xl border-2 border-dashed p-6 ${state.tone}`}>
                  <div className="flex items-center justify-between">
                    <span className="flex size-11 items-center justify-center rounded-xl border border-neutral-200/70 bg-white">
                      <Icon className={`size-5 ${state.ink}`} aria-hidden="true" />
                    </span>
                    <span className="text-xs font-semibold uppercase tracking-[0.18em] text-neutral-400">
                      Step {index + 1}
                    </span>
                  </div>
                  <h3 className="mt-5 font-serif text-2xl font-medium text-neutral-900">
                    {state.name}
                  </h3>
                  <p className="mt-2 text-sm font-medium text-neutral-800">{state.summary}</p>
                  <p className="mt-2 text-sm leading-relaxed text-neutral-600">{state.detail}</p>
                </div>
              </li>
            )
          })}
        </ol>

        <div className="mt-6 flex items-center justify-center gap-3 text-sm text-neutral-400">
          <ArrowRight className="size-4" aria-hidden="true" />
          <span>or back again, whenever the customer chooses</span>
        </div>

        <ul className="mt-6 grid gap-6 md:grid-cols-1">
          <li>
            <div className="flex h-full flex-col rounded-2xl border-2 border-dashed border-neutral-200 bg-white p-6">
              <span className="flex size-11 items-center justify-center rounded-xl border border-neutral-200/70 bg-white">
                <RotateCcw className="size-5 text-lavender" aria-hidden="true" />
              </span>
              <h3 className="mt-5 font-serif text-2xl font-medium text-neutral-900">Re-grant</h3>
              <p className="mt-2 text-sm font-medium text-neutral-800">
                A choice can be reversed at any time.
              </p>
              <p className="mt-2 text-sm leading-relaxed text-neutral-600">
                If a customer later wants the boutique to remember them again, the boutique records
                a fresh yes and processing resumes from that point. Changing a choice is never
                penalised.
              </p>
            </div>
          </li>
        </ul>

        <section className="mt-14 rounded-2xl border-2 border-dashed border-neutral-200 bg-white p-6">
          <h2 className="font-serif text-2xl font-medium text-neutral-900">
            How a customer changes the choice
          </h2>
          <p className="mt-3 text-sm leading-relaxed text-neutral-600">
            The WhatsApp message that introduced Aveline carries a permanent opt-out link. Opening
            it takes the customer to a page that asks for the number and a one-time code sent to
            that number on WhatsApp. No account and no email are involved.
          </p>
          <div className="mt-5 flex flex-col gap-3 sm:flex-row">
            <Link
              to="/privacy/opt-out"
              className="inline-flex items-center justify-center gap-2 rounded-full bg-primary px-5 py-2.5 text-sm font-medium text-primary-foreground transition-colors hover:bg-primary/90"
            >
              Open the opt-out page
              <ArrowRight className="size-4" aria-hidden="true" />
            </Link>
            <Link
              to="/privacy"
              className="inline-flex items-center justify-center rounded-full border border-neutral-300 bg-white px-5 py-2.5 text-sm font-medium text-neutral-800 transition-colors hover:border-primary/50 hover:text-primary"
            >
              Read the Data Policy
            </Link>
          </div>
        </section>
      </main>
    </SitePage>
  )
}
