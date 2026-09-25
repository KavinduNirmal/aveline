import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/lib/api-error'

const startMock = vi.fn()
const verifyMock = vi.fn()

vi.mock('@/lib/privacy', async () => {
  const actual = await vi.importActual<typeof import('@/lib/privacy')>('@/lib/privacy')
  return {
    ...actual,
    startOptOut: (...args: unknown[]) => startMock(...args),
    verifyOptOut: (...args: unknown[]) => verifyMock(...args),
  }
})

vi.mock('@clerk/react', () => ({
  useAuth: () => ({ isLoaded: true, isSignedIn: false }),
}))

import { OptOutPage } from './OptOutPage'

const ORG = '9f1c1111-1111-4111-8111-111111111111'
const VALID_LINK = `/privacy/opt-out?o=${ORG}&v=1&s=base64url-hmac`

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/privacy/opt-out" element={<OptOutPage />} />
        <Route path="/privacy" element={<div>POLICY</div>} />
        <Route path="*" element={<div>OTHER</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  startMock
    .mockReset()
    .mockResolvedValue({ status: 'accepted', handle: 'opaque-handle-1', expiresInSeconds: 300 })
  verifyMock
    .mockReset()
    .mockResolvedValue({ status: 'revoked', scope: 'org', effectiveAtUtc: '2026-09-25T10:15:00Z' })
})

afterEach(() => vi.clearAllMocks())

describe('OptOutPage — the signed link', () => {
  it('refuses a page opened without the signed link values', () => {
    renderAt('/privacy/opt-out')

    expect(
      screen.getByRole('heading', { level: 1, name: /link is not valid/i }),
    ).toBeInTheDocument()
    expect(screen.queryByLabelText(/whatsapp number/i)).not.toBeInTheDocument()
    expect(startMock).not.toHaveBeenCalled()
  })

  it('refuses a tampered organization id before it reaches the server', () => {
    renderAt('/privacy/opt-out?o=not-an-org&v=1&s=abc')

    expect(
      screen.getByRole('heading', { level: 1, name: /link is not valid/i }),
    ).toBeInTheDocument()
    expect(startMock).not.toHaveBeenCalled()
  })

  it('accepts the link the disclosure builds', () => {
    renderAt(VALID_LINK)

    expect(screen.getByLabelText(/whatsapp number/i)).toBeInTheDocument()
  })
})

describe('OptOutPage — the phone and the scope', () => {
  it('labels every input and offers both scopes as keyboard-navigable radios', async () => {
    renderAt(VALID_LINK)

    const phone = screen.getByLabelText(/whatsapp number/i)
    expect(phone).toHaveAttribute('type', 'tel')
    expect(phone).toHaveAttribute('autocomplete', 'tel')

    const scope = screen.getByRole('radiogroup', { name: /what should we stop/i })
    expect(within(scope).getAllByRole('radio')).toHaveLength(2)
    expect(within(scope).getByRole('radio', { name: /this boutique only/i })).toBeChecked()
  })

  it('does not call the API for an empty number', async () => {
    const user = userEvent.setup()
    renderAt(VALID_LINK)

    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    expect(startMock).not.toHaveBeenCalled()
    expect(screen.getByRole('alert')).toHaveTextContent(/enter your whatsapp number/i)
  })

  it('starts the opt-out with the typed number and the chosen global scope', async () => {
    const user = userEvent.setup()
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(
      within(screen.getByRole('radiogroup')).getByRole('radio', {
        name: /every aveline boutique/i,
      }),
    )
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    await waitFor(() => expect(startMock).toHaveBeenCalledTimes(1))
    expect(startMock.mock.calls[0][1]).toBe('0771234567')
    expect(startMock.mock.calls[0][2]).toBe('all')
  })
})

describe('OptOutPage — anti-enumeration', () => {
  it('always moves to the code step with the same neutral copy after start', async () => {
    const user = userEvent.setup()
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    // The start call cannot reveal whether the number exists, and neither must the page.
    expect(await screen.findByText(/if that number is registered/i)).toBeInTheDocument()
    expect(screen.queryByText(/not registered/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/no record/i)).not.toBeInTheDocument()
  })
})

describe('OptOutPage — the code and the terminal state', () => {
  it('verifies with the handle start returned and lands on the revoked state', async () => {
    const user = userEvent.setup()
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    const code = await screen.findByLabelText(/six-digit code/i)
    expect(code).toHaveAttribute('inputmode', 'numeric')
    // The lifetime is the server's, not a hardcoded five minutes.
    expect(screen.getByText(/expires in 5 minutes/i)).toBeInTheDocument()
    await user.type(code, '123456')
    await user.click(screen.getByRole('button', { name: /confirm opt-out/i }))

    await waitFor(() => expect(verifyMock).toHaveBeenCalledTimes(1))
    expect(verifyMock.mock.calls[0][0]).toMatchObject({
      handle: 'opaque-handle-1',
      otp: '123456',
      phoneNumber: '0771234567',
      scope: 'org',
    })

    expect(
      await screen.findByRole('heading', { level: 1, name: /you have opted out/i }),
    ).toBeInTheDocument()
    expect(screen.getByText(/this boutique will not process/i)).toBeInTheDocument()
  })

  it('says the opt-out covers every boutique when the global scope was chosen', async () => {
    const user = userEvent.setup()
    verifyMock.mockResolvedValue({
      status: 'revoked',
      scope: 'all',
      effectiveAtUtc: '2026-09-25T10:15:00Z',
    })
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(
      within(screen.getByRole('radiogroup')).getByRole('radio', {
        name: /every aveline boutique/i,
      }),
    )
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    const code = await screen.findByLabelText(/six-digit code/i)
    await user.type(code, '123456')
    await user.click(screen.getByRole('button', { name: /confirm opt-out/i }))

    expect(await screen.findByText(/every aveline boutique holding your number/i)).toBeInTheDocument()
  })

  it('keeps the customer on the code step and explains an invalid or expired code', async () => {
    const user = userEvent.setup()
    verifyMock.mockRejectedValue(new ApiError(400, 'bad', 'otp-invalid'))
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    const code = await screen.findByLabelText(/six-digit code/i)
    await user.type(code, '000000')
    await user.click(screen.getByRole('button', { name: /confirm opt-out/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(
      /not valid, has expired, or was already used/i,
    )
    expect(screen.getByLabelText(/six-digit code/i)).toBeInTheDocument()
    expect(
      screen.queryByRole('heading', { level: 1, name: /you have opted out/i }),
    ).not.toBeInTheDocument()
  })

  it('reports an outage without pretending the code was wrong', async () => {
    const user = userEvent.setup()
    verifyMock.mockRejectedValue(new ApiError(503, 'down'))
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    const code = await screen.findByLabelText(/six-digit code/i)
    await user.type(code, '123456')
    await user.click(screen.getByRole('button', { name: /confirm opt-out/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/temporarily unavailable/i)
  })

  it('lets the customer start over from the code step', async () => {
    const user = userEvent.setup()
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    await user.click(await screen.findByRole('button', { name: /start over/i }))

    expect(screen.getByLabelText(/whatsapp number/i)).toBeInTheDocument()
  })
})

describe('OptOutPage — a start the page cannot use', () => {
  it('degrades to a generic failure when start returns no handle, rather than collecting a dead code', async () => {
    // The endpoint mints the handle unconditionally, so this is a regression path, not a normal
    // state. It must fail visibly instead of collecting a code the page can never redeem.
    const user = userEvent.setup()
    startMock.mockResolvedValue({ status: 'accepted', handle: null, expiresInSeconds: null })
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    expect(
      await screen.findByRole('heading', {
        level: 1,
        name: /could not start the opt-out/i,
      }),
    ).toBeInTheDocument()
    expect(screen.queryByLabelText(/six-digit code/i)).not.toBeInTheDocument()
  })

  it('shows the server reason when start is refused, and stays on the form', async () => {
    const user = userEvent.setup()
    startMock.mockRejectedValue(new ApiError(429, 'slow down', 'otp-rate-limited'))
    renderAt(VALID_LINK)

    await user.type(screen.getByLabelText(/whatsapp number/i), '0771234567')
    await user.click(screen.getByRole('button', { name: /send me a code/i }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/too many/i)
    expect(screen.getByLabelText(/whatsapp number/i)).toBeInTheDocument()
  })
})
