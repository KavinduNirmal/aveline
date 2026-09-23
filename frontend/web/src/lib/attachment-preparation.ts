/**
 * The composer's attachment decisions, kept pure so they can be tested without a DOM.
 *
 * The rules are the server's, restated here so a file the server would refuse is refused
 * client-side before a doomed upload: the nine image types plus `application/pdf`, a 5 MB
 * per-file cap, and at most five files per message. `isValidImageFile` is deliberately not
 * reused: it knows six image types and no PDF, so it would reject files this route stores.
 */

import {
  compressAndResizeImage,
  dataUrlByteLength,
  dataUrlToBlob,
  formatFileSize,
} from './image-optimizer'

/** The per-file attachment cap, matching `MediaContentTypes.MaxFileBytes`. */
export const MAX_ATTACHMENT_BYTES = 5 * 1024 * 1024

/** The per-message attachment cap, matching `MediaContentTypes.MaxPerMessage`. */
export const MAX_ATTACHMENTS_PER_MESSAGE = 5

/** The server's own wording (`ConversationService.cs`), reused verbatim so the clients agree. */
export const ATTACHMENT_CAP_MESSAGE = `A message may carry at most ${MAX_ATTACHMENTS_PER_MESSAGE} attachments.`

/** The nine image types the store accepts, in the same order as the server's list. */
const IMAGE_ATTACHMENT_TYPES = [
  'image/jpeg',
  'image/png',
  'image/webp',
  'image/gif',
  'image/avif',
  'image/bmp',
  'image/tiff',
  'image/heic',
  'image/heif',
] as const

const PDF_CONTENT_TYPE = 'application/pdf'

const ACCEPTED_ATTACHMENT_TYPES: readonly string[] = [...IMAGE_ATTACHMENT_TYPES, PDF_CONTENT_TYPE]

/**
 * The four formats the visual assistant can read (`VisionContentTypes`). Everything else on the
 * allow-list is stored and served normally and is simply not analysable.
 */
const ANALYSABLE_CONTENT_TYPES: readonly string[] = [
  'image/jpeg',
  'image/png',
  'image/gif',
  'image/webp',
]

/** The type several clients send for a picked file, which the extension is allowed to rescue. */
const GENERIC_CONTENT_TYPE = 'application/octet-stream'

/** Mirrors `MediaContentTypes.Resolve`'s extension arm for a declared type that says nothing. */
const EXTENSION_CONTENT_TYPES: Readonly<Record<string, string>> = {
  pdf: PDF_CONTENT_TYPE,
  jpg: 'image/jpeg',
  jpeg: 'image/jpeg',
  png: 'image/png',
  webp: 'image/webp',
  gif: 'image/gif',
  avif: 'image/avif',
  bmp: 'image/bmp',
  tif: 'image/tiff',
  tiff: 'image/tiff',
  heic: 'image/heic',
  heif: 'image/heif',
}

function normalizeContentType(value: string | null | undefined): string | null {
  const mediaType = value?.split(';')[0].trim().toLowerCase()
  return mediaType ? mediaType : null
}

/** Whether the content type may be stored at all: one of the nine images, or a PDF. */
export function isAcceptedAttachmentContentType(value: string | null | undefined): boolean {
  const mediaType = normalizeContentType(value)
  return mediaType !== null && ACCEPTED_ATTACHMENT_TYPES.includes(mediaType)
}

/**
 * Whether the visual assistant can read the stored bytes. This is asked of the type the upload
 * **response** reports, never the declared one: a HEIC the browser re-encoded to JPEG is stored as
 * JPEG and is analysable.
 */
export function isAnalysableContentType(value: string | null | undefined): boolean {
  const mediaType = normalizeContentType(value)
  return mediaType !== null && ANALYSABLE_CONTENT_TYPES.includes(mediaType)
}

/**
 * The canonical content type for a picked file, or `null` when it may not be attached. Mirrors the
 * server: the declared type wins when it is on the allow-list; absent or generic, the extension
 * decides; a declared type that is present and disallowed is not rescued by an extension.
 */
export function resolveAttachmentContentType(file: {
  type?: string | null
  name?: string | null
}): string | null {
  const declared = normalizeContentType(file?.type)
  if (declared && isAcceptedAttachmentContentType(declared)) {
    return declared
  }
  if (declared && declared !== GENERIC_CONTENT_TYPE) {
    return null
  }

  const name = (file?.name ?? '').toLowerCase()
  const dot = name.lastIndexOf('.')
  if (dot < 0) return null
  return EXTENSION_CONTENT_TYPES[name.slice(dot + 1)] ?? null
}

/** The two refusals the picker can produce. Both are a result, never a thrown string. */
export type AttachmentRefusalReason = 'over-cap' | 'unsupported-type'

/** A file the picker refuses, with a message the UI can show as-is. */
export interface AttachmentRefusal {
  status: 'refused'
  reason: AttachmentRefusalReason
  /** The refused file's name, when the refusal is about one file. */
  fileName?: string
  message: string
  /** The offending decoded byte length, for a byte-cap refusal. */
  byteLength?: number
  /** The byte cap that was crossed, for a byte-cap refusal. */
  limitBytes?: number
  /** The count cap that was crossed, for a count-cap refusal. */
  limitCount?: number
}

/** A file ready to upload, with the decoded size of the bytes that will actually be sent. */
export interface PreparedAttachment {
  status: 'ready'
  /** The payload to upload: a JPEG Blob for an image, or the untouched file for a PDF. */
  file: Blob
  fileName: string
  contentType: string
  /** The decoded byte length of `file`, which is what the 5 MB cap is checked against. */
  byteLength: number
  kind: 'image' | 'pdf'
}

export type AttachmentPreparation = PreparedAttachment | AttachmentRefusal

function overCapRefusal(fileName: string, byteLength: number): AttachmentRefusal {
  return {
    status: 'refused',
    reason: 'over-cap',
    fileName,
    byteLength,
    limitBytes: MAX_ATTACHMENT_BYTES,
    message:
      `${fileName} is ${formatFileSize(byteLength)}. ` +
      `Attachments may be at most ${formatFileSize(MAX_ATTACHMENT_BYTES)}. ` +
      `Try a smaller photo or a JPEG.`,
  }
}

function unsupportedTypeRefusal(fileName: string): AttachmentRefusal {
  return {
    status: 'refused',
    reason: 'unsupported-type',
    fileName,
    message: `${fileName} is not an image or a PDF, so it can't be attached.`,
  }
}

function dataUrlContentType(dataUrl: string): string | null {
  const value = /^data:([^;,]*)/i.exec(dataUrl)?.[1]
  return value ? value.trim().toLowerCase() : null
}

/**
 * Resizes an image, or passes a PDF through untouched, and gates the **resulting decoded length**
 * against the 5 MB cap.
 *
 * The decoded length is the thing checked because `compressAndResizeImage` falls back to the raw
 * file bytes whenever the canvas path fails: the source's own `size` says nothing about what would
 * be uploaded. A 5-15 MB phone photo that resizes under the cap is accepted; the same photo whose
 * decode fails is refused with the size of the bytes the server would have received.
 */
export async function prepareAttachment(file: File): Promise<AttachmentPreparation> {
  const contentType = resolveAttachmentContentType(file)
  if (!contentType) {
    return unsupportedTypeRefusal(file.name)
  }

  if (contentType === PDF_CONTENT_TYPE) {
    if (file.size > MAX_ATTACHMENT_BYTES) {
      return overCapRefusal(file.name, file.size)
    }
    return {
      status: 'ready',
      file,
      fileName: file.name,
      contentType,
      byteLength: file.size,
      kind: 'pdf',
    }
  }

  const dataUrl = await compressAndResizeImage(file, 1280, 0.85)
  const byteLength = dataUrlByteLength(dataUrl)
  if (byteLength > MAX_ATTACHMENT_BYTES) {
    return overCapRefusal(file.name, byteLength)
  }

  return {
    status: 'ready',
    file: dataUrlToBlob(dataUrl),
    fileName: file.name,
    contentType: dataUrlContentType(dataUrl) ?? 'image/jpeg',
    byteLength,
    kind: 'image',
  }
}

/**
 * Refuses a pick that would cross the five-per-message cap, with the server's wording. Returns
 * `null` when the batch fits.
 */
export function checkAttachmentCap(heldCount: number, incomingCount = 1): AttachmentRefusal | null {
  if (heldCount + incomingCount <= MAX_ATTACHMENTS_PER_MESSAGE) {
    return null
  }
  return {
    status: 'refused',
    reason: 'over-cap',
    limitCount: MAX_ATTACHMENTS_PER_MESSAGE,
    message: ATTACHMENT_CAP_MESSAGE,
  }
}
