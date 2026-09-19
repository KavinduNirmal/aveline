/**
 * The honesty vocabulary. Three families, three shapes, and **none is coerced into another**.
 *
 * Authority: `BR-7.10` (`docs/backend/backend-requirements.md:989`) — *"A metric whose value
 * cannot be determined is omitted, never recorded as 0."*
 *
 * - **system**: `omitted: string[]` plus `dataQuality { points, measured, omitted[] }`
 * - **agent**: five booleans — `latencyInstrumented`, `nodeFailuresObserved`,
 *   `perStepAttribution`, `toolInstrumented`, `costInstrumented`
 * - **api**: three booleans — `rollupComplete`, `rawLogSampled`, `latencyBuckets`
 */

export type DataQualityVocabulary = 'system' | 'agent' | 'api'

export interface DataQualityReport {
  vocabulary: DataQualityVocabulary
  messages: string[]
}

/** Every chart: a `null` is a **gap**, never a line through zero. */
export const CONNECT_NULLS = false

/**
 * Renders a metric value. A value the server could not determine is *"not measured"* — never
 * `0.00%`, which is the specific lie the delivered console told.
 */
export function formatMetricValue(
  value: number | null | undefined,
  unit: 'percent' | 'count' | 'seconds' | 'raw' = 'raw',
): string {
  if (value === null || value === undefined || Number.isNaN(value)) return 'not measured'
  switch (unit) {
    case 'percent':
      return `${(value * 100).toFixed(2)}%`
    case 'seconds':
      return `${value.toFixed(1)} s`
    case 'count':
      return value.toLocaleString()
    default:
      return String(value)
  }
}

export interface SystemDataQualityInput {
  omitted?: readonly string[] | undefined
  dataQuality?:
    | { points?: number | undefined; measured?: boolean | undefined; omitted?: readonly string[] | undefined }
    | undefined
}

/** The system family's vocabulary. */
export function systemDataQuality(input: SystemDataQualityInput): DataQualityReport {
  const messages: string[] = []
  const names = [
    ...(input.omitted ?? []),
    ...(input.dataQuality?.omitted ?? []),
  ]
  for (const name of Array.from(new Set(names))) {
    messages.push(`${name}: not measured on this host`)
  }
  if (input.dataQuality?.measured === false) {
    messages.push('the server reports no measured points for this series')
  }
  if (input.dataQuality?.points === 0) {
    messages.push('no points in this window')
  }
  return { vocabulary: 'system', messages }
}

export interface AgentDataQualityInput {
  totalRuns: number
  latencyInstrumented: boolean
  nodeFailuresObserved: boolean
  perStepAttribution: boolean
  toolInstrumented: boolean
  costInstrumented: boolean
}

/**
 * The agent family's vocabulary.
 *
 * `totalRuns === 0` is a **different message** from a false instrumentation flag: "nothing has
 * run yet" is not "we cannot see it".
 */
export function agentDataQuality(input: AgentDataQualityInput): DataQualityReport {
  if (input.totalRuns === 0) {
    return { vocabulary: 'agent', messages: ['no runs recorded yet'] }
  }
  const messages: string[] = []
  if (!input.latencyInstrumented) messages.push('latency is not instrumented')
  if (!input.nodeFailuresObserved) messages.push('node failures are not observed')
  if (!input.perStepAttribution) messages.push('per-step attribution is not instrumented')
  if (!input.toolInstrumented) messages.push('tool calls are not instrumented')
  if (!input.costInstrumented) messages.push('cost is not instrumented')
  return { vocabulary: 'agent', messages }
}

export interface ApiDataQualityInput {
  rollupComplete: boolean
  rawLogSampled: boolean
  latencyBuckets: boolean
}

/** The API family's vocabulary. */
export function apiDataQuality(input: ApiDataQualityInput): DataQualityReport {
  const messages: string[] = []
  if (!input.rollupComplete) messages.push('the rollup is incomplete; counts are partial')
  if (!input.rawLogSampled) messages.push('raw request logs were not sampled for this window')
  if (!input.latencyBuckets) messages.push('latency buckets are not available for this window')
  return { vocabulary: 'api', messages }
}
