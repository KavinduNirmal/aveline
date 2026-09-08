import { describe, expect, it } from 'vitest'
import { renderToString } from 'react-dom/server'

import { AvelineAvatar } from './AvelineAvatar'

describe('AvelineAvatar', () => {
  it('renders the blossom without a background circle', () => {
    const html = renderToString(<AvelineAvatar />)
    expect(html).toContain('aveline-waiting')
    expect(html).not.toContain('bg-primary')
  })

  it('applies the colour-cycle class in the idle state', () => {
    const html = renderToString(<AvelineAvatar state="idle" />)
    expect(html).toContain('aveline-waiting')
  })
})
