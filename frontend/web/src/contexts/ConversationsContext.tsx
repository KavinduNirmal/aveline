import { useAuth } from '@clerk/react'
import { HubConnectionState } from '@microsoft/signalr'
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
} from '@/lib/conversations'
import {
  decideSignOff,
  fetchConversations,
  fetchMessages,
  getOrCreateConversation,
  sendMessage,
} from '@/lib/conversations-api'
import type { ConversationDto, MessageDto } from '@/types/conversation'

/** Clerk JWT template that mints the Aveline role claims (matches AuthApiBridge). */
const JWT_TEMPLATE = 'jwt-aveline-v1'

interface ConversationsContextValue {
  /** The organization's Salons, newest first. */
  conversations: ConversationDto[]
  /** The currently open Salon id, or null when none is selected. */
  activeConversationId: string | null
  /** Messages of the active Salon, oldest first. */
  messages: MessageDto[]
  loading: boolean
  sending: boolean
  /** True while Aveline is processing a reply (after a send until an agent message arrives). */
  waiting: boolean
  connectionState: HubConnectionState
  /** Selects a Salon and loads its messages. */
  openConversation: (conversationId: string) => Promise<void>
  /** Gets-or-creates the Salon for an optional customer and opens it. */
  openOrCreateSalon: (customerId?: string | null) => Promise<ConversationDto>
  /** Sends a staff note and triggers the agent. */
  send: (text: string) => Promise<void>
  /** Approves or rejects a SignOff message. */
  decide: (messageId: string, approved: boolean) => Promise<void>
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
  const [messages, setMessages] = useState<MessageDto[]>([])
  const [loading, setLoading] = useState(true)
  const [sending, setSending] = useState(false)
  const [waiting, setWaiting] = useState(false)
  const [connectionState, setConnectionState] = useState<HubConnectionState>(
    HubConnectionState.Disconnected,
  )
  const cleanupRef = useRef<(() => void) | null>(null)
  const activeRef = useRef<string | null>(null)
  activeRef.current = activeConversationId

  const handleStateChange = useCallback((state: HubConnectionState) => {
    setConnectionState(state)
  }, [])

  const openConversation = useCallback(
    async (conversationId: string) => {
      setActiveConversationId(conversationId)
      setMessages([])
      try {
        const page = await fetchMessages(organizationId, conversationId, { pageSize: 100 })
        setMessages(page.items)
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
    cleanupRef.current = startConversations(connection, {
      onMessage: (payload) => {
        // Only surface messages for the currently open Salon.
        if (payload.conversationId === activeRef.current) {
          setMessages((prev) => {
            if (prev.some((m) => m.id === payload.id)) return prev
            return [...prev, payload]
          })
          // An agent/system reply means Aveline is no longer waiting.
          if (payload.authorKind !== 'User') {
            setWaiting(false)
          }
        }
      },
      onStateChange: handleStateChange,
    })

    return () => {
      cleanupRef.current?.()
      cleanupRef.current = null
    }
  }, [getToken, handleStateChange, isLoaded, isSignedIn, organizationId])

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
      setSending(true)
      try {
        const message = await sendMessage(organizationId, activeConversationId, text.trim())
        setMessages((prev) => (prev.some((m) => m.id === message.id) ? prev : [...prev, message]))
        // Aveline is now processing; the waiting state clears when an agent reply arrives.
        setWaiting(true)
      } finally {
        setSending(false)
      }
    },
    [activeConversationId, organizationId],
  )

  const decide = useCallback(
    async (messageId: string, approved: boolean) => {
      if (!activeConversationId) return
      const updated = await decideSignOff(organizationId, activeConversationId, messageId, approved)
      setMessages((prev) => prev.map((m) => (m.id === updated.id ? updated : m)))
    },
    [activeConversationId, organizationId],
  )

  return (
    <ConversationsContext.Provider
      value={{
        conversations,
        activeConversationId,
        messages,
        loading,
        sending,
        waiting,
        connectionState,
        openConversation,
        openOrCreateSalon,
        send,
        decide,
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
