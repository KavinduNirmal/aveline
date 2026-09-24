import { describe, expect, it } from 'vitest'

import { CONNECT_NULLS } from './dashboard-chart-rules'

/**
 * The tenant chart non-negotiable, pinned beside the admin equivalent
 * (`lib/admin/data-quality.test.ts`). The value is the whole point: a chart that connects a `null`
 * reports a measurement the server never made.
 */
describe('the tenant chart non-negotiables', () => {
  it('never connects a null across a gap', () => {
    expect(CONNECT_NULLS).toBe(false)
  })
})
