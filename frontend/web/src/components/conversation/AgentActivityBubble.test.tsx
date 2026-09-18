import { describe, expect, it } from 'vitest'
import { renderToString } from 'react-dom/server'

import { AgentActivityBubble } from './AgentActivityBubble'

describe('AgentActivityBubble', () => {
  it('renders the Aveline persona and a state label', () => {
    const html = renderToString(<AgentActivityBubble state="searching" />)
    expect(html).toContain('Aveline')
    expect(html).toContain('Searching…')
  })

  it('reflects the current reasoning state', () => {
    const html = renderToString(<AgentActivityBubble state="tool_call" />)
    expect(html).toContain('Using a tool…')
  })
})
