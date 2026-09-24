import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { ConversationDto } from '@/types/conversation'
import type { ContentBlock } from './blocks'
import { BlockList } from './blocks'
import { useBlockActions, type UseBlockActionsOptions } from './useBlockActions'

const toastMocks = vi.hoisted(() => ({ success: vi.fn(), error: vi.fn() }))

vi.mock('sonner', () => ({
  toast: { success: toastMocks.success, error: toastMocks.error },
}))

/** One Salon row, as the forward picker reads it. */
function conversation(overrides: Partial<ConversationDto> = {}): ConversationDto {
  return {
    id: 'conv-open',
    kind: 'Salon',
    customerId: null,
    customerName: null,
    externalRef: null,
    threadId: 'thread-1',
    status: 'Active',
    lastMessageAt: null,
    lastMessagePreview: null,
    lastMessageKind: null,
    lastMessageBlock: null,
    lastMessageAuthor: null,
    lastMessageAgentKey: null,
    markers: [],
    ...overrides,
  }
}

const OPEN_THREAD = conversation({
  id: 'conv-open',
  customerId: 'u-1',
  customerName: 'Nadia Perera',
})
const OTHER_THREAD = conversation({
  id: 'conv-other',
  customerId: 'u-2',
  customerName: 'Ravi Silva',
})
/** The client-less concierge: a thread, but not a delivery destination. */
const CONCIERGE = conversation({ id: 'conv-concierge' })

/** The options every case starts from; a case overrides only what it is about. */
function options(overrides: Partial<UseBlockActionsOptions> = {}): UseBlockActionsOptions {
  return {
    conversationId: 'conv-open',
    customerName: 'Nadia Perera',
    customerReachable: true,
    conversations: [OPEN_THREAD, OTHER_THREAD, CONCIERGE],
    deliver: vi.fn().mockResolvedValue(undefined),
    regenerate: vi.fn().mockResolvedValue(undefined),
    agentBusy: false,
    ...overrides,
  }
}

const SUGGESTION: ContentBlock = { type: 'suggestion', text: 'We have it in size M.' }
/** The lookbook rail is the one that carries Copy, Forward and Regenerate. */
const LOOK: ContentBlock = { type: 'look', name: 'Gala Ensemble', text: 'Gold with wine.' }

/** Mounts the rail and its dialogs exactly as a Salon surface does. */
function Harness({
  options: config,
  block = SUGGESTION,
  messageId = 'msg-1',
}: {
  options: UseBlockActionsOptions
  block?: ContentBlock
  messageId?: string
}) {
  const { bridge, dialogs } = useBlockActions(config)
  return (
    <>
      <BlockList blocks={[block]} messageId={messageId} bridge={bridge} />
      {dialogs}
    </>
  )
}

const writeText = vi.fn<(text: string) => Promise<void>>()

/** jsdom has no clipboard; each case decides what the platform offers. */
function stubClipboard() {
  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: { writeText },
  })
}

beforeEach(() => {
  writeText.mockReset().mockResolvedValue(undefined)
  toastMocks.success.mockReset()
  toastMocks.error.mockReset()
  stubClipboard()
})

afterEach(() => {
  vi.restoreAllMocks()
})

describe('Copy', () => {
  it('puts the block\'s own words on the clipboard and says so', async () => {
    render(<Harness options={options()} />)

    await userEvent.click(screen.getByRole('button', { name: 'Copy' }))

    await waitFor(() => expect(writeText).toHaveBeenCalledWith('We have it in size M.'))
    expect(toastMocks.success).toHaveBeenCalledWith('Draft reply copied')
  })

  it('reports a refused clipboard rather than swallowing it', async () => {
    writeText.mockRejectedValue(new Error('denied'))
    render(<Harness options={options()} />)

    await userEvent.click(screen.getByRole('button', { name: 'Copy' }))

    await waitFor(() =>
      expect(toastMocks.error).toHaveBeenCalledWith('That could not be copied to the clipboard.'),
    )
  })

  it('falls back to a selection when the platform has no async clipboard', async () => {
    // A boutique reaching the dashboard on a plain-HTTP LAN host has no `navigator.clipboard`.
    Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })
    const execCommand = vi.fn().mockReturnValue(true)
    Object.defineProperty(document, 'execCommand', { configurable: true, value: execCommand })

    render(<Harness options={options()} />)
    await userEvent.click(screen.getByRole('button', { name: 'Copy' }))

    await waitFor(() => expect(execCommand).toHaveBeenCalledWith('copy'))
  })
})

describe('Send to customer', () => {
  it('delivers to the open thread\'s client, after naming them and quoting the words', async () => {
    const config = options()
    render(<Harness options={config} />)

    await userEvent.click(screen.getByRole('button', { name: 'Send to customer' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Send to Nadia Perera?')
    expect(dialog).toHaveTextContent('We have it in size M.')
    // Nothing has gone anywhere until the confirmation is accepted.
    expect(config.deliver).not.toHaveBeenCalled()

    await userEvent.click(
      within(dialog).getByRole('button', { name: 'Send to customer' }),
    )

    await waitFor(() => expect(config.deliver).toHaveBeenCalledWith('conv-open', 'We have it in size M.'))
    expect(toastMocks.success).toHaveBeenCalledWith('Draft reply sent')
  })

  it('delivers nothing when the confirmation is dismissed', async () => {
    const config = options()
    render(<Harness options={config} />)

    await userEvent.click(screen.getByRole('button', { name: 'Send to customer' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Cancel' }))

    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(config.deliver).not.toHaveBeenCalled()
  })

  it('is refused, with its reason, on a thread that is not linked to a client', async () => {
    const config = options({ conversationId: null, customerName: null, customerReachable: false })
    render(<Harness options={config} />)

    const send = document.querySelector('[data-action="send_to_customer"]')!
    expect(send).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(send)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(config.deliver).not.toHaveBeenCalled()
  })

  it('shows the server\'s own sentence when the channel refuses', async () => {
    // "You have not connected WhatsApp" and "that client has no number" are different things for
    // the associate to do next, so the server's sentence is preferred over one generic failure.
    const config = options({
      deliver: vi.fn().mockRejectedValue(new Error('This boutique has not connected WhatsApp yet.')),
    })
    render(<Harness options={config} />)

    await userEvent.click(screen.getByRole('button', { name: 'Send to customer' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: 'Send to customer' }))

    await waitFor(() =>
      expect(toastMocks.error).toHaveBeenCalledWith(
        'This boutique has not connected WhatsApp yet.',
      ),
    )
    // The dialog stays open so the associate can see what they were about to send.
    expect(screen.getByRole('dialog')).toBeInTheDocument()
    expect(toastMocks.success).not.toHaveBeenCalled()
  })
})

describe('Forward', () => {
  it('offers only clients a delivery can actually reach', async () => {
    render(<Harness options={options()} block={LOOK} />)

    await userEvent.click(screen.getByRole('button', { name: 'Forward' }))

    const dialog = await screen.findByRole('dialog')
    expect(dialog).toHaveTextContent('Gala Ensemble — Gold with wine.')
    expect(within(dialog).getByRole('button', { name: /Ravi Silva/ })).toBeInTheDocument()
    // The open thread is what "Send to customer" is for, and the concierge has no client to
    // deliver to, so neither is a destination.
    expect(within(dialog).queryByRole('button', { name: /Nadia Perera/ })).not.toBeInTheDocument()
    expect(within(dialog).queryByRole('button', { name: /Aveline/ })).not.toBeInTheDocument()
  })

  it('delivers the card to the client that was picked', async () => {
    const config = options()
    render(<Harness options={config} block={LOOK} />)

    await userEvent.click(screen.getByRole('button', { name: 'Forward' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: /Ravi Silva/ }))

    await waitFor(() =>
      expect(config.deliver).toHaveBeenCalledWith('conv-other', 'Gala Ensemble — Gold with wine.'),
    )
    expect(toastMocks.success).toHaveBeenCalledWith('Gala Ensemble sent')
  })

  it('refuses the forward when there is no client to deliver to', async () => {
    const config = options({ conversations: [OPEN_THREAD, CONCIERGE] })
    render(<Harness options={config} block={LOOK} />)

    const forward = document.querySelector('[data-action="forward"]')!
    expect(forward).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(forward)

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(config.deliver).not.toHaveBeenCalled()
  })

  it('keeps the picker open and says nothing was sent when the delivery fails', async () => {
    const config = options({ deliver: vi.fn().mockRejectedValue(new Error('offline')) })
    render(<Harness options={config} block={LOOK} />)

    await userEvent.click(screen.getByRole('button', { name: 'Forward' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: /Ravi Silva/ }))

    await waitFor(() => expect(toastMocks.error).toHaveBeenCalledWith('offline'))
    expect(toastMocks.success).not.toHaveBeenCalled()
    expect(screen.getByRole('dialog')).toBeInTheDocument()
  })
})

describe('Regenerate', () => {
  it('asks the agent for a fresh take on the block\'s own message', async () => {
    const config = options()
    render(<Harness options={config} block={LOOK} messageId="msg-4" />)

    await userEvent.click(screen.getByRole('button', { name: 'Regenerate' }))

    await waitFor(() => expect(config.regenerate).toHaveBeenCalledWith('msg-4'))
    expect(toastMocks.success).toHaveBeenCalledWith('Asking Aveline for a fresh take')
  })

  it('reports a regeneration the server refused', async () => {
    const config = options({ regenerate: vi.fn().mockRejectedValue(new Error('agent down')) })
    render(<Harness options={config} />)

    await userEvent.click(screen.getByRole('button', { name: 'Regenerate' }))

    await waitFor(() =>
      expect(toastMocks.error).toHaveBeenCalledWith('Could not regenerate that draft reply.'),
    )
  })

  it('is refused while Aveline is already answering the thread', async () => {
    const config = options({ agentBusy: true })
    render(<Harness options={config} />)

    const regenerate = document.querySelector('[data-action="regenerate"]')!
    expect(regenerate).toHaveAttribute('aria-disabled', 'true')
    await userEvent.click(regenerate)
    expect(config.regenerate).not.toHaveBeenCalled()
  })
})

describe('one action at a time on a block', () => {
  it('refuses a second delivery while the first is still in flight', async () => {
    // A delivery that never settles keeps the block busy for the rest of the test.
    const deliver = vi.fn().mockImplementation(() => new Promise<void>(() => {}))
    const config = options({ deliver })
    render(<Harness options={config} block={LOOK} />)

    await userEvent.click(screen.getByRole('button', { name: 'Forward' }))
    const dialog = await screen.findByRole('dialog')
    await userEvent.click(within(dialog).getByRole('button', { name: /Ravi Silva/ }))

    await waitFor(() => expect(deliver).toHaveBeenCalledTimes(1))
    // The picker disables every row while one delivery runs, so a second tap cannot message a
    // second client with the same card.
    for (const row of within(screen.getByRole('dialog')).getAllByRole('button')) {
      if (row.textContent?.includes('Ravi')) expect(row).toBeDisabled()
    }
  })
})
