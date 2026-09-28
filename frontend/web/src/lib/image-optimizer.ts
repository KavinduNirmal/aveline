/**
 * Image processing utilities for client-side garment photo optimization,
 * format validation, and base64 data URL conversion.
 */

const ALLOWED_MIME_TYPES = [
  'image/jpeg',
  'image/png',
  'image/webp',
  'image/heic',
  'image/heif',
  'image/avif',
]

/**
 * Validates whether the uploaded file is a supported image format.
 */
export function isValidImageFile(file: File | null | undefined): boolean {
  if (!file) return false
  if (ALLOWED_MIME_TYPES.includes(file.type.toLowerCase())) return true
  // Fallback for file extensions if mime type is missing or generic
  const name = file.name.toLowerCase()
  return (
    name.endsWith('.jpg') ||
    name.endsWith('.jpeg') ||
    name.endsWith('.png') ||
    name.endsWith('.webp') ||
    name.endsWith('.heic') ||
    name.endsWith('.avif')
  )
}

/**
 * Formats a byte size into a human-readable string (e.g. "1.4 MB", "420 KB").
 */
export function formatFileSize(bytes: number): string {
  if (bytes <= 0) return '0 B'
  const k = 1024
  const sizes = ['B', 'KB', 'MB', 'GB']
  const i = Math.floor(Math.log(bytes) / Math.log(k))
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`
}

/**
 * Converts a File or Blob into a base64 Data URL.
 */
export async function fileToDataUrl(file: Blob): Promise<string> {
  if (typeof FileReader !== 'undefined') {
    return new Promise((resolve, reject) => {
      const reader = new FileReader()
      reader.onload = () => resolve(reader.result as string)
      reader.onerror = (err) => reject(err)
      reader.readAsDataURL(file)
    })
  }

  const arrayBuffer = await file.arrayBuffer()
  const bytes = new Uint8Array(arrayBuffer)
  let binary = ''
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i])
  }
  const base64 = btoa(binary)
  const mimeType = file.type || 'image/jpeg'
  return `data:${mimeType};base64,${base64}`
}

/**
 * Calculates target dimensions preserving aspect ratio within max constraints.
 */
export function calculateTargetDimensions(
  width: number,
  height: number,
  maxDimension = 1280,
): { width: number; height: number } {
  if (width <= 0 || height <= 0) {
    return { width: maxDimension, height: maxDimension }
  }

  if (width <= maxDimension && height <= maxDimension) {
    return { width, height }
  }

  if (width > height) {
    const ratio = height / width
    return {
      width: maxDimension,
      height: Math.round(maxDimension * ratio),
    }
  } else {
    const ratio = width / height
    return {
      width: Math.round(maxDimension * ratio),
      height: maxDimension,
    }
  }
}

/**
 * Resizes and compresses an image file on an offscreen canvas.
 * Produces a high-quality JPEG base64 Data URL suitable for sub-second Vision AI transfer.
 */
export async function compressAndResizeImage(
  file: File,
  maxDimension = 1280,
  quality = 0.85,
): Promise<string> {
  if (typeof window === 'undefined') {
    return fileToDataUrl(file)
  }

  return new Promise(async (resolve, reject) => {
    try {
      // 1. If createImageBitmap is available, fast path
      if (typeof createImageBitmap === 'function') {
        const bitmap = await createImageBitmap(file).catch(() => null)
        if (bitmap) {
          const { width, height } = calculateTargetDimensions(
            bitmap.width,
            bitmap.height,
            maxDimension,
          )

          const canvas = document.createElement('canvas')
          canvas.width = width
          canvas.height = height
          const ctx = canvas.getContext('2d')
          if (ctx) {
            ctx.drawImage(bitmap, 0, 0, width, height)
            const dataUrl = canvas.toDataURL('image/jpeg', quality)
            return resolve(dataUrl)
          }
        }
      }

      // 2. Standard HTMLImageElement path
      const img = new Image()
      const objectUrl = URL.createObjectURL(file)

      img.onload = () => {
        URL.revokeObjectURL(objectUrl)
        try {
          const { width, height } = calculateTargetDimensions(
            img.naturalWidth || img.width,
            img.naturalHeight || img.height,
            maxDimension,
          )

          const canvas = document.createElement('canvas')
          canvas.width = width
          canvas.height = height
          const ctx = canvas.getContext('2d')
          if (!ctx) {
            return fileToDataUrl(file).then(resolve).catch(reject)
          }

          ctx.drawImage(img, 0, 0, width, height)
          const dataUrl = canvas.toDataURL('image/jpeg', quality)
          resolve(dataUrl)
        } catch {
          fileToDataUrl(file).then(resolve).catch(reject)
        }
      }

      img.onerror = () => {
        URL.revokeObjectURL(objectUrl)
        fileToDataUrl(file).then(resolve).catch(reject)
      }

      img.src = objectUrl
    } catch {
      fileToDataUrl(file).then(resolve).catch(reject)
    }
  })
}

/**
 * The decoded byte length of a data URL's payload, without allocating the bytes.
 *
 * Base64 inflates a payload by roughly 4/3, so a data URL's string length is not the size of the
 * file it carries and must never be compared against a byte cap. This is the number to compare.
 */
export function dataUrlByteLength(dataUrl: string): number {
  const comma = dataUrl.indexOf(',')
  if (comma < 0) return 0
  const meta = dataUrl.slice(0, comma)
  const payload = dataUrl.slice(comma + 1)

  if (!/;base64/i.test(meta)) {
    return new TextEncoder().encode(decodeURIComponent(payload)).length
  }

  const clean = payload.replace(/\s/g, '')
  if (clean.length === 0) return 0
  const padding = clean.endsWith('==') ? 2 : clean.endsWith('=') ? 1 : 0
  return Math.floor((clean.length * 3) / 4) - padding
}

/**
 * Rebuilds the bytes a data URL carries as a Blob, preserving its declared media type.
 *
 * This is what turns the optimizer's canvas output back into something uploadable as multipart
 * form data instead of re-encoding the same bytes as base64 JSON.
 */
export function dataUrlToBlob(dataUrl: string): Blob {
  const comma = dataUrl.indexOf(',')
  const meta = comma < 0 ? '' : dataUrl.slice(0, comma)
  const payload = comma < 0 ? '' : dataUrl.slice(comma + 1)
  const contentType = /^data:([^;,]*)/i.exec(meta)?.[1] || 'application/octet-stream'

  if (!/;base64/i.test(meta)) {
    return new Blob([new TextEncoder().encode(decodeURIComponent(payload))], { type: contentType })
  }

  const binary = atob(payload.replace(/\s/g, ''))
  const bytes = new Uint8Array(binary.length)
  for (let i = 0; i < binary.length; i++) {
    bytes[i] = binary.charCodeAt(i)
  }
  return new Blob([bytes], { type: contentType })
}
