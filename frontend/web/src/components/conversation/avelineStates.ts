/**
 * Aveline's animated avatar states.
 *
 * The blossom avatar reflects Aveline's current agentic-workflow state. Today only the
 * idle animation (colour cycle + spin) is wired; the remaining states are stubs to be
 * driven by real agentic-workflow events (e.g. `agent.status` over SignalR) in a later
 * slice.
 *
 * Planned states:
 *  - `idle`      - resting, gently rotating + colour cycling (current default).
 *  - `thinking`  - Aveline is processing a reply (faster sway / pulse).
 *  - `working`   - a specialist (Ava/Elle/Lina) is actively working.
 *  - `awaiting`  - paused for a human decision (SignOff).
 *  - `error`     - something went wrong.
 */

export type AvelineState =
  | 'idle'
  | 'thinking'
  | 'working'
  | 'awaiting'
  | 'error'

/** Maps an agentic-workflow state to the blossom's animation behaviour. */
export interface AvelineStateConfig {
  /** Whether the blossom counter-swings its petals. */
  sway: boolean
  /** Whether the colour cycle is applied. */
  colourCycle: boolean
  /** Rotation duration in seconds (Infinity disables rotation). */
  spinSeconds: number
}

export const AVELINE_STATES: Record<AvelineState, AvelineStateConfig> = {
  idle: { sway: false, colourCycle: true, spinSeconds: 12 },
  thinking: { sway: true, colourCycle: true, spinSeconds: 6 },
  working: { sway: true, colourCycle: true, spinSeconds: 4 },
  awaiting: { sway: false, colourCycle: false, spinSeconds: 12 },
  error: { sway: false, colourCycle: false, spinSeconds: 12 },
}

/** Resolves the animation config for a state, defaulting to idle. */
export function avelineStateConfig(state: AvelineState): AvelineStateConfig {
  return AVELINE_STATES[state] ?? AVELINE_STATES.idle
}
