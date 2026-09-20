import { describe, expect, it } from 'vitest'

import type { BusinessDataQuality } from '@/types/admin'

import { BUSINESS_WIDGETS, findBusinessWidget } from './business-kpis'
import { DASHBOARD_WIDGETS } from './metrics'

/**
 * The `B1…B12` catalogue guard (DR-2). Deliberately a **separate catalogue** from `metrics.ts`:
 * C5 pins that one to exactly `V1…V11`, and extending it would blur a deliberately frozen
 * contract. This test gives the new catalogue the same guarantee, plus a cross-catalogue guard.
 */
describe('the B1…B12 business-KPI catalogue', () => {
  it('declares twelve widgets with unique, contiguous ids', () => {
    expect(BUSINESS_WIDGETS.map((widget) => widget.id)).toEqual([
      'B1',
      'B2',
      'B3',
      'B4',
      'B5',
      'B6',
      'B7',
      'B8',
      'B9',
      'B10',
      'B11',
      'B12',
    ])
  })

  it('names a title, a source and a definition for every widget', () => {
    for (const widget of BUSINESS_WIDGETS) {
      expect(widget.title.length, widget.id).toBeGreaterThan(0)
      expect(widget.source.length, widget.id).toBeGreaterThan(0)
      expect(widget.definition.length, widget.id).toBeGreaterThan(0)
    }
  })

  it('sources every widget from the shipped business endpoint family', () => {
    for (const widget of BUSINESS_WIDGETS) {
      expect(widget.source, widget.id).toMatch(
        /^\/api\/v1\/admin\/statistics\/business\/(growth|active-users|plan-mix|subscriptions|usage|organizations)/,
      )
    }
  })

  it('never declares a Prometheus series name as a source', () => {
    for (const widget of BUSINESS_WIDGETS) {
      expect(widget.source, widget.id).not.toMatch(/aveline_|pg_/)
    }
  })

  it('declares a kind that the surface can actually render', () => {
    const kinds = new Set(['tile', 'trend', 'table', 'distribution'])
    for (const widget of BUSINESS_WIDGETS) {
      expect(kinds.has(widget.kind), widget.id).toBe(true)
    }
  })

  it('names only keys that exist on the business data-quality contract', () => {
    const known: ReadonlyArray<keyof BusinessDataQuality> = [
      'userAttributionAvailable',
      'unresolvedAttributionCount',
      'subscriptionHistoryBackfilled',
      'lastActivityIsReconstructed',
      'agentMetricsUninstrumented',
      'notes',
    ]
    for (const widget of BUSINESS_WIDGETS) {
      if (widget.dataQualityDependency === undefined) continue
      expect(known, widget.id).toContain(widget.dataQualityDependency)
    }
  })

  it('resolves its own entries by id', () => {
    expect(findBusinessWidget('B1')?.title).toBe('New users')
    expect(findBusinessWidget('B99')).toBeUndefined()
  })

  it('leaves the V1–V11 catalogue untouched (cross-catalogue guard)', () => {
    expect(DASHBOARD_WIDGETS.map((widget) => widget.id)).toEqual([
      'V1',
      'V2',
      'V3',
      'V4',
      'V5',
      'V6',
      'V7',
      'V8',
      'V9',
      'V10',
      'V11',
    ])
  })
})
