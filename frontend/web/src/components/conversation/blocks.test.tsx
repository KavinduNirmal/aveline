import { describe, expect, it } from 'vitest'
import { renderToString } from 'react-dom/server'

import { BlockList, BlockRenderer, type ContentBlock } from './blocks'

describe('BlockRenderer', () => {
  it('renders a text block', () => {
    const html = renderToString(<BlockRenderer block={{ type: 'text', text: 'Hello there' }} />)
    expect(html).toContain('Hello there')
  })

  it('renders a piece block with price', () => {
    const block: ContentBlock = { type: 'piece', name: 'Silk Slip Dress', price: 24000, size: 'M', stock: 2 }
    const html = renderToString(<BlockRenderer block={block} />)
    expect(html).toContain('Silk Slip Dress')
    expect(html).toContain('24,000')
    expect(html).toContain('Size')
    expect(html).toContain('in stock')
  })

  it('renders an at_a_glance table', () => {
    const block: ContentBlock = {
      type: 'at_a_glance',
      columns: ['Size', 'Price'],
      rows: [['M', '24000'], ['L', '26000']],
    }
    const html = renderToString(<BlockRenderer block={block} />)
    expect(html).toContain('Size')
    expect(html).toContain('26000')
  })

  it('renders a sign_off block with approve/reject when a handler is provided', () => {
    const block: ContentBlock = { type: 'sign_off', reason: 'above limit', amount: 48000 }
    const html = renderToString(<BlockRenderer block={block} onSignOff={() => undefined} />)
    expect(html).toContain('Approval needed')
    expect(html).toContain('Approve')
    expect(html).toContain('Reject')
  })

  it('renders a client_message block', () => {
    const block: ContentBlock = { type: 'client_message', from: '+94771234567', text: 'Do you have this?' }
    const html = renderToString(<BlockRenderer block={block} />)
    expect(html).toContain('Customer')
    expect(html).toContain('Do you have this?')
  })

  it('returns null for an unknown block type', () => {
    const html = renderToString(<BlockRenderer block={{ type: 'unknown' }} />)
    expect(html).toBe('')
  })
})

describe('BlockList', () => {
  it('renders nothing for an empty block list', () => {
    const html = renderToString(<BlockList blocks={[]} />)
    expect(html).toBe('')
  })

  it('renders multiple blocks', () => {
    const blocks = [
      { type: 'text', text: 'Three pieces match.' },
      { type: 'piece', name: 'Dress', price: 1000 },
    ]
    const html = renderToString(<BlockList blocks={blocks} />)
    expect(html).toContain('Three pieces match.')
    expect(html).toContain('Dress')
  })
})
