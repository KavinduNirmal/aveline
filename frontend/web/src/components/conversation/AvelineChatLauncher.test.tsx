import { describe, expect, it, vi } from 'vitest'
import { renderToString } from 'react-dom/server'

import { AvelineChatLauncher } from './AvelineChatLauncher'

const { useConversationsMock } = vi.hoisted(() => ({
  useConversationsMock: vi.fn(),
}))

vi.mock('@/contexts/ConversationsContext', () => ({
  useConversations: () => useConversationsMock(),
}))

describe('AvelineChatLauncher', () => {
  it('renders a button with the Aveline label', () => {
    useConversationsMock.mockReturnValue({ waiting: false })
    const html = renderToString(<AvelineChatLauncher open={false} onOpen={() => undefined} />)
    expect(html).toContain('Open Aveline chat')
  })

  it('always applies the animated blossom class (main CTA)', () => {
    useConversationsMock.mockReturnValue({ waiting: false })
    const html = renderToString(<AvelineChatLauncher open={false} onOpen={() => undefined} />)
    expect(html).toContain('aveline-waiting')
  })

  it('marks the button active when the chat is open', () => {
    useConversationsMock.mockReturnValue({ waiting: false })
    const html = renderToString(<AvelineChatLauncher open onOpen={() => undefined} />)
    expect(html).toContain('Aveline chat is open')
  })
})
