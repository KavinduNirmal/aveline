import { describe, expect, it } from 'vitest'

import { buildItemQrJson, buildItemQrUrl } from './qrPayload'

const ITEM = '96050729-811b-4781-b65a-d071c8102dbf'
const ORG = '11111111-2222-3333-4444-555555555555'

describe('item QR payload', () => {
  it('names the boutique in the URL when a slug is in hand', () => {
    // The defect: the tag encoded `/catalog/items/{id}`, which names no shop, so the same code could
    // not be resolved to this boutique's piece.
    const url = buildItemQrUrl(
      { itemId: ITEM, organizationId: ORG, organizationSlug: 'aveline-colombo-07' },
      'https://app.aveline.luxury',
    )

    expect(url).toBe(`https://app.aveline.luxury/app/b/aveline-colombo-07/catalog/${ITEM}`)
  })

  it('falls back to the organisation id in the query when no slug is known, never to a bare link', () => {
    const url = buildItemQrUrl({ itemId: ITEM, organizationId: ORG }, 'https://app.aveline.luxury')

    expect(url).toBe(`https://app.aveline.luxury/catalog/items/${ITEM}?org=${ORG}`)
  })

  it('carries the organisation in the structured encoding, and omits it rather than inventing one', () => {
    const withOrg = JSON.parse(
      buildItemQrJson(
        { itemId: ITEM, sku: 'AVL-001', organizationId: ORG, organizationSlug: 'aveline-colombo-07' },
        'https://app.aveline.luxury',
      ),
    )

    expect(withOrg.type).toBe('aveline_inventory_item')
    expect(withOrg.orgId).toBe(ORG)
    expect(withOrg.itemId).toBe(ITEM)
    expect(withOrg.sku).toBe('AVL-001')
    expect(withOrg.url).toContain(`/app/b/aveline-colombo-07/catalog/${ITEM}`)
    expect(withOrg.v).toBe(1)

    const withoutOrg = JSON.parse(buildItemQrJson({ itemId: ITEM }, 'https://app.aveline.luxury'))
    expect(withoutOrg).not.toHaveProperty('orgId')
    expect(withoutOrg.url).toBe(`https://app.aveline.luxury/catalog/items/${ITEM}`)
  })
})
