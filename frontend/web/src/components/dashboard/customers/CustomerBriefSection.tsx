import { useEffect, useState } from 'react'

import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { fetchCustomerBrief, type TenantCustomerBrief } from '@/lib/customers-api'
import { toApiError } from '@/lib/api-error'

interface CustomerBriefSectionProps {
  organizationId: string
  customerId: string
}

const EVENT_LABEL: Record<string, string> = {
  wedding: 'Wedding',
  birthday: 'Birthday',
  party: 'Party',
  office: 'Work function',
  anniversary: 'Anniversary',
  other: 'Occasion',
}

/**
 * The pre-contact brief: what an associate reads before making contact.
 *
 * It is deliberately a read of its own rather than a projection of the client record. The record
 * answers "what do we know about this client"; the brief answers "what do I need before I speak to
 * them", and it is the only surface that leads with the description and with what is coming up.
 *
 * The stored notes are consent-gated on the server. When they are empty the section says why
 * (`consentStatus`) rather than reporting "nothing on file", because those are different facts and
 * an associate acting on the wrong one will decide the boutique knows nothing about a client whose
 * notes simply may not be shown.
 */
export function CustomerBriefSection({ organizationId, customerId }: CustomerBriefSectionProps) {
  const [brief, setBrief] = useState<TenantCustomerBrief | null>(null)
  const [isLoading, setIsLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    const load = async () => {
      setIsLoading(true)
      setError(null)
      try {
        const next = await fetchCustomerBrief(organizationId, customerId)
        if (!cancelled) setBrief(next)
      } catch (caught) {
        if (cancelled) return
        const apiError = toApiError(caught)
        setError(apiError.status === 404 ? 'This client is not in this boutique.' : 'Could not load the brief.')
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    }
    void load()
    return () => {
      cancelled = true
    }
  }, [organizationId, customerId])

  if (isLoading) {
    return (
      <div className="flex flex-col gap-2">
        <Skeleton className="h-5 w-32" />
        <Skeleton className="h-16 w-full" />
      </div>
    )
  }

  if (error) {
    return (
      <div className="rounded-lg border border-border bg-muted/40 p-3">
        <p className="text-xs text-muted-foreground">{error}</p>
      </div>
    )
  }

  if (!brief) return null

  const memoriesHidden = brief.consentStatus !== 'granted'

  return (
    <section className="flex flex-col gap-3 rounded-lg border border-border bg-muted/30 p-4">
      <header className="flex items-baseline justify-between gap-2">
        <h3 className="font-serif text-sm font-medium">Before you make contact</h3>
        <span className="text-xs text-muted-foreground capitalize">
          {brief.status === 'new' ? 'New client' : brief.status}
        </span>
      </header>

      {brief.description ? (
        <p className="text-sm leading-relaxed">{brief.description}</p>
      ) : (
        <p className="text-xs text-muted-foreground">
          No description written for this client yet.
        </p>
      )}

      {brief.preferenceSummary ? (
        <div className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wide text-muted-foreground">Reaching them</span>
          <p className="text-sm">{brief.preferenceSummary}</p>
        </div>
      ) : null}

      {brief.upcomingEvents.length > 0 ? (
        <div className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wide text-muted-foreground">Coming up</span>
          <ul className="flex flex-col gap-0.5">
            {brief.upcomingEvents.map((event) => (
              <li key={event.id} className="text-sm">
                {EVENT_LABEL[event.eventType] ?? event.eventType} on{' '}
                {new Date(event.eventDate).toLocaleDateString()}
                {event.description ? ` — ${event.description}` : ''}
              </li>
            ))}
          </ul>
        </div>
      ) : (
        <div className="flex flex-col gap-1">
          <span className="text-xs uppercase tracking-wide text-muted-foreground">Coming up</span>
          <p className="text-sm text-muted-foreground">No upcoming occasions on file.</p>
        </div>
      )}

      {brief.tags.length > 0 ? (
        <div className="flex flex-wrap gap-1.5">
          {brief.tags.map((tag) => (
            <Badge key={tag} variant="outline">
              {tag}
            </Badge>
          ))}
        </div>
      ) : null}

      <div className="flex flex-col gap-1">
        <span className="text-xs uppercase tracking-wide text-muted-foreground">
          What Aveline remembers
        </span>
        {memoriesHidden ? (
          <p className="text-sm text-muted-foreground">
            {brief.consentStatus === 'revoked'
              ? 'This client has opted out, so nothing derived from their messages is shown.'
              : 'This client has not given consent yet, so nothing derived from their messages is shown.'}
          </p>
        ) : brief.memories.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nothing on file yet.</p>
        ) : (
          <ul className="flex flex-col gap-1">
            {brief.memories.map((memory) => (
              <li key={memory.id} className="flex flex-col gap-0.5 text-sm">
                <span>{memory.content}</span>
                <span className="text-xs text-muted-foreground">
                  {memory.category}
                  {/* Stated versus inferred is the distinction an associate acts on. */}
                  {' · '}
                  {memory.isExplicit ? 'Stated by the client' : 'Inferred'}
                </span>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  )
}
