/**
 * Derived log severity (Q4).
 *
 * The audit endpoint has **no severity field**. Anything the console shows as a level is the
 * console's own reading of the action string, so it is *labelled as derived* and must never be
 * presented as server truth. The label carries a colour, and that colour is a **semantic token**
 * (`text-destructive`, `text-warning`, `text-success`, `text-primary`, `text-muted-foreground`),
 * never a raw palette class — the delivered `deriveLogLevel` returned `info` for an unrecognised
 * action, which filed every unknown action as a success.
 *
 * `actorKind` is the primary axis of the source, per Q4.
 */

import type { LogEntry } from './log-stream'

export const LOG_LEVELS = ['error', 'warn', 'info', 'other'] as const

export type LogLevel = (typeof LOG_LEVELS)[number]

/**
 * The semantic colour token a level renders with. These are Tailwind semantic utilities, not the
 * `red-500`-style palette classes the conformance rule forbids, and they invert in dark mode.
 */
export type SemanticTone = 'destructive' | 'warning' | 'primary' | 'muted'

const LEVEL_TONE: Record<LogLevel, SemanticTone> = {
  error: 'destructive',
  warn: 'warning',
  info: 'primary',
  other: 'muted',
}

const LEVEL_LABEL: Record<LogLevel, string> = {
  error: 'Error',
  warn: 'Warning',
  info: 'Info',
  other: 'Unclassified',
}

const ERROR_MARKERS = [
  'fail',
  'error',
  'reject',
  'denied',
  'suspended',
  'suspend',
  'unauthor',
  'exception',
  'timeout',
  'timed_out',
] as const

const WARN_MARKERS = [
  'cancel',
  'revoke',
  'warning',
  'warn',
  'deprecat',
  'expired',
  'expire',
  'retry',
  'rolled_back',
] as const

const INFO_MARKERS = [
  'create',
  'created',
  'update',
  'updated',
  'activate',
  'approved',
  'granted',
  'credited',
  'published',
  'completed',
  'acknowledged',
  'login',
  'invited',
] as const

/**
 * Reads a level out of an action name. An unrecognised action is `other` — explicitly
 * unclassified — rather than the delivered `debug` default, which made an unknown action look
 * like a harmless one.
 */
export function deriveLevel(action: string): LogLevel {
  const lower = action.toLowerCase()
  if (lower.length === 0) return 'other'
  if (ERROR_MARKERS.some((marker) => lower.includes(marker))) return 'error'
  if (WARN_MARKERS.some((marker) => lower.includes(marker))) return 'warn'
  if (INFO_MARKERS.some((marker) => lower.includes(marker))) return 'info'
  return 'other'
}

export function levelTone(level: LogLevel): SemanticTone {
  return LEVEL_TONE[level]
}

export function levelLabel(level: LogLevel): string {
  return LEVEL_LABEL[level]
}

export interface LogSource {
  /** `actorKind` is primary. */
  label: string
  /** The most specific reference the entry carries, when it carries one. */
  detail: string | null
  tone: SemanticTone
}

const SOURCE_TONES: Record<string, SemanticTone> = {
  user: 'primary',
  admin: 'primary',
  system: 'muted',
  scheduler: 'muted',
  service: 'muted',
  worker: 'muted',
  alert: 'warning',
  migration: 'muted',
}

/**
 * The entry's actor, labelled and toned. A blank `actorKind` says *"Unknown actor"* rather than
 * rendering an empty cell the operator reads as "no actor".
 */
export function deriveSource(
  entry: Pick<LogEntry, 'actorKind' | 'actorUserId' | 'actorRef'>,
): LogSource {
  const kind = (entry.actorKind ?? '').trim()
  const label = kind.length > 0 ? kind : 'Unknown actor'
  const tone = SOURCE_TONES[label.toLowerCase()] ?? 'muted'
  const detail = entry.actorRef ?? entry.actorUserId ?? null
  return { label, detail, tone }
}
