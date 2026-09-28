import { describe, expect, it } from 'vitest'

import {
  BLOCK_ACTION_LABELS,
  actionsForBlockType,
  blockTitle,
  blockToText,
  deliveryTargetsFrom,
  hasBlockActions,
  isDeliverable,
  resolveBlockActions,
  type ActionableBlock,
  type BlockActionEnvironment,
} from './blockActions'
import type { ConversationDto } from '@/types/conversation'

/** An environment where every action the block offers is available. */
function openEnvironment(
  overrides: Partial<BlockActionEnvironment> = {},
): BlockActionEnvironment {
  return {
    hasCustomerDestination: true,
    hasForwardDestination: true,
    agentBusy: false,
    pending: null,
    ...overrides,
  }
}

/** The ids the rail would draw for a block, which is the brief's action mapping. */
function actionIds(block: ActionableBlock, environment = openEnvironment()) {
  return resolveBlockActions(block, environment).map((action) => action.id)
}

describe('the action mapping per block type', () => {
  it('offers Copy, Send to customer and Regenerate on a suggestion', () => {
    expect(actionIds({ type: 'suggestion', text: 'We found a piece for you.' })).toEqual([
      'copy',
      'send_to_customer',
      'regenerate',
    ])
  })

  it('offers Forward alone on an item block', () => {
    // The brief names one action for an item block, so the tile grid does not grow a Copy.
    expect(actionIds({ type: 'piece', name: 'Silk Slip Dress' })).toEqual(['forward'])
  })

  it('offers Copy, Forward and Regenerate on a lookbook block', () => {
    expect(actionIds({ type: 'look', name: 'Gala Ensemble' })).toEqual([
      'copy',
      'forward',
      'regenerate',
    ])
  })

  it('offers no Share anywhere: every delivery action goes out over a channel', () => {
    // Share was dropped deliberately — an action that only produced a link would sit beside
    // Forward and Send doing nothing the customer could receive.
    for (const type of ['suggestion', 'piece', 'item', 'look', 'lookbook']) {
      expect(actionsForBlockType(type)).not.toContain('share')
    }
  })

  it('resolves the brief\'s own block names to the same rails as the wire names', () => {
    // `item` and `lookbook` are the brief's words; `piece` and `look` are the wire's.
    expect(actionsForBlockType('item')).toEqual(actionsForBlockType('piece'))
    expect(actionsForBlockType('lookbook')).toEqual(actionsForBlockType('look'))
  })

  it('offers nothing, and so draws no rail, on a block type that is not in the mapping', () => {
    for (const type of ['text', 'at_a_glance', 'sign_off', 'client_message', 'payment', 'courier', 'attachment', 'choice', 'unknown']) {
      expect(actionsForBlockType(type)).toEqual([])
      expect(hasBlockActions({ type })).toBe(false)
    }
  })

  it('never renders an action outside the block type\'s own set', () => {
    // A thread that can do everything must still not put Regenerate on a piece.
    const piece = resolveBlockActions({ type: 'piece', name: 'Dress' }, openEnvironment())
    expect(piece.map((action) => action.id)).not.toContain('regenerate')
    expect(piece.map((action) => action.id)).not.toContain('copy')
    expect(piece).toHaveLength(1)
  })

  it('labels every action it can draw', () => {
    for (const type of ['suggestion', 'piece', 'look']) {
      for (const action of resolveBlockActions({ type }, openEnvironment())) {
        expect(action.label).toBe(BLOCK_ACTION_LABELS[action.id])
        expect(action.shortLabel.length).toBeGreaterThan(0)
      }
    }
  })
})

describe('availability within a block\'s own action set', () => {
  it('disables Send to customer, with a reason, when the thread has no client', () => {
    const actions = resolveBlockActions(
      { type: 'suggestion', text: 'Draft' },
      openEnvironment({ hasCustomerDestination: false }),
    )
    const send = actions.find((action) => action.id === 'send_to_customer')!
    expect(send.enabled).toBe(false)
    expect(send.reason).toMatch(/linked to a client/i)
  })

  it('disables Forward, with a reason, when there is nowhere to forward to', () => {
    const actions = resolveBlockActions(
      { type: 'look', name: 'Look' },
      openEnvironment({ hasForwardDestination: false }),
    )
    const forward = actions.find((action) => action.id === 'forward')!
    expect(forward.enabled).toBe(false)
    expect(forward.reason).toMatch(/no other client to forward to/i)
  })

  it('disables Regenerate, with a reason, while Aveline is still answering', () => {
    const actions = resolveBlockActions(
      { type: 'suggestion', text: 'Draft' },
      openEnvironment({ agentBusy: true }),
    )
    const regenerate = actions.find((action) => action.id === 'regenerate')!
    expect(regenerate.enabled).toBe(false)
    expect(regenerate.reason).toMatch(/still working/i)
  })

  it('marks the in-flight action busy and disables every other action on that block', () => {
    const actions = resolveBlockActions(
      { type: 'suggestion', text: 'Draft' },
      openEnvironment({ pending: 'send_to_customer' }),
    )
    const sending = actions.find((action) => action.id === 'send_to_customer')!
    expect(sending.busy).toBe(true)
    expect(sending.enabled).toBe(false)

    for (const other of actions.filter((action) => action.id !== 'send_to_customer')) {
      expect(other.enabled).toBe(false)
      expect(other.reason).toMatch(/already running/i)
    }
  })

  it('leaves Copy available whatever the thread is doing', () => {
    const actions = resolveBlockActions(
      { type: 'look', name: 'Look' },
      openEnvironment({ hasCustomerDestination: false, hasForwardDestination: false, agentBusy: true }),
    )
    expect(actions.find((action) => action.id === 'copy')!.enabled).toBe(true)
  })
})

describe('the payload each action hands over', () => {
  it('passes a suggestion through as the sentence to send', () => {
    expect(blockToText({ type: 'suggestion', text: '  We have it in size M.  ' })).toBe(
      'We have it in size M.',
    )
  })

  it('writes a piece as one line: name, size and money', () => {
    expect(
      blockToText({ type: 'piece', name: 'Silk Slip Dress', size: 'M', price: 24000 }),
    ).toBe('Silk Slip Dress · Size M · LKR 24,000')
  })

  it('omits the parts of a piece the server never sent', () => {
    expect(blockToText({ type: 'piece', name: 'Silk Slip Dress' })).toBe('Silk Slip Dress')
  })

  it('writes a look as its name and its rationale', () => {
    expect(blockToText({ type: 'look', name: 'Gala Ensemble', text: 'Gold with wine.' })).toBe(
      'Gala Ensemble — Gold with wine.',
    )
  })

  it('does not print a look\'s name twice when it has no note of its own', () => {
    expect(blockToText({ type: 'look', text: 'Gold with wine.' })).toBe('Gold with wine.')
  })

  it('names a block for the share title and the toast', () => {
    expect(blockTitle({ type: 'piece', name: 'Silk Slip Dress' })).toBe('Silk Slip Dress')
    expect(blockTitle({ type: 'suggestion' })).toBe('Draft reply')
    expect(blockTitle({ type: 'look' })).toBe('Look')
  })
})

describe('the clients a forward may reach', () => {
  const conversation = (overrides: Partial<ConversationDto>): ConversationDto => ({
    id: 'c',
    kind: 'Salon',
    customerId: null,
    customerName: null,
    externalRef: null,
    threadId: 't',
    status: 'Open',
    lastMessageAt: null,
    lastMessagePreview: null,
    lastMessageKind: null,
    lastMessageBlock: null,
    lastMessageAuthor: null,
    lastMessageAgentKey: null,
    markers: [],
    ...overrides,
  })

  it('treats a thread as deliverable when it has a client or a channel handle', () => {
    expect(isDeliverable(conversation({ customerId: 'u1' }))).toBe(true)
    // An inbound thread whose number is not on file still has an address to send to.
    expect(isDeliverable(conversation({ externalRef: '94771234567' }))).toBe(true)
    // The concierge has neither, so nothing forwarded to it could reach a customer.
    expect(isDeliverable(conversation({}))).toBe(false)
  })

  it('excludes the thread the block is already in', () => {
    const targets = deliveryTargetsFrom(
      [conversation({ id: 'open' }), conversation({ id: 'other', customerId: 'u1', customerName: 'Nadia' })],
      'open',
    )
    expect(targets.map((target) => target.id)).toEqual(['other'])
    expect(targets[0].label).toBe('Nadia')
  })

  it('offers no destination that could not actually receive a delivery', () => {
    const targets = deliveryTargetsFrom(
      [conversation({ id: 'concierge' }), conversation({ id: 'client', customerId: 'u1', customerName: 'Ravi' })],
      'open',
    )
    expect(targets.map((target) => target.id)).toEqual(['client'])
  })

  it('carries the last word\'s time so the picker reads like the inbox', () => {
    const targets = deliveryTargetsFrom(
      [
        conversation({
          id: 'client',
          customerId: 'u1',
          customerName: 'Ravi',
          lastMessageAt: '2026-09-20T10:00:00Z',
        }),
      ],
      null,
    )
    expect(targets).toEqual([
      { id: 'client', label: 'Ravi', lastMessageAt: '2026-09-20T10:00:00Z' },
    ])
  })
})
