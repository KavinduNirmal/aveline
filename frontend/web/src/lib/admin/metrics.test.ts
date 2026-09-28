import { describe, expect, it } from 'vitest'

import { GRAFANA_DASHBOARD_UIDS } from './grafana'
import { DASHBOARD_WIDGETS, REMOVED_WIDGETS, findWidget } from './metrics'

describe('the V1–V11 dashboard catalogue', () => {
  it('declares exactly the eleven widgets, in order', () => {
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

  it('names a source and a rationale for every widget', () => {
    for (const widget of DASHBOARD_WIDGETS) {
      expect(widget.source.length, widget.id).toBeGreaterThan(0)
      expect(widget.rationale.length, widget.id).toBeGreaterThan(0)
    }
  })

  it('only ever links to one of the four provisioned Grafana UIDs', () => {
    const known = new Set(Object.keys(GRAFANA_DASHBOARD_UIDS))
    for (const widget of DASHBOARD_WIDGETS) {
      if (widget.grafana === null) continue
      expect(known.has(widget.grafana), `${widget.id} links to ${widget.grafana}`).toBe(true)
    }
  })

  it('never declares a Prometheus series name as a source', () => {
    for (const widget of DASHBOARD_WIDGETS) {
      expect(widget.source, widget.id).not.toMatch(/aveline_|pg_/)
    }
  })

  it('records the widgets dropped as duplicates of Grafana panels', () => {
    expect(REMOVED_WIDGETS.length).toBeGreaterThanOrEqual(5)
    const present = DASHBOARD_WIDGETS.map((widget) => widget.title).join(' | ')
    // The two largest chart specs were duplicates of Grafana's own panels.
    expect(present).not.toMatch(/latency percentile/i)
    expect(present).not.toMatch(/5xx volume/i)
  })

  it('resolves a widget by id', () => {
    expect(findWidget('V11')?.kind).toBe('link')
    expect(findWidget('V0')).toBeUndefined()
  })
})
