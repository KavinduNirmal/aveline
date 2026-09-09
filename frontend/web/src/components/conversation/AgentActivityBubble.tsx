import { motion } from 'motion/react'

import { avelineStateConfig, type AvelineState } from './avelineStates'
import { AvelineAvatar } from './AvelineAvatar'

interface AgentActivityBubbleProps {
  state: AvelineState
}

/**
 * The live "Aveline is working" bubble shown while a reply is being produced. It reflects
 * the agent's current reasoning state (Thinking…, Searching…, Working…, Using a tool…) and
 * collapses away once the real reply lands.
 */
export function AgentActivityBubble({ state }: AgentActivityBubbleProps) {
  const label = avelineStateConfig(state).label

  return (
    <motion.div
      initial={{ opacity: 0, y: 10 }}
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, y: 6 }}
      transition={{ duration: 0.2 }}
      className="flex w-full gap-2.5"
    >
      <AvelineAvatar className="size-8" blossomClassName="size-5" />
      <div className="flex max-w-[78%] flex-col gap-1">
        <span className="px-1 text-[11px] font-medium text-primary">Aveline</span>
        <div className="flex items-center gap-2 rounded-2xl rounded-bl-md border border-border bg-card px-3.5 py-2.5">
          <span className="flex items-center gap-1">
            <span className="size-1.5 animate-bounce rounded-full bg-primary [animation-delay:-0.3s]" />
            <span className="size-1.5 animate-bounce rounded-full bg-primary [animation-delay:-0.15s]" />
            <span className="size-1.5 animate-bounce rounded-full bg-primary" />
          </span>
          <span className="text-sm text-muted-foreground">{label}</span>
        </div>
      </div>
    </motion.div>
  )
}
