import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'

import { ErrorState } from './ErrorState'

describe('ErrorState', () => {
  it('renders the server message and offers a retry', async () => {
    const onRetry = vi.fn()
    render(<ErrorState error={{ status: 503, message: 'Service unavailable' }} onRetry={onRetry} />)
    expect(screen.getByText(/service unavailable/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /retry/i }))
    expect(onRetry).toHaveBeenCalledTimes(1)
  })

  it('renders the traceId with a copy button on a 500', async () => {
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, 'clipboard', {
      configurable: true,
      value: { writeText },
    })

    render(<ErrorState error={{ status: 500, message: 'Internal error', traceId: 'trace-42' }} />)
    expect(screen.getByText(/trace-42/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /copy/i }))
    expect(writeText).toHaveBeenCalledWith('trace-42')
  })

  it('renders no trace handle when the proxy stripped it', () => {
    render(<ErrorState error={{ status: 500, message: 'Internal error' }} />)
    expect(screen.queryByRole('button', { name: /copy/i })).toBeNull()
  })
})
