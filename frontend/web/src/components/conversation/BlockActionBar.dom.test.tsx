import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { BlockActionBar } from './BlockActionBar'
import { resolveBlockActions, type ActionableBlock } from './blockActions'

/** The suggestion rail, fully available, as the bar receives it. */
function suggestionActions(pending: 'regenerate' | null = null) {
  return resolveBlockActions(
    { type: 'suggestion', text: 'We have it in size M.' },
    {
      hasCustomerDestination: true,
      hasForwardDestination: true,
      agentBusy: false,
      pending,
    },
  )
}

describe('the rail is joined to the card, not a row of pills', () => {
  it('draws the segments inside one divided strip', () => {
    render(
      <BlockActionBar actions={suggestionActions()} blockTitle="Draft reply" onAction={() => {}} />,
    )

    const rail = screen.getByRole('toolbar', { name: 'Draft reply actions' })
    expect(rail).toHaveAttribute('data-slot', 'block-action-bar')
    // One hairline above the rail (the card's seam) and one between neighbours; no rounding of
    // its own, so the card's radius is what shapes the rail's bottom corners.
    expect(rail.className).toContain('divide-x')
    expect(rail.className).toContain('border-t')
    expect(rail.className).not.toMatch(/rounded-(full|lg|xl|md|sm)/)
  })

  it('gives every segment a flat edge rather than the app\'s pill radius', () => {
    render(
      <BlockActionBar actions={suggestionActions()} blockTitle="Draft reply" onAction={() => {}} />,
    )

    for (const segment of screen.getAllByRole('button')) {
      // `rounded-full` is the shared Button's pill; a joined rail must not wear it.
      expect(segment.className).not.toContain('rounded-full')
      expect(segment.className).toContain('flex-1')
    }
  })

  it('draws one segment per action, in the order the mapping gives them', () => {
    render(
      <BlockActionBar actions={suggestionActions()} blockTitle="Draft reply" onAction={() => {}} />,
    )

    expect(screen.getAllByRole('button').map((button) => button.getAttribute('data-action'))).toEqual(
      ['copy', 'send_to_customer', 'regenerate'],
    )
  })

  it('renders nothing at all when the block offers no actions', () => {
    const { container } = render(
      <BlockActionBar actions={[]} blockTitle="Piece" onAction={() => {}} />,
    )
    expect(container).toBeEmptyDOMElement()
  })
})

describe('the rail inside a narrow tile row', () => {
  it('drops the printed word but keeps the full accessible name and a tooltip', async () => {
    render(
      <BlockActionBar
        actions={suggestionActions()}
        blockTitle="Gala Ensemble"
        onAction={() => {}}
        compact
      />,
    )

    const copy = screen.getByRole('button', { name: 'Copy' })
    // The glyph carries the segment; the words are still there for anyone who needs them.
    expect(copy).not.toHaveTextContent('Copy')
    expect(copy.querySelector('svg')).not.toBeNull()

    copy.focus()
    expect(await screen.findByRole('tooltip')).toHaveTextContent('Copy')
  })
})

describe('acting through the rail', () => {
  it('reports the action the associate pressed', async () => {
    const onAction = vi.fn()
    render(
      <BlockActionBar actions={suggestionActions()} blockTitle="Draft reply" onAction={onAction} />,
    )

    await userEvent.click(screen.getByRole('button', { name: /copy/i }))
    expect(onAction).toHaveBeenCalledWith('copy')

    await userEvent.click(screen.getByRole('button', { name: /regenerate/i }))
    expect(onAction).toHaveBeenCalledWith('regenerate')
  })

  it('is reachable by keyboard, and Enter presses the focused segment', async () => {
    const onAction = vi.fn()
    render(
      <BlockActionBar actions={suggestionActions()} blockTitle="Draft reply" onAction={onAction} />,
    )

    await userEvent.tab()
    expect(screen.getByRole('button', { name: /copy/i })).toHaveFocus()

    await userEvent.keyboard('{Enter}')
    expect(onAction).toHaveBeenCalledWith('copy')
  })
})

describe('a segment that is waiting on the server', () => {
  it('says it is busy, spins, and refuses a second press', async () => {
    const onAction = vi.fn()
    render(
      <BlockActionBar
        actions={suggestionActions('regenerate')}
        blockTitle="Draft reply"
        onAction={onAction}
      />,
    )

    const busy = screen.getByRole('button', { name: /redoing/i })
    expect(busy).toHaveAttribute('aria-busy', 'true')
    expect(busy.querySelector('svg.animate-spin')).not.toBeNull()

    await userEvent.click(busy)
    expect(onAction).not.toHaveBeenCalled()
  })

  it('disables the block\'s other actions while one runs, so two cannot race the content', async () => {
    const onAction = vi.fn()
    render(
      <BlockActionBar
        actions={suggestionActions('regenerate')}
        blockTitle="Draft reply"
        onAction={onAction}
      />,
    )

    for (const action of ['copy', 'send_to_customer'] as const) {
      const segment = document.querySelector(`[data-action="${action}"]`)!
      expect(segment).toHaveAttribute('aria-disabled', 'true')
      await userEvent.click(segment)
    }
    expect(onAction).not.toHaveBeenCalled()
  })
})

describe('a segment that cannot run here', () => {
  it('stays focusable, so the reason is reachable without a pointer', async () => {
    const actions = resolveBlockActions(
      { type: 'look', name: 'Gala Ensemble' } satisfies ActionableBlock,
      {
        hasCustomerDestination: true,
        hasForwardDestination: false,
        agentBusy: false,
        pending: null,
      },
    )
    render(<BlockActionBar actions={actions} blockTitle="Gala Ensemble" onAction={() => {}} />)

    const forward = document.querySelector('[data-action="forward"]') as HTMLElement
    // `aria-disabled`, not `disabled`: a truly disabled button leaves the tab order and takes its
    // explanation with it.
    expect(forward).toHaveAttribute('aria-disabled', 'true')
    expect(forward).toHaveAttribute('data-state', 'unavailable')
    expect(forward).not.toBeDisabled()

    forward.focus()
    expect(forward).toHaveFocus()
  })

  it('shows the reason as the segment\'s tooltip', async () => {
    const actions = resolveBlockActions(
      { type: 'look', name: 'Gala Ensemble' },
      {
        hasCustomerDestination: true,
        hasForwardDestination: false,
        agentBusy: false,
        pending: null,
      },
    )
    render(<BlockActionBar actions={actions} blockTitle="Gala Ensemble" onAction={() => {}} />)

    const forward = document.querySelector('[data-action="forward"]') as HTMLElement
    forward.focus()

    expect(await screen.findByRole('tooltip')).toHaveTextContent(
      /no other client to forward to yet/i,
    )
  })

  it('does not press a segment whose block offers the action but the thread cannot serve it', async () => {
    const onAction = vi.fn()
    const actions = resolveBlockActions(
      { type: 'look', name: 'Gala Ensemble' },
      {
        hasCustomerDestination: true,
        hasForwardDestination: false,
        agentBusy: false,
        pending: null,
      },
    )
    render(<BlockActionBar actions={actions} blockTitle="Gala Ensemble" onAction={onAction} />)

    await userEvent.click(document.querySelector('[data-action="forward"]')!)
    expect(onAction).not.toHaveBeenCalled()
  })
})
