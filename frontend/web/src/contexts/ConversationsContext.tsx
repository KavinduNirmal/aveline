import { useAuth } from '@clerk/react'
import { HubConnection, HubConnectionState } from '@microsoft/signalr'
import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'

import {
  createConversationsConnection,
  startConversations,
  type AgentStatePayload,
} from '@/lib/conversations'
import {
  decideSignOff,
  fetchConversations,
  fetchMessages,
  getOrCreateConversation,
  selectConversationCustomer,
  sendMessage,
  uploadConversationAttachment,
} from '@/lib/conversations-api'
import {
  checkAttachmentCap,
  isAnalysableContentType,
  prepareAttachment,
  type AttachmentRefusal,
  type PreparedAttachment,
} from '@/lib/attachment-preparation'
import type { ConversationDto, MessageDto } from '@/types/conversation'
import type { AvelineState } from '@/components/conversation/avelineStates'
import { isTerminalState } from '@/components/conversation/avelineStates'

/** Clerk JWT template that mints the Aveline role claims (matches AuthApiBridge). */
const JWT_TEMPLATE = 'jwt-aveline-v1'

/** States that count as "Aveline is actively working on a reply". */
const ACTIVE_STATES: ReadonlySet<string> = new Set([
  'thinking',
  'searching',
  'processing',
  'tool_call',
])

/** How long to hold a transient state (success/error/response) before settling to idle. */
const TRANSIENT_TIMEOUT_MS = 2500

/**
 * A message as rendered in the thread. Extends the wire `MessageDto` with UI-only fields:
 * `pending` marks an optimistic (not-yet-confirmed) staff message; `thoughtSeconds` records
 * how long Aveline "thought" before producing an agent message.
 */
export type ChatMessage = MessageDto & {
  pending?: 'sending' | 'failed'
  thoughtSeconds?: number
  /** True for agent messages that arrived live and should type out word by word. */
  streamIn?: boolean
}

/** The first `text` block of a message, or undefined when it has none. */
function lastStaffPrimaryText(message: MessageDto): string | undefined {
  for (const block of message.contentBlocks ?? []) {
    if (
      block &&
      typeof block === 'object' &&
      (block as { type?: string }).type === 'text' &&
      typeof (block as { text?: unknown }).text === 'string'
    ) {
      return (block as { text: string }).text
    }
  }
  return undefined
}

/** Aveline's in-progress reasoning, shown as a live activity bubble until a reply lands. */
export interface AgentActivity {
  /** Epoch ms when the activity began (used to compute "Thought for Xs"). */
  startedAt: number
  /** The most recent agentic-workflow state. */
  currentState: AvelineState
}

/**
 * A file held in the composer's pending tray. Picked files upload immediately (upload-on-pick) and
 * the tray is keyed by conversation id so switching dashboard sections does not lose it, mirroring
 * the mobile client. The prepared bytes are retained after a failure so a retry re-uploads the
 * exact payload rather than re-encoding the original file.
 */
export interface PendingAttachment {
  /** Stable client-side id for the chip (also its list key). */
  id: string
  fileName: string
  /** The bytes an upload sends; retained so a retry reuses them. */
  payload: Blob
  status: 'uploading' | 'ready' | 'failed'
  /** The server's id once the upload is stored; null until then. */
  attachmentId: string | null
  /** The stored content type the upload response reported; null until stored. */
  storedContentType: string | null
  /**
   * Whether the visual assistant can read the stored bytes. Taken from the upload **response's**
   * content type, never the declared one, and never a reason to block the upload.
   */
  analysable: boolean
  sizeBytes: number
  error?: string
  /**
   * Whether retrying this upload could plausibly succeed. False for a denial the server will not
   * reconsider (403/404), true for a network, timeout or 5xx failure. The tray reads this rather
   * than matching the error wording, so the two cannot drift apart.
   */
  retryable: boolean
}

/** Mints the idempotency key for a composed message; every retry of that message reuses it. */
function mintClientMessageId(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID()
  }
  return `cmsg-${Date.now()}-${Math.random().toString(36).slice(2)}`
}

/** The HTTP status an axios-style upload failure carries, when it carries one. */
function uploadFailureStatus(error: unknown): number | undefined {
  return (error as { response?: { status?: number } } | null)?.response?.status
}

/**
 * Whether the failure is a denial the server will not reconsider, so a retry would be pointless.
 * The value travels on the chip so the tray never has to match the message text.
 */
function isPermanentUploadFailure(error: unknown): boolean {
  const status = uploadFailureStatus(error)
  return status === 403 || status === 404
}

/** A message for a failed upload, distinguishing a genuine denial from a transient failure. */
function describeUploadFailure(error: unknown): string {
  const status = uploadFailureStatus(error)
  if (status === 403) return "You don't have access to this thread."
  if (status === 404) return 'This thread is no longer available.'
  return 'That file could not be uploaded. Check your connection and try again.'
}

interface ConversationsContextValue {
  /** The organization's Salons, newest first. */
  conversations: ConversationDto[]
  /** The currently open Salon id, or null when none is selected. */
  activeConversationId: string | null
  /** Messages of the active Salon, oldest first. */
  messages: ChatMessage[]
  loading: boolean
  sending: boolean
  /** Aveline's current agentic-workflow state for the active Salon. */
  agentState: AvelineState
  /** True while Aveline is actively processing a reply (derived from `agentState`). */
  waiting: boolean
  /** Aveline's in-progress reasoning, or null when idle / awaiting a reply. */
  agentActivity: AgentActivity | null
  connectionState: HubConnectionState
  /** Selects a Salon and loads its messages. */
  openConversation: (conversationId: string) => Promise<void>
  /** Gets-or-creates the Salon for an optional customer and opens it. */
  openOrCreateSalon: (customerId?: string | null) => Promise<ConversationDto>
  /**
   * Sends a staff note (optimistic) and triggers the agent, binding the already-uploaded
   * `attachmentIds`. The `clientMessageId` is minted once per composed message here and reused
   * across retries, which is what makes a retried send idempotent.
   */
  send: (text: string, attachmentIds?: string[]) => Promise<void>
  /**
   * The pending attachment tray, keyed by conversation id. Empty for a conversation with none.
   */
  pendingAttachments: Record<string, PendingAttachment[]>
  /**
   * Picks files for a conversation: uploads each immediately and returns the refusals (over-cap,
   * unsupported type, or a pick that would cross the five-per-message cap) without creating a chip.
   */
  attach: (conversationId: string, files: File[]) => Promise<AttachmentRefusal[]>
  /** Retries a failed chip with the exact bytes it already holds. */
  retryAttachment: (conversationId: string, attachmentId: string) => Promise<void>
  /** Drops a chip. An already-stored upload is left to the 24 h sweep; the composed text is untouched. */
  removeAttachment: (conversationId: string, attachmentId: string) => void
  /** True when every held file is stored, so a send may bind them. */
  attachmentsReady: (conversationId: string) => boolean
  /** Approves or rejects a SignOff message. */
  decide: (messageId: string, approved: boolean) => Promise<void>
  /**
   * Binds the active Salon to a customer chosen from a resolution `choice` block and
   * re-triggers the agent with that customer in context.
   */
  selectCustomer: (customerId: string) => Promise<void>

  /**
   * The Aveline drawer's **own** thread. It shares the conversation list and the hub connection
   * with the Salon section, but never the open thread: one shared `activeConversationId` meant
   * opening a client's Salon in either surface moved the other one with it.
   */
  avelineConversationId: string | null
  avelineMessages: ChatMessage[]
  avelineAgentState: AvelineState
  avelineAgentActivity: AgentActivity | null
  avelineLoading: boolean
  avelineSending: boolean
  /** Opens the general, client-less Salon in the drawer's thread only. */
  openAveline: () => Promise<void>
  sendToAveline: (text: string) => Promise<void>
  decideAveline: (messageId: string, approved: boolean) => Promise<void>
}

const ConversationsContext = createContext<ConversationsContextValue | undefined>(undefined)

/**
 * Fetches the **newest** page of a thread.
 *
 * History is served oldest-first, so page 1 is the OLDEST page: once a Salon grows past one page, a
 * reload would drop the recent messages. Shared by the Salon section and the Aveline drawer so both
 * threads agree on what "the newest messages" means.
 */
/** The paging query `fetchMessages` accepts, without the ids it is already bound to. */
type MessagePageQuery = Omit<Parameters<typeof fetchMessages>[2] & object, never>

async function loadNewestPage(
  fetchPage: (query: MessagePageQuery) => Promise<{
    items: ChatMessage[]
    total: number
    pageSize: number
  }>,
): Promise<ChatMessage[]> {
  const page = await fetchPage({ pageSize: 100 })
  if (page.total <= page.items.length || page.pageSize <= 0) {
    return page.items
  }
  const lastPage = Math.max(1, Math.ceil(page.total / page.pageSize))
  const newest = await fetchPage({ page: lastPage, pageSize: page.pageSize })
  return newest.items
}

interface ConversationsProviderProps {
  organizationId: string
  children: ReactNode
}

/**
 * Shared conversation ("The Salon") state for a boutique. Establishes the SignalR
 * connection, loads the org's Salons, and exposes the active thread + send/decide
 * actions. Both the full Salon tab and the floating Aveline chat drawer consume this
 * single context so they always show the same thread.
 */
export function ConversationsProvider({
  organizationId,
  children,
}: ConversationsProviderProps) {
  const { isLoaded, isSignedIn, getToken } = useAuth()
  const [conversations, setConversations] = useState<ConversationDto[]>([])
  const [activeConversationId, setActiveConversationId] = useState<string | null>(null)
  const [messages, setMessages] = useState<ChatMessage[]>([])
  // The Aveline drawer keeps its **own** thread. It shares one conversation list and one hub
  // connection with the Salon section, but not the open thread: sharing that made opening a
  // client's Salon in one surface drag the other surface with it.
  const [avelineConversationId, setAvelineConversationId] = useState<string | null>(null)
  const [avelineMessages, setAvelineMessages] = useState<ChatMessage[]>([])
  const [avelineAgentState, setAvelineAgentState] = useState<AvelineState>('idle')
  const [avelineAgentActivity, setAvelineAgentActivity] = useState<AgentActivity | null>(null)
  const [avelineLoading, setAvelineLoading] = useState(false)
  const [avelineSending, setAvelineSending] = useState(false)
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [pendingAttachments, setPendingAttachments] = useState<
    Record<string, PendingAttachment[]>
  >({})
  const [agentState, setAgentState] = useState<AvelineState>('idle')
  const [agentActivity, setAgentActivity] = useState<AgentActivity | null>(null)
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  )
  const cleanupRef = useRef<(() => void) | null>(null)
  const connectionRef = useRef<HubConnection | null>(null)
  const activeRef = useRef<string | null>(null)
  activeRef.current = activeConversationId
  const avelineIdRef = useRef<string | null>(null)
  avelineIdRef.current = avelineConversationId
  const messagesRef = useRef<ChatMessage[]>([])
  messagesRef.current = messages
  const avelineMessagesRef = useRef<ChatMessage[]>([])
  avelineMessagesRef.current = avelineMessages
  /** The key and optimistic row of the message currently being composed, for retry reuse. */
  const composedRef = useRef<{ key: string; signature: string; optimisticId: string } | null>(
    null,
  )
  const agentActivityRef = useRef<AgentActivity | null>(null)
  agentActivityRef.current = agentActivity
  const transientTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)
  const avelineTransientTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  const handleStateChange = useCallback((state: HubConnectionState) => {
    setConnectionState(state)
  }, [])

  // Applies an incoming agent state, scheduling a settle-to-idle for transient states.
  const applyAgentState = useCallback((state: AvelineState) => {
    setAgentState(state)
    if (transientTimerRef.current) {
      clearTimeout(transientTimerRef.current)
      transientTimerRef.current = null
    }
    if (state === 'success' || state === 'error' || state === 'response') {
      transientTimerRef.current = setTimeout(() => setAgentState('idle'), TRANSIENT_TIMEOUT_MS)
    }
  }, [])

  // The drawer's settle-to-idle, independent of the panel's so a transient state in one thread
  // cannot clear the other's indicator.
  const applyAvelineAgentState = useCallback((state: AvelineState) => {
    setAvelineAgentState(state)
    if (avelineTransientTimerRef.current) {
      clearTimeout(avelineTransientTimerRef.current)
      avelineTransientTimerRef.current = null
    }
    if (state === 'success' || state === 'error' || state === 'response') {
      avelineTransientTimerRef.current = setTimeout(
        () => setAvelineAgentState('idle'),
        TRANSIENT_TIMEOUT_MS,
      )
    }
  }, [])

  const waiting = ACTIVE_STATES.has(agentState)

  const openConversation = useCallback(
    async (conversationId: string) => {
      setActiveConversationId(conversationId)
      setMessages([])
      setAgentActivity(null)
      try {
        setMessages(await loadNewestPage((query) => fetchMessages(organizationId, conversationId, query)))
      } catch {
        setMessages([])
      }
    },
    [organizationId],
  )

  // Load the org's Salons once and auto-open the single Aveline salon (customerId = null).
  useEffect(() => {
    if (!organizationId) return
    let mounted = true
    setLoading(true)
    ;(async () => {
      try {
        const page = await fetchConversations(organizationId)
        if (!mounted) return
        setConversations(page.items)

        // The user always has one Aveline salon to chat with directly. Get-or-create it
        // (idempotent per org) and open it by default.
        const avelineSalon =
          page.items.find((c) => c.customerId === null) ??
          (await getOrCreateConversation(organizationId, null))
        if (!mounted) return
        setConversations((prev) =>
          prev.some((c) => c.id === avelineSalon.id) ? prev : [avelineSalon, ...prev],
        )
        await openConversation(avelineSalon.id)
      } catch {
        /* best-effort; the UI shows an empty state */
      } finally {
        if (mounted) setLoading(false)
      }
    })()
    return () => {
      mounted = false
    }
  }, [openConversation, organizationId])

  // Establish the SignalR connection while signed in.
  useEffect(() => {
    if (!isLoaded || !isSignedIn || !organizationId) return

    const connection = createConversationsConnection(() =>
      getToken({ template: JWT_TEMPLATE }),
    )
    connectionRef.current = connection
    cleanupRef.current = startConversations(connection, {
      onMessage: (payload) => {
        // A payload belongs to whichever slot is displaying that conversation. Both slots subscribe
        // to their own group, so the connection can carry two threads at once.
        const forPanel = payload.conversationId === activeRef.current
        const forAveline = payload.conversationId === avelineIdRef.current
        if (!forPanel && !forAveline) return

        const isAgent = payload.authorKind === 'Agent'
        const append = (prev: ChatMessage[]) => {
          if (prev.some((m) => m.id === payload.id)) return prev
          // When an agent reply lands, collapse the live activity into a "Thought for Xs"
          // caption on the incoming message and stream the text in word by word.
          const activity = agentActivityRef.current
          const thoughtSeconds =
            isAgent && activity ? Math.max(0, (Date.now() - activity.startedAt) / 1000) : undefined
          return [...prev, { ...payload, thoughtSeconds, streamIn: isAgent }]
        }

        if (forPanel) {
          setMessages(append)
          if (isAgent) setAgentActivity(null)
        }
        if (forAveline) {
          setAvelineMessages(append)
          if (isAgent) setAvelineAgentActivity(null)
        }
      },
      onAgentState: (payload: AgentStatePayload) => {
        const forPanel = payload.conversationId === activeRef.current
        const forAveline = payload.conversationId === avelineIdRef.current
        if (!forPanel && !forAveline) return
        const state = payload.state as AvelineState
        if (forPanel) {
          applyAgentState(state)
          // Terminal states (success/error/response) mean the workflow finished: collapse the
          // live activity bubble so it can't hang as a stale 'Done' card. In-progress states
          // (thinking/searching/…) keep the "Aveline is working" bubble.
          if (isTerminalState(state)) {
            setAgentActivity(null)
          } else {
            setAgentActivity((prev) =>
              prev
                ? { ...prev, currentState: state }
                : { startedAt: Date.now(), currentState: state },
            )
          }
        }
        if (forAveline) {
          applyAvelineAgentState(state)
          if (isTerminalState(state)) {
            setAvelineAgentActivity(null)
          } else {
            setAvelineAgentActivity((prev) =>
              prev
                ? { ...prev, currentState: state }
                : { startedAt: Date.now(), currentState: state },
            )
          }
        }
      },
      onStateChange: handleStateChange,
      onConnected: (conn) => {
        // Join the Salon group so this client receives ReceiveMessage / ReceiveAgentState.
        // Note: `invoke` is variadic (methodName, ...args); pass args separately, not as an array.
        const conversationId = activeRef.current
        if (conversationId) {
          void conn.invoke('JoinSalon', organizationId, conversationId)
        }
      },
    })

    return () => {
      cleanupRef.current?.()
      cleanupRef.current = null
      connectionRef.current = null
      if (transientTimerRef.current) {
        clearTimeout(transientTimerRef.current)
        transientTimerRef.current = null
      }
      if (avelineTransientTimerRef.current) {
        clearTimeout(avelineTransientTimerRef.current)
        avelineTransientTimerRef.current = null
      }
    }
  }, [
    applyAgentState,
    applyAvelineAgentState,
    getToken,
    handleStateChange,
    isLoaded,
    isSignedIn,
    organizationId,
  ])

  // Join the groups for both open threads. `JoinSalon` adds to a group without leaving others, so
  // one connection can carry the Salon section's thread and the Aveline drawer's thread at once.
  useEffect(() => {
    const connection = connectionRef.current
    if (!connection || connection.state !== HubConnectionState.Connected) return
    if (activeConversationId) {
      void connection.invoke('JoinSalon', organizationId, activeConversationId)
    }
    if (avelineConversationId && avelineConversationId !== activeConversationId) {
      void connection.invoke('JoinSalon', organizationId, avelineConversationId)
    }
  }, [activeConversationId, avelineConversationId, connectionState, organizationId])

  const openOrCreateSalon = useCallback(
    async (customerId?: string | null) => {
      const conversation = await getOrCreateConversation(organizationId, customerId)
      setConversations((prev) =>
        prev.some((c) => c.id === conversation.id)
          ? prev
          : [conversation, ...prev],
      )
      await openConversation(conversation.id)
      return conversation
    },
    [openConversation, organizationId],
  )

  /**
   * Opens the general, client-less Salon in the **drawer's** slot. Deliberately does not touch the
   * panel's thread: `openOrCreateSalon` is the Salon section's, and routing the drawer through it is
   * what coupled the two surfaces.
   */
  const openAveline = useCallback(async () => {
    const existing = conversations.find(
      (c) => c.customerId === null && c.externalRef === null,
    )
    const salon = existing ?? (await getOrCreateConversation(organizationId, null))
    setConversations((prev) => (prev.some((c) => c.id === salon.id) ? prev : [salon, ...prev]))
    setAvelineConversationId(salon.id)
    setAvelineMessages([])
    setAvelineAgentActivity(null)
    setAvelineLoading(true)
    try {
      setAvelineMessages(
        await loadNewestPage((options) => fetchMessages(organizationId, salon.id, options)),
      )
    } catch {
      setAvelineMessages([])
    } finally {
      setAvelineLoading(false)
    }
  }, [conversations, organizationId])

  const sendToAveline = useCallback(
    async (text: string) => {
      const conversationId = avelineIdRef.current
      if (!conversationId || !text.trim()) return
      const trimmed = text.trim()
      const optimistic: ChatMessage = {
        id: `local-${Date.now()}`,
        conversationId,
        authorKind: 'User',
        agentKey: null,
        authorUserId: null,
        kind: 'Note',
        contentBlocks: [{ type: 'text', text: trimmed }],
        contentHash: null,
        replyToMessageId: null,
        status: 'Published',
        createdAt: new Date().toISOString(),
        pending: 'sending',
      }

      setAvelineSending(true)
      setAvelineMessages((prev) => [...prev, optimistic])
      try {
        const message = await sendMessage(organizationId, conversationId, trimmed)
        setAvelineMessages((prev) =>
          prev.map((m) => (m.id === optimistic.id ? { ...message } : m)),
        )
        setAvelineAgentActivity({ startedAt: Date.now(), currentState: 'thinking' })
        applyAvelineAgentState('thinking')
      } catch {
        setAvelineMessages((prev) =>
          prev.map((m) => (m.id === optimistic.id ? { ...m, pending: 'failed' } : m)),
        )
      } finally {
        setAvelineSending(false)
      }
    },
    [applyAvelineAgentState, organizationId],
  )

  const decideAveline = useCallback(
    async (messageId: string, approved: boolean) => {
      const conversationId = avelineIdRef.current
      if (!conversationId) return
      const message = avelineMessagesRef.current.find((m) => m.id === messageId)
      if (!message) return
      const updated = await decideSignOff(
        organizationId,
        conversationId,
        messageId,
        approved,
        message.contentHash ?? '',
      )
      setAvelineMessages((prev) => prev.map((m) => (m.id === updated.id ? updated : m)))
    },
    [organizationId],
  )

  /** Merges a functional update into one conversation's tray. */
  const updatePending = useCallback(
    (conversationId: string, update: (held: PendingAttachment[]) => PendingAttachment[]) => {
      setPendingAttachments((prev) => ({
        ...prev,
        [conversationId]: update(prev[conversationId] ?? []),
      }))
    },
    [],
  )

  /** Uploads one chip's retained bytes and records the stored type the response reported. */
  const runUpload = useCallback(
    async (conversationId: string, chip: PendingAttachment) => {
      try {
        const stored = await uploadConversationAttachment(
          organizationId,
          conversationId,
          chip.payload,
          chip.fileName,
        )
        updatePending(conversationId, (held) =>
          held.map((a) =>
            a.id === chip.id
              ? {
                  ...a,
                  status: 'ready',
                  attachmentId: stored.attachmentId,
                  storedContentType: stored.contentType,
                  analysable: isAnalysableContentType(stored.contentType),
                  sizeBytes: stored.sizeBytes,
                  error: undefined,
                }
              : a,
          ),
        )
      } catch (error) {
        updatePending(conversationId, (held) =>
          held.map((a) =>
            a.id === chip.id
              ? {
                  ...a,
                  status: 'failed',
                  error: describeUploadFailure(error),
                  // A denial the server will not reconsider is not worth retrying; everything
                  // else (network, timeout, 5xx) is.
                  retryable: !isPermanentUploadFailure(error),
                }
              : a,
          ),
        )
      }
    },
    [organizationId, updatePending],
  )

  const attach = useCallback(
    async (conversationId: string, files: File[]): Promise<AttachmentRefusal[]> => {
      const refusals: AttachmentRefusal[] = []
      const heldCount = pendingAttachments[conversationId]?.length ?? 0
      const prepared: PreparedAttachment[] = []

      // Refusals are decided before any chip exists and before any byte leaves the browser: a
      // sixth file, an unsupported type, and an over-cap payload all fail here.
      for (const file of files) {
        const cap = checkAttachmentCap(heldCount + prepared.length)
        if (cap) {
          refusals.push({ ...cap, fileName: file.name })
          continue
        }
        const result = await prepareAttachment(file)
        if (result.status === 'refused') {
          refusals.push(result)
          continue
        }
        prepared.push(result)
      }

      if (prepared.length === 0) return refusals

      const chips: PendingAttachment[] = prepared.map((item) => ({
        id: mintClientMessageId(),
        fileName: item.fileName,
        payload: item.file,
        status: 'uploading',
        attachmentId: null,
        storedContentType: null,
        analysable: false,
        sizeBytes: item.byteLength,
        // A fresh pick can always be retried: nothing has been denied yet.
        retryable: true,
      }))
      updatePending(conversationId, (held) => [...held, ...chips])
      await Promise.all(chips.map((chip) => runUpload(conversationId, chip)))
      return refusals
    },
    [pendingAttachments, runUpload, updatePending],
  )

  const retryAttachment = useCallback(
    async (conversationId: string, attachmentId: string) => {
      const chip = (pendingAttachments[conversationId] ?? []).find((a) => a.id === attachmentId)
      if (!chip) return
      updatePending(conversationId, (held) =>
        held.map((a) =>
          a.id === attachmentId ? { ...a, status: 'uploading', error: undefined } : a,
        ),
      )
      // The same `chip.payload` reference: a retry re-uploads the bytes already prepared.
      await runUpload(conversationId, { ...chip, status: 'uploading', error: undefined })
    },
    [pendingAttachments, runUpload, updatePending],
  )

  const removeAttachment = useCallback(
    (conversationId: string, attachmentId: string) => {
      updatePending(conversationId, (held) => held.filter((a) => a.id !== attachmentId))
    },
    [updatePending],
  )

  const attachmentsReady = useCallback(
    (conversationId: string) => {
      const held = pendingAttachments[conversationId] ?? []
      return held.every((a) => a.status === 'ready' && a.attachmentId !== null)
    },
    [pendingAttachments],
  )

  const send = useCallback(
    async (text: string, attachmentIds?: string[]) => {
      if (!activeConversationId || !text.trim()) return
      const trimmed = text.trim()
      const ids = attachmentIds ?? []
      // The same composed message (same text and same held files) keeps its idempotency key and
      // its optimistic row across retries, so a retried send cannot write a second note.
      const signature = `${trimmed}\u0000${ids.join('\u0000')}`
      const prior = composedRef.current
      const isRetry = prior !== null && prior.signature === signature
      const clientMessageId = isRetry ? prior.key : mintClientMessageId()
      const optimisticId = isRetry ? prior.optimisticId : `local-${Date.now()}`
      composedRef.current = { key: clientMessageId, signature, optimisticId }

      const optimistic: ChatMessage = {
        id: optimisticId,
        conversationId: activeConversationId,
        authorKind: 'User',
        agentKey: null,
        authorUserId: null,
        kind: 'Note',
        contentBlocks: [{ type: 'text', text: trimmed }],
        contentHash: null,
        replyToMessageId: null,
        status: 'Published',
        createdAt: new Date().toISOString(),
        pending: 'sending',
      }

      setSending(true)
      // Optimistic: show the staff message immediately (replacing its failed retry, if any).
      setMessages((prev) =>
        prev.some((m) => m.id === optimisticId)
          ? prev.map((m) => (m.id === optimisticId ? optimistic : m))
          : [...prev, optimistic],
      )

      try {
        const message = await sendMessage(
          organizationId,
          activeConversationId,
          trimmed,
          ids,
          clientMessageId,
        )
        // Replace the optimistic bubble with the confirmed message (real id + timestamp).
        setMessages((prev) =>
          prev.map((m) => (m.id === optimisticId ? { ...message } : m)),
        )
        // The composed message is delivered: the next message mints a fresh key, and the tray
        // clears so a chip can never linger pending after its row is bound.
        composedRef.current = null
        updatePending(activeConversationId, () => [])
        // Only once the user message is confirmed does Aveline's activity bubble appear.
        setAgentActivity({ startedAt: Date.now(), currentState: 'thinking' })
        applyAgentState('thinking')
      } catch (error) {
        setMessages((prev) =>
          prev.map((m) => (m.id === optimisticId ? { ...m, pending: 'failed' } : m)),
        )
        // Rethrow so the caller can tell a failed send from a confirmed one. The optimistic row
        // above is the visual state; this rejection is the control-flow signal the Composer needs
        // to keep the textarea and the tray, because only a confirmed send may clear them.
        throw error
      } finally {
        setSending(false)
      }
    },
    [activeConversationId, applyAgentState, organizationId, updatePending],
  )

  const decide = useCallback(
    async (messageId: string, approved: boolean) => {
      if (!activeConversationId) return
      const message = messagesRef.current.find((m) => m.id === messageId)
      if (!message) return
      // Bind the decision to the exact payload the human saw.
      const contentHash = message.contentHash ?? ''
      const updated = await decideSignOff(
        organizationId,
        activeConversationId,
        messageId,
        approved,
        contentHash,
      )
      setMessages((prev) => prev.map((m) => (m.id === updated.id ? updated : m)))
    },
    [activeConversationId, organizationId],
  )

  const selectCustomer = useCallback(
    async (customerId: string) => {
      if (!activeConversationId) return
      // Re-run the last staff question against the resolved customer (falls back to a
      // server-side summary prompt when there is no prior staff text).
      const lastStaff = [...messagesRef.current]
        .reverse()
        .find((m) => m.authorKind === 'User' && m.pending !== 'failed')
      const query = lastStaff ? lastStaffPrimaryText(lastStaff) : undefined
      const conversation = await selectConversationCustomer(
        organizationId,
        activeConversationId,
        customerId,
        query,
      )
      setConversations((prev) =>
        prev.map((c) => (c.id === conversation.id ? conversation : c)),
      )
      setAgentActivity({ startedAt: Date.now(), currentState: 'thinking' })
      applyAgentState('thinking')
    },
    [activeConversationId, applyAgentState, organizationId],
  )

  return (
    <ConversationsContext.Provider
      value={{
        conversations,
        activeConversationId,
        messages,
        loading,
        sending,
        agentState,
        waiting,
        agentActivity,
        connectionState,
        openConversation,
        openOrCreateSalon,
        send,
        pendingAttachments,
        attach,
        retryAttachment,
        removeAttachment,
        attachmentsReady,
        decide,
        selectCustomer,
        avelineConversationId,
        avelineMessages,
        avelineAgentState,
        avelineAgentActivity,
        avelineLoading,
        avelineSending,
        openAveline,
        sendToAveline,
        decideAveline,
      }}
    >
      {children}
    </ConversationsContext.Provider>
  )
}

export function useConversations(): ConversationsContextValue {
  const context = useContext(ConversationsContext)
  if (!context) {
    throw new Error('useConversations must be used within a ConversationsProvider')
  }
  return context
}
