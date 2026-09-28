import { describe, expect, it } from 'vitest'

import {
  CONNECT_NULLS,
  agentDataQuality,
  apiDataQuality,
  businessDataQuality,
  formatMetricValue,
  systemDataQuality,
} from './data-quality'

/**
 * Authority: `BR-7.10` (`docs/backend/backend-requirements.md:989`) — *"A metric whose value
 * cannot be determined is omitted, never recorded as 0."*
 *
 * Three families have three distinct vocabularies and **none may be coerced into another**: the
 * system family says `omitted[]` + `dataQuality{points,measured,omitted[]}`, the agent family
 * says five booleans, and the API family says three.
 */
describe('systemDataQuality', () => {
  it('names each omitted metric', () => {
    const report = systemDataQuality({
      omitted: ['inbound_message_backlog', 'blossoms_per_hour'],
      dataQuality: { points: 24, measured: true, omitted: [] },
    })
    expect(report.messages).toContain('inbound_message_backlog: not measured on this host')
    expect(report.messages).toContain('blossoms_per_hour: not measured on this host')
  })

  it('says the series is unmeasured when the server says so', () => {
    const report = systemDataQuality({
      dataQuality: { points: 0, measured: false, omitted: ['error_rate'] },
    })
    expect(report.messages).toContain('error_rate: not measured on this host')
    expect(report.messages.join(' ')).toMatch(/no measured points/i)
    expect(report.vocabulary).toBe('system')
  })

  it('uses the system vocabulary, never the agent one', () => {
    const report = systemDataQuality({ omitted: ['x'] })
    expect(report.messages.join(' ')).not.toMatch(/instrumented|no runs recorded/i)
  })
})

describe('agentDataQuality', () => {
  it('names each false instrumentation flag', () => {
    const report = agentDataQuality({
      totalRuns: 12,
      latencyInstrumented: false,
      nodeFailuresObserved: false,
      perStepAttribution: true,
      toolInstrumented: false,
      costInstrumented: true,
    })
    const text = report.messages.join(' | ')
    expect(text).toMatch(/latency is not instrumented/i)
    expect(text).toMatch(/node failures are not observed/i)
    expect(text).toMatch(/tool calls are not instrumented/i)
    expect(report.vocabulary).toBe('agent')
  })

  it('distinguishes "no runs recorded yet" from "not instrumented"', () => {
    const empty = agentDataQuality({
      totalRuns: 0,
      latencyInstrumented: true,
      nodeFailuresObserved: true,
      perStepAttribution: true,
      toolInstrumented: true,
      costInstrumented: true,
    })
    expect(empty.messages).toEqual(['no runs recorded yet'])

    const uninstrumented = agentDataQuality({
      totalRuns: 5,
      latencyInstrumented: false,
      nodeFailuresObserved: true,
      perStepAttribution: true,
      toolInstrumented: true,
      costInstrumented: true,
    })
    expect(uninstrumented.messages.join(' ')).not.toMatch(/no runs recorded yet/i)
  })
})

describe('apiDataQuality', () => {
  it('names each false flag', () => {
    const report = apiDataQuality({
      rollupComplete: false,
      rawLogSampled: true,
      latencyBuckets: false,
    })
    const text = report.messages.join(' | ')
    expect(text).toMatch(/rollup is incomplete/i)
    expect(text).toMatch(/latency buckets are not available/i)
    expect(text).not.toMatch(/instrumented/i)
    expect(report.vocabulary).toBe('api')
  })

  it('reports nothing when every flag is true', () => {
    expect(
      apiDataQuality({ rollupComplete: true, rawLogSampled: true, latencyBuckets: true })
        .messages,
    ).toEqual([])
  })
})

describe('the chart non-negotiables', () => {
  it('never connects a null across a gap', () => {
    expect(CONNECT_NULLS).toBe(false)
  })

  it('renders a null as "not measured", never as zero', () => {
    expect(formatMetricValue(null, 'percent')).toBe('not measured')
    expect(formatMetricValue(undefined, 'percent')).toBe('not measured')
    expect(formatMetricValue(0, 'percent')).toBe('0.00%')
  })
})

describe('businessDataQuality', () => {
  const clean = {
    userAttributionAvailable: true,
    unresolvedAttributionCount: 0,
    subscriptionHistoryBackfilled: false,
    lastActivityIsReconstructed: false,
    agentMetricsUninstrumented: false,
    notes: [],
  }

  it('declares its own vocabulary rather than impersonating another family', () => {
    expect(businessDataQuality(clean).vocabulary).toBe('business')
  })

  it('reports nothing for a fully measured payload', () => {
    expect(businessDataQuality(clean).messages).toEqual([])
  })

  it('names an unavailable attribution rather than leaving a null reading unexplained', () => {
    const report = businessDataQuality({ ...clean, userAttributionAvailable: false })

    expect(report.messages.join(' ')).toMatch(/not measured/i)
  })

  it('names unresolved attribution as an undercount when the count is positive', () => {
    const report = businessDataQuality({ ...clean, unresolvedAttributionCount: 12 })

    expect(report.messages.join(' ')).toContain('12')
  })

  it('does not report an undercount when the unresolved count is zero', () => {
    const report = businessDataQuality({ ...clean, unresolvedAttributionCount: 0 })

    expect(report.messages.join(' ')).not.toMatch(/undercount/i)
  })

  it('marks reconstructed subscription history as approximate', () => {
    const report = businessDataQuality({ ...clean, subscriptionHistoryBackfilled: true })

    expect(report.messages.join(' ')).toMatch(/approximate|reconstructed/i)
  })

  it('names a reconstructed last-activity figure', () => {
    const report = businessDataQuality({ ...clean, lastActivityIsReconstructed: true })

    expect(report.messages.join(' ')).toMatch(/reconstruct/i)
  })

  it('names uninstrumented agent metrics', () => {
    const report = businessDataQuality({ ...clean, agentMetricsUninstrumented: true })

    expect(report.messages.join(' ')).toMatch(/agent/i)
  })

  it('carries the server notes through as their own messages', () => {
    const report = businessDataQuality({ ...clean, notes: ['a server note', 'another note'] })

    expect(report.messages).toContain('a server note')
    expect(report.messages).toContain('another note')
  })
})
