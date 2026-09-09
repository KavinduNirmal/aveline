/**
 * Aveline's animated avatar states.
 *
 * The blossom avatar reflects Aveline's current agentic-workflow state, driven by
 * `agent.status` events received over SignalR (`ReceiveAgentState`). Each state maps to an
 * animation primitive (see {@link AvelineMode}) so the flower behaviour is consistent across
 * the web and Flutter chats.
 *
 * States:
 *  - `idle`       - resting, gently breathing (colour cycle + slow rotation).
 *  - `thinking`   - Aveline is classifying / routing (slow contraction/pulse).
 *  - `searching`  - a specialist is retrieving (ripple around the petals).
 *  - `processing` - Aveline is synthesising (rotation).
 *  - `tool_call`  - a specialist is invoking a tool (mechanical pulse).
 *  - `waiting`    - paused for a human decision (slow sway).
 *  - `success`    - the workflow completed (one-shot bloom).
 *  - `error`      - something went wrong (one-shot shake).
 *  - `response`   - the reply is being delivered (bloom, then settle to idle).
 */

export type AvelineState =
  | 'idle'
  | 'thinking'
  | 'searching'
  | 'processing'
  | 'tool_call'
  | 'waiting'
  | 'success'
  | 'error'
  | 'response'

/** The animation primitive driving the blossom for a given state. */
export type AvelineMode =
  | 'breathing'
  | 'pulse'
  | 'ripple'
  | 'rotation'
  | 'rotationPulse'
  | 'sway'
  | 'bloom'
  | 'shake'

/** Maps an agentic-workflow state to the blossom's animation behaviour. */
export interface AvelineStateConfig {
  /** The animation primitive to play. */
  mode: AvelineMode
  /** Whether the blossom counter-swings its petals. */
  sway: boolean
  /** Whether the colour cycle is applied. */
  colourCycle: boolean
  /** Rotation duration in seconds (Infinity disables rotation). */
  spinSeconds: number
  /** Whether the animation loops (false for one-shot bloom/shake). */
  looping: boolean
  /** Human-readable status line shown under the avatar. */
  label: string
}

export const AVELINE_STATES: Record<AvelineState, AvelineStateConfig> = {
  idle: { mode: 'breathing', sway: false, colourCycle: true, spinSeconds: 20, looping: true, label: 'Your boutique concierge' },
  thinking: { mode: 'pulse', sway: true, colourCycle: true, spinSeconds: 10, looping: true, label: 'Thinking…' },
  searching: { mode: 'ripple', sway: false, colourCycle: true, spinSeconds: Infinity, looping: true, label: 'Searching…' },
  processing: { mode: 'rotation', sway: false, colourCycle: true, spinSeconds: 7, looping: true, label: 'Working…' },
  tool_call: { mode: 'rotationPulse', sway: false, colourCycle: true, spinSeconds: 5, looping: true, label: 'Using a tool…' },
  waiting: { mode: 'sway', sway: true, colourCycle: false, spinSeconds: Infinity, looping: true, label: 'Awaiting your decision…' },
  success: { mode: 'bloom', sway: false, colourCycle: false, spinSeconds: Infinity, looping: false, label: 'Done' },
  error: { mode: 'shake', sway: false, colourCycle: false, spinSeconds: Infinity, looping: false, label: 'Something went wrong' },
  response: { mode: 'bloom', sway: false, colourCycle: false, spinSeconds: Infinity, looping: false, label: '' },
}

/** Resolves the animation config for a state, defaulting to idle. */
export function avelineStateConfig(state: AvelineState): AvelineStateConfig {
  return AVELINE_STATES[state] ?? AVELINE_STATES.idle
}
