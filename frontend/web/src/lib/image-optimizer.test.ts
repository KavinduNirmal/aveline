import { describe, expect, it } from 'vitest'
import {
  calculateTargetDimensions,
  compressAndResizeImage,
  fileToDataUrl,
  formatFileSize,
  isValidImageFile,
} from './image-optimizer'

describe('image-optimizer', () => {
  describe('isValidImageFile', () => {
    it('returns true for supported MIME types', () => {
      const jpeg = new File(['content'], 'saree.jpg', { type: 'image/jpeg' })
      const png = new File(['content'], 'dress.png', { type: 'image/png' })
      const webp = new File(['content'], 'gown.webp', { type: 'image/webp' })
      const heic = new File(['content'], 'outfit.heic', { type: 'image/heic' })
      const heif = new File(['content'], 'outfit.heif', { type: 'image/heif' })
      const avif = new File(['content'], 'outfit.avif', { type: 'image/avif' })

      expect(isValidImageFile(jpeg)).toBe(true)
      expect(isValidImageFile(png)).toBe(true)
      expect(isValidImageFile(webp)).toBe(true)
      expect(isValidImageFile(heic)).toBe(true)
      expect(isValidImageFile(heif)).toBe(true)
      expect(isValidImageFile(avif)).toBe(true)
    })

    it('returns true for recognized file extensions with empty mime types', () => {
      const customHeic = new File(['content'], 'photo.heic', { type: '' })
      const customJpg = new File(['content'], 'garment.jpeg', { type: '' })
      const customPng = new File(['content'], 'look.png', { type: '' })
      const customWebp = new File(['content'], 'look.webp', { type: '' })
      const customAvif = new File(['content'], 'look.avif', { type: '' })

      expect(isValidImageFile(customHeic)).toBe(true)
      expect(isValidImageFile(customJpg)).toBe(true)
      expect(isValidImageFile(customPng)).toBe(true)
      expect(isValidImageFile(customWebp)).toBe(true)
      expect(isValidImageFile(customAvif)).toBe(true)
    })

    it('returns false for invalid files or null/undefined', () => {
      const pdf = new File(['content'], 'doc.pdf', { type: 'application/pdf' })
      const txt = new File(['content'], 'notes.txt', { type: 'text/plain' })

      expect(isValidImageFile(pdf)).toBe(false)
      expect(isValidImageFile(txt)).toBe(false)
      expect(isValidImageFile(null)).toBe(false)
      expect(isValidImageFile(undefined)).toBe(false)
    })
  })

  describe('formatFileSize', () => {
    it('formats bytes accurately', () => {
      expect(formatFileSize(-10)).toBe('0 B')
      expect(formatFileSize(0)).toBe('0 B')
      expect(formatFileSize(512)).toBe('512 B')
      expect(formatFileSize(1024)).toBe('1 KB')
      expect(formatFileSize(1024 * 500)).toBe('500 KB')
      expect(formatFileSize(1024 * 1024 * 2.5)).toBe('2.5 MB')
      expect(formatFileSize(1024 * 1024 * 1024 * 1.5)).toBe('1.5 GB')
    })
  })

  describe('calculateTargetDimensions', () => {
    it('handles zero or negative dimensions safely', () => {
      expect(calculateTargetDimensions(0, 0, 1000)).toEqual({ width: 1000, height: 1000 })
      expect(calculateTargetDimensions(-100, 200, 1000)).toEqual({ width: 1000, height: 1000 })
    })

    it('preserves dimensions when already smaller than maxDimension', () => {
      const result = calculateTargetDimensions(800, 600, 1280)
      expect(result).toEqual({ width: 800, height: 600 })
    })

    it('scales down landscape images while preserving aspect ratio', () => {
      const result = calculateTargetDimensions(2560, 1440, 1280)
      expect(result.width).toBe(1280)
      expect(result.height).toBe(720)
    })

    it('scales down portrait images while preserving aspect ratio', () => {
      const result = calculateTargetDimensions(1440, 2560, 1280)
      expect(result.height).toBe(1280)
      expect(result.width).toBe(720)
    })

    it('handles square images proportionally', () => {
      const result = calculateTargetDimensions(2000, 2000, 1280)
      expect(result).toEqual({ width: 1280, height: 1280 })
    })
  })

  describe('fileToDataUrl', () => {
    it('converts a blob to a base64 data url using FileReader', async () => {
      const file = new Blob(['mock image data'], { type: 'image/jpeg' })
      const result = await fileToDataUrl(file)
      expect(result).toContain('data:image/jpeg;base64,')
    })
  })

  describe('compressAndResizeImage', () => {
    it('falls back to fileToDataUrl in non-window environment', async () => {
      const file = new File(['test image'], 'saree.jpg', { type: 'image/jpeg' })
      const result = await compressAndResizeImage(file)
      expect(result).toContain('data:image/jpeg;base64,')
    })
  })
})
