import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const analyzeProductImageMock = vi.hoisted(() => vi.fn())
const uploadBase64ImageMock = vi.hoisted(() => vi.fn())
const compressAndResizeImageMock = vi.hoisted(() => vi.fn())
const extractVisualAttributesAndColorMock = vi.hoisted(() => vi.fn())

vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() },
}))

vi.mock('@/lib/catalog-api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/catalog-api')>()
  return {
    ...actual,
    analyzeProductImage: (...args: unknown[]) => analyzeProductImageMock(...args),
    uploadBase64Image: (...args: unknown[]) => uploadBase64ImageMock(...args),
  }
})

vi.mock('@/lib/image-optimizer', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/image-optimizer')>()
  return {
    ...actual,
    // Canvas resizing is the browser's job; the drawer only needs a deterministic data URL from it.
    compressAndResizeImage: (...args: unknown[]) => compressAndResizeImageMock(...args),
  }
})

// `color-extractor` is owned by another change in this working tree, and the drawer consumes only
// `extractVisualAttributesAndColor` from it. Replace it wholesale instead of executing the real
// module, so this file pins the drawer's targeting decision and not the extractor's pixel maths.
vi.mock('@/lib/color-extractor', () => ({
  extractVisualAttributesAndColor: (...args: unknown[]) => extractVisualAttributesAndColorMock(...args),
}))

import { AddProductModal } from './AddProductModal'
import type { InventoryItemMock } from './mockData'

const ORG = '11111111-1111-1111-1111-111111111111'
const DATA_URL = 'data:image/jpeg;base64,AAAA'
const UPLOADED_ID = 'img-1'
const RELATIVE_URL = `/api/v1/orgs/${ORG}/catalog/images/${UPLOADED_ID}`

function clientAnalysis(overrides: Record<string, unknown> = {}) {
  return {
    category: 'Sarees',
    garmentType: 'Client Silk Saree',
    suggestedItemName: 'Client Extracted Saree',
    colorName: 'Emerald Green',
    hex: '#0f5132',
    fabric: 'Client Silk',
    pattern: 'Client Zari',
    style: 'Client Style',
    description: 'Client description',
    stylingNotes: 'Client styling',
    confidenceScore: 0.72,
    visualAttributes: ['Client Silk'],
    ...overrides,
  }
}

function liveBackendAnalysis(overrides: Record<string, unknown> = {}) {
  return {
    category: 'Sarees',
    detectedColor: 'Emerald Green',
    colorHex: '#0f5132',
    fabric: 'Mulberry Silk',
    style: 'Traditional Heirloom',
    pattern: 'Gold Zari Brocade',
    confidenceScore: 0.96,
    isFallback: false,
    visualAttributes: ['Mulberry Silk'],
    summary: 'A real analysis',
    description: 'A real analysis',
    stylingNotes: 'Pair with gold.',
    ...overrides,
  }
}

function item(overrides: Partial<InventoryItemMock> = {}): InventoryItemMock {
  return {
    id: 'item-1',
    name: 'Royal Emerald Silk Saree',
    sku: 'AVL-851',
    category: 'Sarees',
    color: 'Emerald Green',
    colorHex: '#046307',
    fabric: 'Pure Mulberry Silk',
    style: 'Zari Brocade',
    sizes: ['38', '40'],
    price: 1250,
    cost: 550,
    stockQuantity: 4,
    status: 'available',
    imageUrl: 'https://example.test/saree.jpg',
    description: 'Handwoven.',
    createdAt: '2026-09-21T09:00:00Z',
    ...overrides,
  }
}

const noop = () => {}

function renderDrawer(overrides: Partial<React.ComponentProps<typeof AddProductModal>> = {}) {
  return render(
    <AddProductModal
      open
      organizationId="11111111-1111-1111-1111-111111111111"
      onClose={noop}
      onSave={noop}
      {...overrides}
    />,
  )
}

/**
 * jsdom cannot point the hidden input at the OS picker, so the chosen file is defined directly and
 * the change event is fired the way the browser would fire it.
 */
function pickFile(file: File) {
  const input = screen.getByLabelText(/choose a garment photograph/i)
  Object.defineProperty(input, 'files', { value: [file], writable: true, configurable: true })
  fireEvent.change(input)
}

describe('the add/edit piece drawer', () => {
  it('opens as a right-hand side drawer, not a centred modal', () => {
    // The piece form is long; a centred dialog fought the photograph, the extracted attributes and
    // the floor tag for the same small box. `right-0` is the shadcn sheet's right side; a centred
    // modal would be `inset-x-0`.
    renderDrawer()

    const dialog = screen.getByRole('dialog')
    expect(dialog).toHaveClass('right-0')
    expect(dialog).not.toHaveClass('inset-x-0')
  })

  it('titles itself "Add a piece" for a new piece and "Edit piece" for an existing one', () => {
    const { unmount } = renderDrawer()
    expect(screen.getByRole('heading', { name: /add a piece/i })).toBeInTheDocument()
    unmount()

    renderDrawer({ editingItem: item() })
    expect(screen.getByRole('heading', { name: /edit piece/i })).toBeInTheDocument()
  })

  it('offers both photograph sources through one labelled control', () => {
    renderDrawer()

    expect(screen.getByRole('radiogroup', { name: /photograph source/i })).toBeInTheDocument()
    expect(screen.getByRole('radio', { name: /upload file/i })).toBeChecked()
    expect(screen.getByRole('radio', { name: /image url/i })).toBeInTheDocument()
  })

  it('does not quote the piece in dollars anywhere in the form', () => {
    // The floor tag inside the drawer prices the piece through the shared formatter; a bare `$`
    // would quote it in a currency the server never sent.
    renderDrawer({ editingItem: item() })

    expect(screen.queryByText(/\$/)).not.toBeInTheDocument()
  })

  it('submits the piece the operator typed', async () => {
    const onSave = vi.fn()
    renderDrawer({ onSave })

    await userEvent.type(screen.getByLabelText(/item name/i), 'Ivory Kanjeevaram')
    await userEvent.click(screen.getByRole('button', { name: /add to catalog/i }))

    expect(onSave).toHaveBeenCalledTimes(1)
    expect(onSave.mock.calls[0][0]).toMatchObject({ name: 'Ivory Kanjeevaram' })
  })

  it('saves no colour hex for a blank new piece, rather than seeding the palette default', async () => {
    const onSave = vi.fn()
    renderDrawer({ onSave })

    await userEvent.type(screen.getByLabelText(/item name/i), 'Unmeasured Piece')
    await userEvent.click(screen.getByRole('button', { name: /add to catalog/i }))

    expect(onSave).toHaveBeenCalledTimes(1)
    const saved = onSave.mock.calls[0][0] as { colorHex?: string }
    // The swatch control renders `colorHex || DEFAULT_COLOR_HEX` so it always has something to
    // paint, but nothing was measured here, so nothing may be stored. The drawer used to seed the
    // state with that literal, which saved a dark green against a piece nobody analysed.
    expect(saved.colorHex).not.toBe('#0f5132')
    expect(saved.colorHex).toBeFalsy()
  })

  describe('how the drawer addresses a re-analysis', () => {
    beforeEach(() => {
      analyzeProductImageMock.mockReset()
      uploadBase64ImageMock.mockReset()
      compressAndResizeImageMock.mockReset()
      extractVisualAttributesAndColorMock.mockReset()

      compressAndResizeImageMock.mockResolvedValue(DATA_URL)
      analyzeProductImageMock.mockResolvedValue(liveBackendAnalysis())
      uploadBase64ImageMock.mockResolvedValue({
        id: UPLOADED_ID,
        url: RELATIVE_URL,
        fileName: 'emerald_saree.jpg',
      })
      extractVisualAttributesAndColorMock.mockResolvedValue(clientAnalysis())
    })

    it('re-analyses a stored upload by reference, never by its relative url', async () => {
      const user = userEvent.setup()
      renderDrawer()

      pickFile(new File(['fake-bytes'], 'emerald_saree.jpg', { type: 'image/jpeg' }))

      // The upload swaps the local data URL for the stored relative path and remembers the row id.
      await waitFor(() => expect(uploadBase64ImageMock).toHaveBeenCalledTimes(1))
      await waitFor(() =>
        expect(screen.getByRole('button', { name: /re-analyze/i })).toBeEnabled(),
      )

      analyzeProductImageMock.mockClear()
      await user.click(screen.getByRole('button', { name: /re-analyze/i }))

      await waitFor(() => expect(analyzeProductImageMock).toHaveBeenCalledTimes(1))
      expect(analyzeProductImageMock).toHaveBeenCalledWith(
        ORG,
        '',
        'emerald_saree.jpg',
        undefined,
        UPLOADED_ID,
      )
      // The vision provider cannot read a relative path; posting one only buys a fabricated answer.
      for (const call of analyzeProductImageMock.mock.calls) {
        expect(call[1]).not.toBe(RELATIVE_URL)
      }
    })

    it('analyses the compressed data url before upload, because that pass reads real bytes', async () => {
      renderDrawer()

      pickFile(new File(['fake-bytes'], 'emerald_saree.jpg', { type: 'image/jpeg' }))

      await waitFor(() => expect(analyzeProductImageMock).toHaveBeenCalledTimes(1))
      expect(analyzeProductImageMock).toHaveBeenCalledWith(ORG, DATA_URL, 'emerald_saree.jpg')
      // The row id does not exist until the upload returns, so the pre-upload pass cannot
      // reference one and must address the data URL directly.
      expect(analyzeProductImageMock.mock.calls[0][4]).toBeUndefined()
    })

    it('refuses a relative path with no remembered id, falling back to the client extraction', async () => {
      const user = userEvent.setup()
      renderDrawer({ editingItem: item({ imageUrl: RELATIVE_URL }) })

      await user.click(screen.getByRole('button', { name: /re-analyze/i }))

      await waitFor(() =>
        expect(screen.getByLabelText(/item name/i)).toHaveValue('Client Extracted Saree'),
      )
      expect(extractVisualAttributesAndColorMock).toHaveBeenCalledWith(RELATIVE_URL, undefined)
      expect(analyzeProductImageMock).not.toHaveBeenCalled()
    })

    it('posts an absolute http(s) url when there is no stored upload to reference', async () => {
      const user = userEvent.setup()
      renderDrawer({ editingItem: item({ imageUrl: 'https://example.test/saree.jpg' }) })

      await user.click(screen.getByRole('button', { name: /re-analyze/i }))

      await waitFor(() => expect(analyzeProductImageMock).toHaveBeenCalledTimes(1))
      expect(analyzeProductImageMock).toHaveBeenCalledWith(
        ORG,
        'https://example.test/saree.jpg',
        undefined,
      )
    })

    it('forgets the stored upload id when the photograph is cleared', async () => {
      const user = userEvent.setup()
      renderDrawer()

      pickFile(new File(['fake-bytes'], 'emerald_saree.jpg', { type: 'image/jpeg' }))
      await waitFor(() => expect(uploadBase64ImageMock).toHaveBeenCalledTimes(1))
      analyzeProductImageMock.mockClear()

      await user.click(screen.getByRole('button', { name: /remove photograph/i }))
      // Switch to URL mode and paste a relative path. A stale id here would silently analyse the
      // image the operator just removed.
      await user.click(screen.getByRole('radio', { name: /image url/i }))
      await user.type(screen.getByPlaceholderText(/image url/i), RELATIVE_URL)
      await user.click(screen.getByRole('button', { name: /extract with vision ai/i }))

      await waitFor(() =>
        expect(extractVisualAttributesAndColorMock).toHaveBeenCalledWith(RELATIVE_URL, undefined),
      )
      expect(analyzeProductImageMock).not.toHaveBeenCalled()
    })
  })

  describe('the colour a live analysis writes into the form', () => {
    beforeEach(() => {
      analyzeProductImageMock.mockReset()
      uploadBase64ImageMock.mockReset()
      compressAndResizeImageMock.mockReset()
      extractVisualAttributesAndColorMock.mockReset()

      compressAndResizeImageMock.mockResolvedValue(DATA_URL)
      uploadBase64ImageMock.mockResolvedValue({
        id: UPLOADED_ID,
        url: RELATIVE_URL,
        fileName: 'dress.jpg',
      })
    })

    it('writes the live backend colour, even when the client extraction disagrees', async () => {
      const user = userEvent.setup()
      analyzeProductImageMock.mockResolvedValue(
        liveBackendAnalysis({ detectedColor: 'Fuchsia Magenta', colorHex: '#ff00ff' }),
      )
      // The client pixel pass guessed green; the model measured fuchsia. The model's answer wins.
      extractVisualAttributesAndColorMock.mockResolvedValue(
        clientAnalysis({ colorName: 'Emerald Green', hex: '#0f5132' }),
      )
      renderDrawer({ editingItem: item({ imageUrl: 'https://example.test/dress.jpg' }) })

      await user.click(screen.getByRole('button', { name: /re-analyze/i }))

      await waitFor(() =>
        expect(screen.getByLabelText(/colour name/i)).toHaveValue('Fuchsia Magenta'),
      )
      expect(screen.getByLabelText(/colour name/i)).not.toHaveValue('Emerald Green')
    })

    it('leaves the colour empty when a live result carries none, rather than inventing Emerald Green', async () => {
      const user = userEvent.setup()
      analyzeProductImageMock.mockResolvedValue(
        liveBackendAnalysis({ detectedColor: undefined, colorHex: undefined }),
      )
      // No client measurement to fall back on: the form must say nothing rather than a default.
      extractVisualAttributesAndColorMock.mockResolvedValue(null)
      renderDrawer({ editingItem: item({ imageUrl: 'https://example.test/dress.jpg' }) })

      await user.click(screen.getByRole('button', { name: /re-analyze/i }))

      await waitFor(() => expect(analyzeProductImageMock).toHaveBeenCalledTimes(1))
      await waitFor(() => expect(screen.getByLabelText(/colour name/i)).toHaveValue(''))
      expect(screen.getByLabelText(/colour name/i)).not.toHaveValue('Emerald Green')
    })
    it('does not invent a fabric, a pattern or a description the live result omitted', async () => {
      const user = userEvent.setup()
      analyzeProductImageMock.mockResolvedValue(
        liveBackendAnalysis({
          detectedColor: 'Fuchsia Magenta',
          fabric: undefined,
          pattern: undefined,
          style: undefined,
          description: undefined,
        }),
      )
      // No client measurement either: every attribute has to stay empty rather than become silk.
      extractVisualAttributesAndColorMock.mockResolvedValue(null)
      renderDrawer({ editingItem: item({ imageUrl: 'https://example.test/dress.jpg' }) })

      await user.click(screen.getByRole('button', { name: /re-analyze/i }))

      await waitFor(() => expect(analyzeProductImageMock).toHaveBeenCalledTimes(1))
      expect(screen.getByLabelText(/detected fabric/i)).toHaveValue('')
      expect(screen.getByLabelText(/style and pattern/i)).toHaveValue('')
    })

    it('carries the analysed hex onto the piece it saves, rather than dropping it', async () => {
      const user = userEvent.setup()
      const onSave = vi.fn()
      analyzeProductImageMock.mockResolvedValue(
        liveBackendAnalysis({ detectedColor: 'Fuchsia Pink', colorHex: '#D5006D' }),
      )
      // No client pixel measurement, so the model's hex is the only measurement in play.
      extractVisualAttributesAndColorMock.mockResolvedValue(null)
      renderDrawer({ editingItem: item({ imageUrl: 'https://example.test/dress.jpg' }), onSave })

      await user.click(screen.getByRole('button', { name: /re-analyze/i }))
      await waitFor(() =>
        expect(screen.getByLabelText(/colour name/i)).toHaveValue('Fuchsia Pink'),
      )

      await user.click(screen.getByRole('button', { name: /save changes/i }))

      await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1))
      // The drawer lower-cases the hex for the HTML colour control; `#d5006d` is the same colour.
      expect(onSave.mock.calls[0][0]).toMatchObject({ colorHex: '#d5006d' })
    })

    it('saves no hex when neither the model nor the pixels measured one, never the palette default', async () => {
      const user = userEvent.setup()
      const onSave = vi.fn()
      // The model named a colour but returned no hex; the client extraction sampled nothing.
      analyzeProductImageMock.mockResolvedValue(
        liveBackendAnalysis({ detectedColor: 'Fuchsia Pink', colorHex: undefined }),
      )
      extractVisualAttributesAndColorMock.mockResolvedValue(null)
      renderDrawer({ editingItem: item({ imageUrl: 'https://example.test/dress.jpg' }), onSave })

      await user.click(screen.getByRole('button', { name: /re-analyze/i }))
      await waitFor(() =>
        expect(screen.getByLabelText(/colour name/i)).toHaveValue('Fuchsia Pink'),
      )

      await user.click(screen.getByRole('button', { name: /save changes/i }))

      await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1))
      const saved = onSave.mock.calls[0][0] as { colorHex?: string }
      // `#0f5132` (DEFAULT_COLOR_HEX) is the palette default the drawer used to derive from a
      // colour *name*. A name is not a measurement, so nothing about the colour is saved.
      expect(saved.colorHex).not.toBe('#0f5132')
      expect(saved.colorHex).toBeFalsy()
    })
  })
})
