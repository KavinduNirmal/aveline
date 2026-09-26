import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import type { ContentBlock } from './blocks'
import { BlockList } from './blocks'
import type { BlockActionBridge, BlockActionId } from './blockActions'

/** A bridge whose every handler is spied, so a press can be traced to the surface that serves it. */
function bridge(
  overrides: {
    pending?: Record<string, BlockActionId | undefined>
    hasCustomerDestination?: boolean
    hasForwardDestination?: boolean
    agentBusy?: boolean
  } = {},
) {
  const handlers = {
    onCopy: vi.fn(),
    onForward: vi.fn(),
    onSendToCustomer: vi.fn(),
    onRegenerate: vi.fn(),
  }
  const value: BlockActionBridge = {
    handlers,
    environment: {
      hasCustomerDestination: overrides.hasCustomerDestination ?? true,
      hasForwardDestination: overrides.hasForwardDestination ?? true,
      agentBusy: overrides.agentBusy ?? false,
    },
    pending: (messageId) => overrides.pending?.[messageId] ?? null,
  }
  return { value, handlers }
}

/** The action ids the rail drew for the one block under test. */
function drawnActions(): (string | null)[] {
  return screen
    .queryAllByRole('button')
    .filter((element) => element.getAttribute('data-slot') === 'block-action')
    .map((element) => element.getAttribute('data-action'))
}

const SUGGESTION: ContentBlock = { type: 'suggestion', text: 'We found it in size M.' }
const PIECE: ContentBlock = { type: 'piece', name: 'Silk Slip Dress', price: 24000 }
const LOOK: ContentBlock = { type: 'look', name: 'Gala Ensemble', text: 'Gold with wine.' }

describe('which rail a block draws, end to end', () => {
  it('draws Copy, Send to customer and Regenerate under a suggestion', () => {
    const { value } = bridge()
    render(<BlockList blocks={[SUGGESTION]} messageId="msg-1" bridge={value} />)
    expect(drawnActions()).toEqual(['copy', 'send_to_customer', 'regenerate'])
  })

  it('draws Forward alone under an item block', () => {
    const { value } = bridge()
    render(<BlockList blocks={[PIECE]} messageId="msg-1" bridge={value} />)
    expect(drawnActions()).toEqual(['forward'])
  })

  it('draws Copy, Forward and Regenerate under a lookbook block', () => {
    const { value } = bridge()
    render(<BlockList blocks={[LOOK]} messageId="msg-1" bridge={value} />)
    expect(drawnActions()).toEqual(['copy', 'forward', 'regenerate'])
  })

  it('draws no rail under a block type the mapping does not name', () => {
    const { value } = bridge()
    render(<BlockList blocks={[{ type: 'text', text: 'Just words' }]} messageId="msg-1" bridge={value} />)
    expect(drawnActions()).toEqual([])
    expect(screen.queryByRole('toolbar')).not.toBeInTheDocument()
  })

  it('draws no rail at all when the surface wired nothing, exactly as before this existed', () => {
    render(<BlockList blocks={[SUGGESTION, PIECE, LOOK]} messageId="msg-1" />)
    expect(drawnActions()).toEqual([])
  })

  it('draws only the actions the surface can actually serve', () => {
    const { value } = bridge()
    // A read-only surface wired Copy alone: the rail shows Copy rather than two dead segments.
    const readOnly: BlockActionBridge = {
      ...value,
      handlers: { onCopy: vi.fn() },
    }
    render(<BlockList blocks={[SUGGESTION]} messageId="msg-1" bridge={readOnly} />)
    expect(drawnActions()).toEqual(['copy'])
  })
})

describe('the rail comes with the block it acts on', () => {
  it('is the card\'s last child, so it shapes the bottom edge', () => {
    const { value } = bridge()
    const { container } = render(<BlockList blocks={[SUGGESTION]} messageId="msg-1" bridge={value} />)

    const card = container.querySelector('[data-block-type="suggestion"]')!
    expect(card).not.toBeNull()
    expect(card.lastElementChild).toBe(screen.getByRole('toolbar'))
    // One overflow-hidden box holds both halves, which is what lets the card's radius clip the
    // rail's bottom corners into the shape of the card's foot.
    expect(card.className).toContain('overflow-hidden')
  })

  it('acts on the block it is drawn under, not on the message as a whole', async () => {
    const { value, handlers } = bridge()
    render(
      <BlockList
        blocks={[{ type: 'text', text: 'Three pieces match.' }, PIECE, LOOK]}
        messageId="msg-7"
        bridge={value}
      />,
    )

    // The sighting rail is the second toolbar (the text block has none); pressing its Forward
    // hands the surface the piece, never the message's text or the look beside it.
    const rails = screen.getAllByRole('toolbar')
    expect(rails).toHaveLength(2)

    await userEvent.click(document.querySelector('[data-block-type="piece"] [data-action="forward"]')!)
    expect(handlers.onForward).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'piece', name: 'Silk Slip Dress' }),
      'msg-7',
    )
  })
})

describe('per-block state', () => {
  it('leaves one block\'s rail live while another block is mid-action', () => {
    const { value } = bridge({ pending: { 'msg-1': 'regenerate' } })
    render(
      <>
        <BlockList blocks={[SUGGESTION]} messageId="msg-1" bridge={value} />
        <BlockList blocks={[LOOK]} messageId="msg-2" bridge={value} />
      </>,
    )

    const busyBlock = document.querySelector('[data-block-type="suggestion"]')!
    const idleBlock = document.querySelector('[data-block-type="look"]')!

    expect(busyBlock.querySelector('[data-action="copy"]')).toHaveAttribute('aria-disabled', 'true')
    expect(idleBlock.querySelector('[data-action="copy"]')).not.toHaveAttribute('aria-disabled')
  })

  it('refuses Send to customer, with its reason, on a thread with no client', async () => {
    const { value, handlers } = bridge({ hasCustomerDestination: false })
    render(<BlockList blocks={[SUGGESTION]} messageId="msg-1" bridge={value} />)

    const send = document.querySelector('[data-action="send_to_customer"]') as HTMLElement
    expect(send).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(send)
    expect(handlers.onSendToCustomer).not.toHaveBeenCalled()
  })

  it('refuses Regenerate while Aveline is already answering', async () => {
    const { value, handlers } = bridge({ agentBusy: true })
    render(<BlockList blocks={[SUGGESTION]} messageId="msg-1" bridge={value} />)

    const regenerate = document.querySelector('[data-action="regenerate"]')!
    expect(regenerate).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(regenerate)
    expect(handlers.onRegenerate).not.toHaveBeenCalled()
  })
})

describe('the full flow: render block, click the action, see the outcome', () => {
  it('copies a suggestion through the surface that owns the clipboard', async () => {
    const { value, handlers } = bridge()
    render(<BlockList blocks={[SUGGESTION]} messageId="msg-1" bridge={value} />)

    await userEvent.click(screen.getByRole('button', { name: 'Copy' }))
    expect(handlers.onCopy).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'suggestion' }),
      'msg-1',
    )
  })

  it('regenerates a look through the surface that owns the agent', async () => {
    const { value, handlers } = bridge()
    render(<BlockList blocks={[LOOK]} messageId="msg-9" bridge={value} />)

    await userEvent.click(screen.getByRole('button', { name: 'Regenerate' }))
    expect(handlers.onRegenerate).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'look', name: 'Gala Ensemble' }),
      'msg-9',
    )
  })

  it('forwards a piece through the surface that owns the Salon list', async () => {
    const { value, handlers } = bridge()
    render(<BlockList blocks={[PIECE]} messageId="msg-3" bridge={value} />)

    await userEvent.click(screen.getByRole('button', { name: 'Forward' }))
    expect(handlers.onForward).toHaveBeenCalledWith(
      expect.objectContaining({ type: 'piece' }),
      'msg-3',
    )
  })
})
