import { beforeEach, describe, expect, it, vi } from 'vitest'

const compressMock = vi.hoisted(() => vi.fn())

// The resize itself is the browser's job and is exercised by `image-optimizer.test.ts`; these tests
// pin the decisions around it (the cap, the allow-list, the PDF arm) against a controlled data URL.
vi.mock('./image-optimizer', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./image-optimizer')>()
  return { ...actual, compressAndResizeImage: compressMock }
})

import { formatFileSize } from './image-optimizer'
import {
  ATTACHMENT_CAP_MESSAGE,
  MAX_ATTACHMENT_BYTES,
  MAX_ATTACHMENTS_PER_MESSAGE,
  checkAttachmentCap,
  isAcceptedAttachmentContentType,
  isAnalysableContentType,
  prepareAttachment,
  resolveAttachmentContentType,
} from './attachment-preparation'

/**
 * A base64 data URL whose decoded payload is exactly `byteLength` bytes. Only exact for a length
 * divisible by three, which is what every byte-length assertion here uses.
 */
function dataUrlOfBytes(byteLength: number, contentType = 'image/jpeg'): string {
  const base64 = 4 * Math.ceil(byteLength / 3)
  return `data:${contentType};base64,${'A'.repeat(base64)}`
}

describe('the attachment allow-list', () => {
  it('accepts the nine image types the server stores plus PDF', () => {
    const accepted = [
      'image/jpeg',
      'image/png',
      'image/webp',
      'image/gif',
      'image/avif',
      'image/bmp',
      'image/tiff',
      'image/heic',
      'image/heif',
      'application/pdf',
    ]

    for (const type of accepted) {
      expect(isAcceptedAttachmentContentType(type)).toBe(true)
    }
  })

  it('matches the allow-list case-insensitively and ignores parameters', () => {
    expect(isAcceptedAttachmentContentType('IMAGE/JPEG')).toBe(true)
    expect(isAcceptedAttachmentContentType('application/pdf; charset=binary')).toBe(true)
  })

  it('refuses everything the server does not store', () => {
    expect(isAcceptedAttachmentContentType('text/plain')).toBe(false)
    expect(isAcceptedAttachmentContentType('image/svg+xml')).toBe(false)
    expect(isAcceptedAttachmentContentType('')).toBe(false)
    expect(isAcceptedAttachmentContentType(null)).toBe(false)
    expect(isAcceptedAttachmentContentType(undefined)).toBe(false)
  })

  it('rescues a missing or generic declared type from the file name, exactly as the server does', () => {
    expect(resolveAttachmentContentType({ type: '', name: 'scan.tiff' })).toBe('image/tiff')
    expect(resolveAttachmentContentType({ type: 'application/octet-stream', name: 'doc.pdf' })).toBe(
      'application/pdf',
    )
  })

  it('does not rescue a declared disallowed type with an innocent extension', () => {
    // The uploader said what it is; the server refuses it and so must the picker.
    expect(resolveAttachmentContentType({ type: 'text/plain', name: 'fake.png' })).toBeNull()
    expect(resolveAttachmentContentType({ type: '', name: 'notes.txt' })).toBeNull()
  })
})

describe('the analysable subset', () => {
  it('is the four formats the visual assistant reads', () => {
    for (const type of ['image/jpeg', 'image/png', 'image/gif', 'image/webp']) {
      expect(isAnalysableContentType(type)).toBe(true)
    }
  })

  it('does not claim the storable-but-unreadable formats are analysable', () => {
    for (const type of [
      'image/heic',
      'image/heif',
      'image/avif',
      'image/bmp',
      'image/tiff',
      'application/pdf',
      '',
      null,
    ]) {
      expect(isAnalysableContentType(type)).toBe(false)
    }
  })
})

describe('prepareAttachment', () => {
  beforeEach(() => {
    compressMock.mockReset()
  })

  it('accepts a PDF untouched and never sends it through the resize path', async () => {
    const pdf = new File(['%PDF-1.4\n%fake\n'], 'invoice.pdf', { type: 'application/pdf' })

    const result = await prepareAttachment(pdf)

    expect(result.status).toBe('ready')
    if (result.status !== 'ready') throw new Error('expected a ready attachment')
    expect(result.kind).toBe('pdf')
    // Identity, not equality: a resized payload would be a different Blob.
    expect(result.file).toBe(pdf)
    expect(result.contentType).toBe('application/pdf')
    expect(result.byteLength).toBe(pdf.size)
    expect(compressMock).not.toHaveBeenCalled()
  })

  it('accepts a HEIC source whose resize yields JPEG, and reports the JPEG', async () => {
    compressMock.mockResolvedValue(dataUrlOfBytes(3000, 'image/jpeg'))
    const heic = new File(['heic bytes'], 'IMG_0042.HEIC', { type: 'image/heic' })

    const result = await prepareAttachment(heic)

    expect(result.status).toBe('ready')
    if (result.status !== 'ready') throw new Error('expected a ready attachment')
    expect(result.kind).toBe('image')
    expect(result.contentType).toBe('image/jpeg')
    expect(result.byteLength).toBe(3000)
    expect(result.file.type).toBe('image/jpeg')
    expect(compressMock).toHaveBeenCalledWith(heic, 1280, 0.85)
  })

  it('refuses a payload one byte over the 5 MB cap, naming the size and the limit', async () => {
    compressMock.mockResolvedValue(dataUrlOfBytes(MAX_ATTACHMENT_BYTES + 1))
    const file = new File(['big'], 'DSC_0042.HEIC', { type: 'image/heic' })

    const result = await prepareAttachment(file)

    expect(result.status).toBe('refused')
    if (result.status !== 'refused') throw new Error('expected a refusal')
    expect(result.reason).toBe('over-cap')
    expect(result.byteLength).toBe(MAX_ATTACHMENT_BYTES + 1)
    expect(result.limitBytes).toBe(MAX_ATTACHMENT_BYTES)
    expect(result.message).toContain(formatFileSize(MAX_ATTACHMENT_BYTES + 1))
    expect(result.message).toContain(formatFileSize(MAX_ATTACHMENT_BYTES))
  })

  it('names a distinctly larger size so the refusal is actionable', async () => {
    // 8.4 MB, divisible by three so the base64 length is exact.
    const oversize = 8_808_036
    compressMock.mockResolvedValue(dataUrlOfBytes(oversize))
    const file = new File(['big'], 'IMG_9000.HEIC', { type: 'image/heic' })

    const result = await prepareAttachment(file)

    expect(result.status).toBe('refused')
    if (result.status !== 'refused') throw new Error('expected a refusal')
    expect(result.message).toContain('8.4 MB')
    expect(result.message).toContain(formatFileSize(MAX_ATTACHMENT_BYTES))
  })

  it('applies the same cap to a PDF, which has no resize to bring it down', async () => {
    const oversized = new File([new Uint8Array(MAX_ATTACHMENT_BYTES + 1)], 'scan.pdf', {
      type: 'application/pdf',
    })

    const result = await prepareAttachment(oversized)

    expect(result.status).toBe('refused')
    if (result.status !== 'refused') throw new Error('expected a refusal')
    expect(result.reason).toBe('over-cap')
    expect(result.byteLength).toBe(MAX_ATTACHMENT_BYTES + 1)
    expect(compressMock).not.toHaveBeenCalled()
  })

  it('gates the resize output, not the source length: an over-source photo that resizes under the cap is accepted', async () => {
    // The raw-bytes fallback is why the resulting decoded length is the thing checked: a 5-15 MB
    // phone photo must be refused only when the bytes that would actually be uploaded are over.
    compressMock.mockResolvedValue(dataUrlOfBytes(300 * 1024))
    const bigSource = new File([new Uint8Array(8 * 1024 * 1024)], 'IMG_9000.HEIC', {
      type: 'image/heic',
    })
    expect(bigSource.size).toBeGreaterThan(MAX_ATTACHMENT_BYTES)

    const result = await prepareAttachment(bigSource)

    expect(result.status).toBe('ready')
    if (result.status !== 'ready') throw new Error('expected a ready attachment')
    expect(result.byteLength).toBe(300 * 1024)
  })

  it('refuses an unsupported type without touching the resize path', async () => {
    const notes = new File(['hello'], 'notes.txt', { type: 'text/plain' })

    const result = await prepareAttachment(notes)

    expect(result.status).toBe('refused')
    if (result.status !== 'refused') throw new Error('expected a refusal')
    expect(result.reason).toBe('unsupported-type')
    expect(result.message).toContain('notes.txt')
    expect(compressMock).not.toHaveBeenCalled()
  })
})

describe('checkAttachmentCap', () => {
  it("refuses the sixth held file with the server's own wording", () => {
    const refusal = checkAttachmentCap(5, 1)

    expect(refusal).not.toBeNull()
    expect(refusal?.reason).toBe('over-cap')
    expect(refusal?.limitCount).toBe(MAX_ATTACHMENTS_PER_MESSAGE)
    expect(refusal?.message).toBe('A message may carry at most 5 attachments.')
    expect(refusal?.message).toBe(ATTACHMENT_CAP_MESSAGE)
  })

  it('allows a message up to and including the cap', () => {
    expect(checkAttachmentCap(0, 5)).toBeNull()
    expect(checkAttachmentCap(4, 1)).toBeNull()
    expect(checkAttachmentCap(5, 0)).toBeNull()
  })

  it('refuses a batch that would cross the cap', () => {
    expect(checkAttachmentCap(4, 2)).not.toBeNull()
    expect(checkAttachmentCap(0, 6)).not.toBeNull()
  })
})
