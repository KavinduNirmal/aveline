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
} from '@/lib/conversations-api'
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
  /** Sends a staff note (optimistic) and triggers the agent. */
  send: (text: string) => Promise<void>
  /** Approves or rejects a SignOff message. */
  decide: (messageId: string, approved: boolean) => Promise<void>
  /**
   * Binds the active Salon to a customer chosen from a resolution `choice` block and
   * re-triggers the agent with that customer in context.
   */
  selectCustomer: (customerId: string) => Promise<void>
}

const ConversationsContext = createContext<ConversationsContextValue | undefined>(undefined)

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
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [agentState, setAgentState] = useState<AvelineState>('idle')
  const [agentActivity, setAgentActivity] = useState<AgentActivity | null>(null)
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  )
  const cleanupRef = useRef<(() => void) | null>(null)
  const connectionRef = useRef<HubConnection | null>(null)
  const activeRef = useRef<string | null>(null)
  activeRef.current = activeConversationId
  const messagesRef = useRef<ChatMessage[]>([])
  messagesRef.current = messages
  const agentActivityRef = useRef<AgentActivity | null>(null)
  agentActivityRef.current = agentActivity
  const transientTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null)

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

  const waiting = ACTIVE_STATES.has(agentState)

  const openConversation = useCallback(
    async (conversationId: string) => {
      setActiveConversationId(conversationId)
      setMessages([])
      setAgentActivity(null)
      try {
        const page = await fetchMessages(organizationId, conversationId, { pageSize: 100 })
        // History is served oldest-first, so page 1 is the OLDEST 100 messages. Once a Salon
        // grows past a single page, a reload would otherwise drop the newest messages. Fetch
        // the last (newest) page instead so the recent thread survives a refresh.
        let items = page.items
        if (page.total > items.length && page.pageSize > 0) {
          const lastPage = Math.max(1, Math.ceil(page.total / page.pageSize))
          const newest = await fetchMessages(organizationId, conversationId, {
            page: lastPage,
            pageSize: page.pageSize,
          })
          items = newest.items
        }
        setMessages(items)
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
        // Only surface messages for the currently open Salon.
        if (payload.conversationId !== activeRef.current) return

        setMessages((prev) => {
          if (prev.some((m) => m.id === payload.id)) return prev
          // When an agent reply lands, collapse the live activity into a "Thought for Xs"
          // caption on the incoming message and stream the text in word by word.
          const isAgent = payload.authorKind === 'Agent'
          const activity = agentActivityRef.current
          const thoughtSeconds =
            isAgent && activity ? Math.max(0, (Date.now() - activity.startedAt) / 1000) : undefined
          return [...prev, { ...payload, thoughtSeconds, streamIn: isAgent }]
        })

        if (payload.authorKind === 'Agent') {
          setAgentActivity(null)
        }
      },
      onAgentState: (payload: AgentStatePayload) => {
        // Only reflect states for the currently open Salon.
        if (payload.conversationId !== activeRef.current) return
        const state = payload.state as AvelineState
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
    }
  }, [applyAgentState, getToken, handleStateChange, isLoaded, isSignedIn, organizationId])

  // Re-join the Salon group whenever the active conversation changes so the client keeps
  // receiving live messages/states for the conversation currently on screen.
  useEffect(() => {
    const connection = connectionRef.current
    if (!connection || connection.state !== HubConnectionState.Connected) return
    if (!activeConversationId) return
    void connection.invoke('JoinSalon', organizationId, activeConversationId)
  }, [activeConversationId, connectionState, organizationId])

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

  const send = useCallback(
    async (text: string) => {
      if (!activeConversationId || !text.trim()) return
      const trimmed = text.trim()
      const optimistic: ChatMessage = {
        id: `local-${Date.now()}`,
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
      // Optimistic: show the staff message immediately.
      setMessages((prev) => [...prev, optimistic])

      try {
        const message = await sendMessage(organizationId, activeConversationId, trimmed)
        // Replace the optimistic bubble with the confirmed message (real id + timestamp).
        setMessages((prev) =>
          prev.map((m) => (m.id === optimistic.id ? { ...message } : m)),
        )
        // Only once the user message is confirmed does Aveline's activity bubble appear.
        setAgentActivity({ startedAt: Date.now(), currentState: 'thinking' })
        applyAgentState('thinking')
      } catch {
        setMessages((prev) =>
          prev.map((m) => (m.id === optimistic.id ? { ...m, pending: 'failed' } : m)),
        )
      } finally {
        setSending(false)
      }
    },
    [activeConversationId, applyAgentState, organizationId],
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
        decide,
        selectCustomer,
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
