import { describe, expect, it, vi } from 'vitest'
import { renderToString } from 'react-dom/server'

import { Composer } from './Composer'

describe('Composer', () => {
  it('renders a textarea and send button', () => {
    const html = renderToString(<Composer onSend={() => undefined} />)
    expect(html).toContain('Message Aveline')
    expect(html).toContain('Send message')
  })

  it('renders a custom placeholder', () => {
    const html = renderToString(<Composer onSend={() => undefined} placeholder="Ask Aveline…" />)
    expect(html).toContain('Ask Aveline')
  })

  it('disables the send button when disabled', () => {
    const html = renderToString(<Composer onSend={() => undefined} disabled />)
    expect(html).toContain('disabled')
  })

  it('does not call onSend when there is no text', () => {
    const onSend = vi.fn()
    renderToString(<Composer onSend={onSend} />)
    expect(onSend).not.toHaveBeenCalled()
  })
})
