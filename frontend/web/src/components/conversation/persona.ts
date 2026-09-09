import { AGENT_KEYS, type AgentKey } from '@/types/conversation'

/** Display metadata for an Aveline agent persona. */
export interface Persona {
  key: string
  name: string
  /** Tailwind text color class for the persona accent. */
  text: string
  /** Tailwind background color class for the persona accent. */
  bg: string
  /** Tailwind soft (tinted) background color class for avatar chips. */
  bgSoft: string
  /** Tailwind ring/border color class. */
  ring: string
  /** Short descriptor shown under the name. */
  role: string
}

const AGENT_PERSONAS: Record<AgentKey, Persona> = {
  [AGENT_KEYS.aveline]: {
    key: AGENT_KEYS.aveline,
    name: 'Aveline',
    text: 'text-primary',
    bg: 'bg-primary',
    bgSoft: 'bg-primary/10',
    ring: 'ring-primary/20',
    role: 'Your concierge',
  },
  [AGENT_KEYS.ava]: {
    key: AGENT_KEYS.ava,
    name: 'Ava',
    text: 'text-memory',
    bg: 'bg-memory',
    bgSoft: 'bg-memory/10',
    ring: 'ring-memory/20',
    role: 'Memory',
  },
  [AGENT_KEYS.elle]: {
    key: AGENT_KEYS.elle,
    name: 'Elle',
    text: 'text-visual',
    bg: 'bg-visual',
    bgSoft: 'bg-visual/10',
    ring: 'ring-visual/20',
    role: 'Visual sourcing',
  },
  [AGENT_KEYS.lina]: {
    key: AGENT_KEYS.lina,
    name: 'Lina',
    text: 'text-commerce',
    bg: 'bg-commerce',
    bgSoft: 'bg-commerce/10',
    ring: 'ring-commerce/20',
    role: 'Commerce',
  },
}

/** Resolves a persona for an agent key, defaulting to Aveline for unknown keys. */
export function personaForAgent(agentKey: string | null | undefined): Persona {
  if (agentKey && agentKey in AGENT_PERSONAS) {
    return AGENT_PERSONAS[agentKey as AgentKey]
  }
  return AGENT_PERSONAS[AGENT_KEYS.aveline]
}

/** Resolves a persona for a message author kind + agent key. */
export function personaForAuthor(
  authorKind: string,
  agentKey: string | null | undefined,
): Persona | null {
  if (authorKind === 'Agent') {
    return personaForAgent(agentKey)
  }
  return null
}
