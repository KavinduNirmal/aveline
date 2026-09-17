import { describe, expect, it } from 'vitest'
import { COLOR_PALETTE, extractDominantColor, getClosestColorName } from './color-extractor'

describe('color-extractor', () => {
  describe('COLOR_PALETTE', () => {
    it('contains comprehensive green shade differentiation', () => {
      const greenNames = COLOR_PALETTE.map((c) => c.name)
      expect(greenNames).toContain('Emerald Green')
      expect(greenNames).toContain('Forest Green')
      expect(greenNames).toContain('Olive Green')
      expect(greenNames).toContain('Mint Green')
      expect(greenNames).toContain('Sage Green')
      expect(greenNames).toContain('Bottle Green')
      expect(greenNames).toContain('Peacock Teal')
    })

    it('contains essential luxury and couture shades', () => {
      const shadeNames = COLOR_PALETTE.map((c) => c.name)
      expect(shadeNames).toContain('Royal Burgundy')
      expect(shadeNames).toContain('Burnt Terracotta')
      expect(shadeNames).toContain('Midnight Navy')
      expect(shadeNames).toContain('Champagne Gold')
      expect(shadeNames).toContain('Deep Plum')
    })
  })

  describe('getClosestColorName', () => {
    it('accurately differentiates green shades rather than defaulting to Sage Green', () => {
      // Emerald Green: rich deep jewel green
      expect(getClosestColorName(15, 81, 50)).toBe('Emerald Green')
      expect(getClosestColorName(18, 90, 55)).toBe('Emerald Green')

      // Forest Green: vivid woodland green
      expect(getClosestColorName(34, 139, 34)).toBe('Forest Green')
      expect(getClosestColorName(40, 145, 38)).toBe('Forest Green')

      // Olive Green: warm earthy yellow-green
      expect(getClosestColorName(85, 107, 47)).toBe('Olive Green')
      expect(getClosestColorName(90, 115, 50)).toBe('Olive Green')

      // Mint Green: crisp bright pastel green
      expect(getClosestColorName(152, 255, 152)).toBe('Mint Green')

      // Bottle Green: very dark pine green
      expect(getClosestColorName(0, 66, 37)).toBe('Bottle Green')

      // Peacock Teal: deep cyan-green
      expect(getClosestColorName(0, 128, 128)).toBe('Peacock Teal')

      // Sage Green: muted soft grey-green
      expect(getClosestColorName(156, 175, 136)).toBe('Sage Green')
    })

    it('accurately resolves non-green shades', () => {
      // Royal Burgundy
      expect(getClosestColorName(128, 0, 32)).toBe('Royal Burgundy')

      // Burnt Terracotta
      expect(getClosestColorName(226, 114, 91)).toBe('Burnt Terracotta')

      // Midnight Navy
      expect(getClosestColorName(30, 41, 59)).toBe('Midnight Navy')

      // Pure White
      expect(getClosestColorName(255, 255, 255)).toBe('Pure White')

      // Midnight Black
      expect(getClosestColorName(18, 18, 18)).toBe('Midnight Black')
    })
  })

  describe('extractDominantColor', () => {
    it('returns null when given an empty URL', async () => {
      const result = await extractDominantColor('')
      expect(result).toBeNull()
    })
  })
})
