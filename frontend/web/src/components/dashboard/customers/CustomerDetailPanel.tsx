import { useCallback, useEffect, useState } from 'react'
import { ArrowLeft, CalendarClock, Mail, Pencil, Phone, Plus, Trash2 } from 'lucide-react'
import { toast } from 'sonner'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { ScrollArea } from '@/components/ui/scroll-area'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { Textarea } from '@/components/ui/textarea'
import { CustomerBriefSection } from '@/components/dashboard/customers/CustomerBriefSection'
import { LogVisitDialog } from '@/components/dashboard/customers/LogVisitDialog'
import { toApiError } from '@/lib/api-error'
import {
  deleteCustomer,
  fetchCustomer,
  fetchCustomerEvents,
  fetchCustomerInteractions,
  fetchCustomerMemories,
  updateCustomer,
  type CustomerInteractionItem,
  type TenantCustomerDetail,
  type TenantCustomerEvent,
  type TenantCustomerMemory,
  type TenantCustomerPreference,
} from '@/lib/customers-api'
import { formatMoney } from '@/lib/format-money'
import { hasPermission } from '@/lib/permissions'
import type { OrganizationProfileDto } from '@/types/organization'

interface CustomerDetailPanelProps {
  organization: OrganizationProfileDto
  role: string
  customerId: string
  /** Returns to the book. The list is a different URL, so this is navigation, not state. */
  onClose: () => void
}

interface LoadedDetail {
  detail: TenantCustomerDetail
  memories: TenantCustomerMemory[]
  events: TenantCustomerEvent[]
  interactions: CustomerInteractionItem[]
}

type LoadState =
  | { kind: 'loading' }
  | { kind: 'notFound' }
  | { kind: 'error' }
  | ({ kind: 'ready' } & LoadedDetail)

const LEVELS = ['level1', 'level2', 'level3', 'vip'] as const

/** How many exchanges the rail shows before it stops being a rail. */
const ACTIVITY_WINDOW = 20

const EVENT_LABEL: Record<string, string> = {
  wedding: 'Wedding',
  birthday: 'Birthday',
  anniversary: 'Anniversary',
  party: 'Party',
  office: 'Work function',
  other: 'Occasion',
}

/**
 * One client's full page at `/app/b/:slug/customers/:customerId` (E-6…E-9).
 *
 * The book used to open this record in a side sheet, which could only ever show a summary: a sheet
 * is as wide as the viewport it slides over, and the record has more to say than a column can hold.
 * A page of its own carries what the associate's mobile screen carries - who they are, the
 * boutique's own description of them, the brief, what is known about their taste, what Aveline
 * remembers with the stated/inferred distinction, the exchange history and the occasions ahead -
 * and it is linkable, so "look at this client" is a URL rather than an instruction.
 *
 * The order is deliberate: identity, then the brief (the one block read *before* contact), then the
 * next occasion, then the facts, then the history. The deadline sits high because it is the only
 * thing on the page that expires.
 */
export function CustomerDetailPanel({
  organization,
  role,
  customerId,
  onClose,
}: CustomerDetailPanelProps) {
  const organizationId = organization.id
  const canManage = hasPermission(role, 'customers:manage')

  const [state, setState] = useState<LoadState>({ kind: 'loading' })
  const [refreshToken, setRefreshToken] = useState(0)
  const [visitOpen, setVisitOpen] = useState(false)
  /**
   * Whether the record is being edited.
   *
   * Held here rather than in the record body because the Edit control lives in the page head, and
   * because the form replaces the record instead of sitting beside it.
   */
  const [isEditing, setIsEditing] = useState(false)

  const reload = useCallback(() => setRefreshToken((token) => token + 1), [])

  useEffect(() => {
    let cancelled = false
    const controller = new AbortController()
    setState({ kind: 'loading' })

    // The four reads are independent, so they run together. The events and memories lists are
    // best-effort: a refused read of either degrades to its empty value rather than failing a page
    // whose identity and history did load. The detail read is the one that decides the page, because
    // its 404 is the server's answer about whether this client is even in the boutique.
    async function load() {
      try {
        const detail = await fetchCustomer(organizationId, customerId, controller.signal)
        const [interactions, memories, events] = await Promise.all([
          fetchCustomerInteractions(
            organizationId,
            customerId,
            { page: 1, pageSize: ACTIVITY_WINDOW },
            controller.signal,
          ),
          fetchCustomerMemories(organizationId, customerId, controller.signal).catch(() => []),
          fetchCustomerEvents(organizationId, customerId, controller.signal).catch(() => []),
        ])
        if (cancelled) return
        setState({
          kind: 'ready',
          detail,
          memories,
          events,
          interactions: interactions.items,
        })
      } catch (caught) {
        if (cancelled) return
        const apiError = toApiError(caught)
        // A 404 is the server's deliberate answer for "not in this boutique" and for "deleted"
        // alike, so the page must not guess which it was.
        setState(apiError.status === 404 ? { kind: 'notFound' } : { kind: 'error' })
      }
    }

    void load()
    return () => {
      cancelled = true
      controller.abort()
    }
  }, [organizationId, customerId, refreshToken])

  return (
    <div className="flex flex-col gap-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="flex flex-col gap-3">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            className="-ml-2 w-fit gap-1.5 text-muted-foreground"
            onClick={onClose}
          >
            <ArrowLeft className="size-4" aria-hidden />
            Clients
          </Button>
          <div>
            <p className="text-sm font-medium uppercase tracking-[0.15em] text-muted-foreground">
              {organization.name}
            </p>
            <h1 className="mt-2 font-serif text-4xl font-medium tracking-tight">
              {state.kind === 'ready'
                ? state.detail.fullName ?? state.detail.nickname ?? 'Unnamed client'
                : 'Client'}
            </h1>
          </div>
        </div>

        {state.kind === 'ready' ? (
          <div className="flex flex-wrap gap-2">
            <Button type="button" size="sm" onClick={() => setVisitOpen(true)} className="gap-1.5">
              <Plus className="size-3.5" aria-hidden /> Log a visit
            </Button>
            {canManage ? (
              <>
                <Button
                  type="button"
                  size="sm"
                  variant="outline"
                  className="gap-1.5"
                  onClick={() => setIsEditing(true)}
                >
                  <Pencil className="size-3.5" aria-hidden /> Edit
                </Button>
                <RemoveAction
                  organizationId={organizationId}
                  detail={state.detail}
                  onRemoved={onClose}
                />
              </>
            ) : null}
          </div>
        ) : null}
      </div>

      {state.kind === 'loading' ? (
        <div className="flex flex-col gap-3" aria-label="Loading client">
          <Skeleton className="h-28 w-full" />
          <Skeleton className="h-40 w-full" />
          <Skeleton className="h-24 w-full" />
        </div>
      ) : state.kind === 'notFound' ? (
        <Card>
          <CardContent className="flex flex-col items-start gap-3 pt-6">
            <p className="text-sm font-medium">This client is not in this boutique.</p>
            <p className="text-xs text-muted-foreground">
              A removed client and a client belonging to another boutique are deliberately
              indistinguishable, so this page cannot say which it was.
            </p>
            <Button type="button" variant="outline" size="sm" onClick={onClose}>
              Back to the client book
            </Button>
          </CardContent>
        </Card>
      ) : state.kind === 'error' ? (
        <Card>
          <CardContent className="flex flex-col items-start gap-3 pt-6">
            <p className="text-sm text-destructive">Could not load this client.</p>
            <Button type="button" variant="outline" size="sm" onClick={reload}>
              Try again
            </Button>
          </CardContent>
        </Card>
      ) : isEditing ? (
        // The form replaces the record rather than sitting beside it: a half-updated record on
        // screen next to the form about to change it is two sources of truth for one client.
        <EditForm
          organizationId={organizationId}
          detail={state.detail}
          onCancel={() => setIsEditing(false)}
          onSaved={() => {
            setIsEditing(false)
            reload()
          }}
        />
      ) : (
        <ReadyDetail
          organizationId={organizationId}
          detail={state.detail}
          memories={state.memories}
          events={state.events}
          interactions={state.interactions}
        />
      )}

      <LogVisitDialog
        organizationId={organizationId}
        customer={
          state.kind === 'ready'
            ? {
                customerId: state.detail.customerId,
                fullName: state.detail.fullName,
                nickname: state.detail.nickname,
              }
            : null
        }
        open={visitOpen}
        onOpenChange={setVisitOpen}
        onRecorded={reload}
      />
    </div>
  )
}

/**
 * The destructive write, with its own in-flight state.
 *
 * Kept apart from the rest of the head so a removal attempt does not disable "Log a visit" and
 * "Edit" while it is in flight.
 */
function RemoveAction({
  organizationId,
  detail,
  onRemoved,
}: {
  organizationId: string
  detail: TenantCustomerDetail
  onRemoved: () => void
}) {
  const [isRemoving, setIsRemoving] = useState(false)

  const remove = async () => {
    setIsRemoving(true)
    try {
      await deleteCustomer(organizationId, detail.customerId)
      toast.success('Client removed', {
        description: 'The record is hidden, and their orders stay readable.',
      })
      onRemoved()
    } catch (caught) {
      const apiError = toApiError(caught)
      toast.error('Could not remove this client', {
        description:
          apiError.code === 'customer-has-open-orders'
            ? 'This client has orders that are still live. Close or cancel them first.'
            : apiError.message,
      })
    } finally {
      setIsRemoving(false)
    }
  }

  return (
    <Button
      type="button"
      size="sm"
      variant="outline"
      disabled={isRemoving}
      onClick={() => void remove()}
    >
      <Trash2 className="size-3.5 text-destructive" aria-hidden /> Remove
    </Button>
  )
}

interface ReadyDetailProps extends LoadedDetail {
  organizationId: string
}

/**
 * The record itself, once loaded, in the order an associate reads it: identity and the brief first,
 * then the next occasion, then the facts, then the history.
 */
function ReadyDetail({
  organizationId,
  detail,
  memories,
  events,
  interactions,
}: ReadyDetailProps) {
  const now = Date.now()
  const upcoming = events
    .filter((event) => Date.parse(event.eventDate) >= startOfToday(now))
    .sort((a, b) => Date.parse(a.eventDate) - Date.parse(b.eventDate))
  const past = events
    .filter((event) => Date.parse(event.eventDate) < startOfToday(now))
    .sort((a, b) => Date.parse(b.eventDate) - Date.parse(a.eventDate))
  const next = upcoming[0]

  return (
    <div className="flex flex-col gap-6">
      <IdentityCard detail={detail} />

      <CustomerBriefSection organizationId={organizationId} customerId={detail.customerId} />

      {next ? <OccasionBand event={next} /> : null}

      <div className="grid gap-6 lg:grid-cols-3">
        <div className="flex flex-col gap-6 lg:col-span-2">
          <TasteCard preferences={detail.preferences ?? []} />
          <MemoryCard memories={memories} />
          <ActivityCard interactions={interactions} />
          <PastOccasionsCard events={past} />
        </div>
        <div className="flex flex-col gap-6">
          <ContactCard detail={detail} />
          <TagsCard tags={detail.tags} />
          <LedgerCard detail={detail} />
          <ReminderCard upcoming={upcoming} />
        </div>
      </div>
    </div>
  )
}

function IdentityCard({ detail }: { detail: TenantCustomerDetail }) {
  return (
    <Card>
      <CardContent className="flex flex-wrap items-center justify-between gap-4 pt-6">
        <div className="flex items-center gap-4">
          <div
            aria-hidden
            className="flex size-14 shrink-0 items-center justify-center rounded-full bg-primary/10 font-serif text-xl font-medium text-primary"
          >
            {initials(detail.fullName, detail.nickname, detail.phoneNumber)}
          </div>
          <div className="flex flex-col gap-1">
            <span className="text-lg font-medium">
              {detail.fullName ?? detail.nickname ?? 'Unnamed client'}
            </span>
            {detail.fullName && detail.nickname ? (
              <span className="text-xs text-muted-foreground">
                Known at the counter as {detail.nickname}
              </span>
            ) : null}
            <span className="font-mono text-[11px] text-muted-foreground">{detail.customerId}</span>
          </div>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant="secondary" className="capitalize">
            {detail.status}
          </Badge>
          {detail.level ? (
            <Badge variant="outline" className="capitalize">
              {detail.level}
            </Badge>
          ) : (
            <Badge variant="outline">Not graded</Badge>
          )}
          <ConsentBadge status={detail.consentStatus} />
        </div>
      </CardContent>
    </Card>
  )
}

/**
 * Consent, shown and never offered.
 *
 * The customer's own decision is not a staff action, and the surfaces that write it are separate
 * from this record. A revoked client is marked plainly rather than hidden, because an associate
 * about to message them needs to know that nothing will go out.
 */
function ConsentBadge({ status }: { status: string }) {
  if (status === 'granted') {
    return (
      <Badge className="border-transparent bg-success/15 text-success">Consent granted</Badge>
    )
  }
  if (status === 'revoked') {
    return <Badge variant="destructive">Opted out</Badge>
  }
  return <Badge variant="outline">Consent not given</Badge>
}

/**
 * The next occasion, set apart.
 *
 * The only block on the page that expires, so it is allowed to be the loudest: an associate scanning
 * this record needs the deadline, not the ledger.
 */
function OccasionBand({ event }: { event: TenantCustomerEvent }) {
  const days = daysUntil(event.eventDate)
  const when = days === 0 ? 'Today' : days === 1 ? 'Tomorrow' : `In ${days} days`
  return (
    <Card className="border-primary/30 bg-primary/5">
      <CardContent className="flex flex-wrap items-center gap-4 pt-6">
        <CalendarClock className="size-5 text-primary" aria-hidden />
        <div className="flex flex-1 flex-col">
          <span className="text-sm font-medium">
            {EVENT_LABEL[event.eventType] ?? event.eventType} ·{' '}
            {new Date(event.eventDate).toLocaleDateString()}
          </span>
          {event.description ? (
            <span className="text-xs text-muted-foreground">{event.description}</span>
          ) : null}
        </div>
        <Badge variant="secondary">{when}</Badge>
      </CardContent>
    </Card>
  )
}

function LedgerCard({ detail }: { detail: TenantCustomerDetail }) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <h2 className="font-serif text-base font-medium">The relationship</h2>
        <dl className="grid grid-cols-2 gap-4">
          <Stat label="Spent with us" value={formatMoney(detail.totalSpent)} />
          <Stat
            label={detail.visitCount === 1 ? 'Visit' : 'Visits'}
            value={String(detail.visitCount)}
          />
          <Stat
            label="Last visit"
            value={
              detail.lastVisitAtUtc
                ? new Date(detail.lastVisitAtUtc).toLocaleDateString()
                : 'Never'
            }
          />
          <Stat label="Client since" value={new Date(detail.createdAtUtc).toLocaleDateString()} />
        </dl>
        {detail.loyaltyTierIsDerived === 1 ? (
          <p className="text-xs text-muted-foreground">
            The tier is derived from spend, visits and recency, which is why it is not editable.
          </p>
        ) : null}
      </CardContent>
    </Card>
  )
}

function Stat({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs uppercase tracking-wide text-muted-foreground">{label}</dt>
      <dd className="text-sm">{value}</dd>
    </div>
  )
}

function ContactCard({ detail }: { detail: TenantCustomerDetail }) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <h2 className="font-serif text-base font-medium">Reaching them</h2>
        <div className="flex flex-col gap-3">
          <ContactRow icon={<Phone className="size-4 text-primary" aria-hidden />} label="Phone">
            {detail.phoneNumber ? (
              <a className="hover:underline" href={`tel:${detail.phoneNumber}`}>
                {detail.phoneNumber}
              </a>
            ) : (
              '—'
            )}
          </ContactRow>
          <ContactRow icon={<Mail className="size-4 text-primary" aria-hidden />} label="Email">
            {detail.email ? (
              <a className="hover:underline" href={`mailto:${detail.email}`}>
                {detail.email}
              </a>
            ) : (
              '—'
            )}
          </ContactRow>
        </div>
      </CardContent>
    </Card>
  )
}

function ContactRow({
  icon,
  label,
  children,
}: {
  icon: React.ReactNode
  label: string
  children: React.ReactNode
}) {
  return (
    <div className="flex items-start gap-3">
      <span className="mt-0.5">{icon}</span>
      <div className="flex flex-col">
        <span className="text-xs uppercase tracking-wide text-muted-foreground">{label}</span>
        <span className="text-sm">{children}</span>
      </div>
    </div>
  )
}

function TasteCard({ preferences }: { preferences: TenantCustomerPreference[] }) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <SectionHeading title="Their taste" count={preferences.length} />
        {preferences.length === 0 ? (
          <p className="text-sm text-muted-foreground">
            No preferences on file yet. A stated preference reaches here from the conversation it was
            said in.
          </p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2">
            {preferences.map((preference) => (
              <div
                key={preference.id}
                className="flex flex-col gap-1 rounded-lg border border-border p-3"
              >
                <span className="text-xs uppercase tracking-wide text-muted-foreground">
                  {preference.preferenceKey}
                </span>
                <span className="text-sm">{preference.preferenceValue}</span>
                <span className="text-xs text-muted-foreground">
                  {preference.isExplicit ? 'Stated' : 'Inferred'} ·{' '}
                  {Math.round(preference.confidence * 100)}%
                </span>
              </div>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

function TagsCard({ tags }: { tags: string[] }) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <SectionHeading title="Boutique tags" count={tags.length} />
        {tags.length === 0 ? (
          <p className="text-sm text-muted-foreground">No tags on this client yet.</p>
        ) : (
          <div className="flex flex-wrap gap-1.5">
            {tags.map((tag) => (
              <Badge key={tag} variant="outline">
                {tag}
              </Badge>
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/**
 * What Aveline remembers, with the distinction an associate acts on.
 *
 * `Stated` versus `Inferred` is the reason this panel is worth reading: a note the client gave is a
 * fact to act on, and a note the agent inferred is a hypothesis to check. The category is shown too,
 * so a `complaint` cannot be read as a preference.
 */
function MemoryCard({ memories }: { memories: TenantCustomerMemory[] }) {
  return (
    <Card data-testid="customer-memories">
      <CardContent className="flex flex-col gap-4 pt-6">
        <SectionHeading title="What Aveline remembers" count={memories.length} />
        {memories.length === 0 ? (
          <p className="text-sm text-muted-foreground">Nothing remembered about this client yet.</p>
        ) : (
          <ul className="flex flex-col gap-4">
            {memories.map((memory) => (
              <li
                key={memory.id}
                className="flex flex-col gap-1.5 border-l-2 border-primary/30 pl-3"
              >
                <p className="text-sm leading-relaxed">{memory.content}</p>
                <span className="text-xs text-muted-foreground">
                  {memory.category} · {memory.isExplicit ? 'Stated by the client' : 'Inferred'} ·{' '}
                  {memory.source} · {Math.round(memory.confidence * 100)}% ·{' '}
                  {new Date(memory.createdAtUtc).toLocaleDateString()}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function ActivityCard({ interactions }: { interactions: CustomerInteractionItem[] }) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <SectionHeading title="Recent activity" count={interactions.length} />
        {interactions.length === 0 ? (
          <p className="text-sm text-muted-foreground">No exchanges recorded yet.</p>
        ) : (
          // A client of any age has more exchanges than a page should show, and an unbounded list
          // pushed the record's later sections off the screen. The rail is a window onto the
          // history, not the history: it is a fixed height and it scrolls, and the count in the
          // heading still says how much there is.
          //
          // The height is `h-` and not `max-h-` on purpose: Radix's viewport is `size-full`, so a
          // max-height on the root leaves the viewport unresolvable and the list grows again.
          <ScrollArea
            className="h-80"
            data-testid="customer-activity-scroll"
            aria-label="Recent activity"
          >
            <ol className="flex flex-col pr-3">
              {interactions.map((interaction, index) => (
                <li key={interaction.interactionId} className="flex gap-3">
                  <div className="flex flex-col items-center">
                    <span
                      aria-hidden
                      className="mt-1.5 size-2 shrink-0 rounded-full bg-primary/50"
                    />
                    {index < interactions.length - 1 ? (
                      <span aria-hidden className="w-px flex-1 bg-border" />
                    ) : null}
                  </div>
                  <div className="flex flex-1 flex-col gap-0.5 pb-4">
                    <span className="text-xs capitalize text-muted-foreground">
                      {new Date(interaction.occurredAtUtc).toLocaleDateString()} ·{' '}
                      {interaction.channel.replace('_', ' ')} · {interaction.direction}
                      {interaction.countedAsVisit ? ' · counted as a visit' : ''}
                    </span>
                    <span className="text-sm">{interaction.note ?? 'No note recorded.'}</span>
                  </div>
                </li>
              ))}
            </ol>
          </ScrollArea>
        )}
        {interactions.length === ACTIVITY_WINDOW ? (
          <p className="text-xs text-muted-foreground">
            The most recent {ACTIVITY_WINDOW} exchanges are shown. Scroll the rail for the rest.
          </p>
        ) : null}
      </CardContent>
    </Card>
  )
}

/**
 * The occasions that are no longer ahead.
 *
 * Kept rather than dropped: a past wedding is why the client came in, and it is the context for the
 * preferences above it.
 */
function PastOccasionsCard({ events }: { events: TenantCustomerEvent[] }) {
  if (events.length === 0) return null
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <SectionHeading title="Past occasions" count={events.length} />
        <ul className="flex flex-col gap-2">
          {events.map((event) => (
            <li key={event.id} className="flex items-baseline justify-between gap-3">
              <span className="text-sm">
                {EVENT_LABEL[event.eventType] ?? event.eventType}
                {event.description ? (
                  <span className="text-muted-foreground"> — {event.description}</span>
                ) : null}
              </span>
              <span className="text-xs text-muted-foreground">
                {new Date(event.eventDate).toLocaleDateString()}
              </span>
            </li>
          ))}
        </ul>
      </CardContent>
    </Card>
  )
}

/**
 * The occasions still ahead, as a list.
 *
 * The API's list carries `isActive` but no date filter, so "upcoming" is decided here. The band above
 * shows the nearest one; this is the rest, so a second occasion is not invisible until the first has
 * passed.
 */
function ReminderCard({ upcoming }: { upcoming: TenantCustomerEvent[] }) {
  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <SectionHeading title="Coming up" count={upcoming.length} />
        {upcoming.length === 0 ? (
          <p className="text-sm text-muted-foreground">No upcoming occasions on file.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {upcoming.map((event) => (
              <li key={event.id} className="flex items-baseline justify-between gap-3">
                <span className="text-sm capitalize">
                  {EVENT_LABEL[event.eventType] ?? event.eventType}
                </span>
                <span className="text-xs text-muted-foreground">
                  {new Date(event.eventDate).toLocaleDateString()}
                </span>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}

function SectionHeading({ title, count }: { title: string; count: number }) {
  return (
    <div className="flex items-baseline justify-between gap-2">
      <h2 className="font-serif text-base font-medium">{title}</h2>
      <span className="text-xs text-muted-foreground">{count}</span>
    </div>
  )
}

/**
 * The writable subset of the record.
 *
 * `status` is deliberately absent: the server derives it and ignores a client-supplied value, so
 * offering it would be a control that cannot work.
 */
function EditForm({
  organizationId,
  detail,
  onCancel,
  onSaved,
}: {
  organizationId: string
  detail: TenantCustomerDetail
  onCancel: () => void
  onSaved: () => void
}) {
  const [fullName, setFullName] = useState(detail.fullName ?? '')
  const [nickname, setNickname] = useState(detail.nickname ?? '')
  const [phoneNumber, setPhoneNumber] = useState(detail.phoneNumber ?? '')
  const [email, setEmail] = useState(detail.email ?? '')
  const [level, setLevel] = useState(detail.level ?? 'ungraded')
  const [description, setDescription] = useState(detail.description ?? '')
  const [isSaving, setIsSaving] = useState(false)

  const save = async () => {
    setIsSaving(true)
    try {
      await updateCustomer(organizationId, detail.customerId, {
        fullName,
        nickname,
        phoneNumber: phoneNumber.trim().length > 0 ? phoneNumber : undefined,
        email,
        level: level === 'ungraded' ? '' : level,
        // Sent always, so clearing the field clears the description rather than leaving the old one
        // in place; the server treats an empty string as "remove it".
        description,
      })
      toast.success('Client updated', { description: fullName || 'Saved' })
      onSaved()
    } catch (caught) {
      const apiError = toApiError(caught)
      toast.error('Could not save this client', {
        description:
          apiError.code === 'customer-phone-conflict'
            ? 'Another client in this boutique already has that phone number.'
            : apiError.message,
      })
    } finally {
      setIsSaving(false)
    }
  }

  return (
    <Card>
      <CardContent className="flex flex-col gap-4 pt-6">
        <h2 className="font-serif text-base font-medium">Edit client</h2>
        <div className="grid gap-4 sm:grid-cols-2">
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="customer-full-name">Name</Label>
            <Input
              id="customer-full-name"
              value={fullName}
              onChange={(event) => setFullName(event.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="customer-nickname">Nickname</Label>
            <Input
              id="customer-nickname"
              value={nickname}
              onChange={(event) => setNickname(event.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="customer-phone">Phone</Label>
            <Input
              id="customer-phone"
              value={phoneNumber}
              onChange={(event) => setPhoneNumber(event.target.value)}
              placeholder="0771234567"
            />
            <p className="text-xs text-muted-foreground">
              The identity key. Another client already holding this number is refused.
            </p>
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="customer-email">Email</Label>
            <Input
              id="customer-email"
              value={email}
              onChange={(event) => setEmail(event.target.value)}
            />
          </div>
          <div className="flex flex-col gap-1.5">
            <Label htmlFor="customer-level">Level</Label>
            <Select value={level} onValueChange={setLevel}>
              <SelectTrigger id="customer-level" aria-label="Client level">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="ungraded">Not graded</SelectItem>
                {LEVELS.map((option) => (
                  <SelectItem key={option} value={option}>
                    {option}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
            <p className="text-xs text-muted-foreground">
              Status is not editable: the loyalty rule derives it from spend and visits.
            </p>
          </div>
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="customer-description">Description</Label>
          <Textarea
            id="customer-description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            rows={4}
            maxLength={1000}
            placeholder="Who this client is, in the boutique's own words."
          />
          <p className="text-xs text-muted-foreground">
            Shown at the top of the brief an associate reads before making contact. Empty clears it.
          </p>
        </div>
        <div className="flex gap-2">
          <Button type="button" onClick={() => void save()} disabled={isSaving}>
            Save changes
          </Button>
          <Button type="button" variant="outline" onClick={onCancel} disabled={isSaving}>
            Cancel
          </Button>
        </div>
      </CardContent>
    </Card>
  )
}

/** Midnight today, so an occasion happening today is still ahead. */
function startOfToday(now: number): number {
  const date = new Date(now)
  date.setHours(0, 0, 0, 0)
  return date.getTime()
}

function daysUntil(isoDate: string): number {
  const target = new Date(isoDate)
  target.setHours(0, 0, 0, 0)
  return Math.round((target.getTime() - startOfToday(Date.now())) / 86_400_000)
}

/**
 * The two letters in the record's circle.
 *
 * Taken from one name rather than from both: a client with a full name and a nickname has one
 * identity, and mixing the two would print `EE` for `Eleanor Vane (Ellie)`. A client the boutique
 * has no name for shows the last two digits of the number they are reached on, which is what the
 * counter calls them.
 */
function initials(
  fullName: string | null,
  nickname: string | null,
  phoneNumber: string | null,
): string {
  const source = fullName?.trim() || nickname?.trim() || ''
  if (!source) {
    return phoneNumber ? phoneNumber.slice(-2) : '··'
  }
  const words = source.split(/\s+/).filter(Boolean)
  if (words.length === 1) {
    return words[0].slice(0, 2).toUpperCase()
  }
  return `${words[0][0]}${words[words.length - 1][0]}`.toUpperCase()
}
