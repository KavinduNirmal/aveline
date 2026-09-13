/**
 * Client-side visual color extractor using HTML5 Canvas.
 * Analyzes image pixel data in the browser to extract dominant RGB colors and hex codes
 * without requiring external server calls or when running in offline/local dev mode.
 */

export interface ExtractedColorResult {
  hex: string
  colorName: string
  r: number
  g: number
  b: number
}

const COLOR_PALETTE: { name: string; r: number; g: number; b: number; hex: string }[] = [
  { name: 'Emerald Green', r: 15, g: 81, b: 50, hex: '#0F5132' },
  { name: 'Forest Green', r: 34, g: 139, b: 34, hex: '#228B22' },
  { name: 'Olive Green', r: 128, g: 128, b: 0, hex: '#808000' },
  { name: 'Sage Green', r: 156, g: 175, b: 136, hex: '#9CAF88' },
  { name: 'Teal', r: 13, g: 148, b: 136, hex: '#0D9488' },
  { name: 'Midnight Blue', r: 30, g: 41, b: 59, hex: '#1E293B' },
  { name: 'Navy Blue', r: 0, g: 0, b: 128, hex: '#000080' },
  { name: 'Royal Blue', r: 65, g: 105, b: 225, hex: '#4169E1' },
  { name: 'Sky Blue', r: 135, g: 206, b: 235, hex: '#87CEEB' },
  { name: 'Imperial Burgundy', r: 128, g: 0, b: 32, hex: '#800020' },
  { name: 'Ruby Red', r: 155, g: 17, b: 30, hex: '#9B111E' },
  { name: 'Crimson Red', r: 220, g: 38, b: 38, hex: '#DC2626' },
  { name: 'Dusty Rose', r: 220, g: 174, b: 150, hex: '#DCAE96' },
  { name: 'Rose Gold', r: 183, g: 110, b: 121, hex: '#B76E79' },
  { name: 'Blush Pink', r: 255, g: 192, b: 203, hex: '#FFC0CB' },
  { name: 'Deep Plum', r: 74, g: 14, b: 78, hex: '#4A0E4E' },
  { name: 'Royal Purple', r: 147, g: 51, b: 234, hex: '#9333EA' },
  { name: 'Lavender', r: 230, g: 230, b: 250, hex: '#E6E6FA' },
  { name: 'Antique Gold', r: 212, g: 175, b: 55, hex: '#D4AF37' },
  { name: 'Champagne', r: 247, g: 231, b: 206, hex: '#F7E7CE' },
  { name: 'Mustard Yellow', r: 234, g: 179, b: 8, hex: '#EAB308' },
  { name: 'Burnt Orange', r: 234, g: 88, b: 12, hex: '#EA580C' },
  { name: 'Terracotta', r: 183, g: 65, b: 14, hex: '#B7410E' },
  { name: 'Ivory White', r: 255, g: 255, b: 240, hex: '#FFFFF0' },
  { name: 'Pure White', r: 255, g: 255, b: 255, hex: '#FFFFFF' },
  { name: 'Charcoal Grey', r: 55, g: 65, b: 81, hex: '#374151' },
  { name: 'Silver', r: 192, g: 192, b: 192, hex: '#C0C0C0' },
  { name: 'Midnight Black', r: 18, g: 18, b: 18, hex: '#121212' },
]

/**
 * Calculates color distance (Euclidean in RGB space) to find the nearest human-readable color name.
 */
export function getClosestColorName(r: number, g: number, b: number): string {
  let closest = COLOR_PALETTE[0].name
  let minDistance = Infinity

  for (const item of COLOR_PALETTE) {
    const dist =
      Math.pow(r - item.r, 2) * 0.3 +
      Math.pow(g - item.g, 2) * 0.59 +
      Math.pow(b - item.b, 2) * 0.11 // Weighted perceived luminance
    if (dist < minDistance) {
      minDistance = dist
      closest = item.name
    }
  }

  return closest
}

/**
 * Samples pixels from an image element or URL to compute the authentic dominant color and hex.
 */
export async function extractDominantColor(imageUrl: string): Promise<ExtractedColorResult | null> {
  if (typeof window === 'undefined' || !imageUrl) return null

  return new Promise((resolve) => {
    const img = new Image()
    img.crossOrigin = 'anonymous'

    img.onload = () => {
      try {
        const canvas = document.createElement('canvas')
        const ctx = canvas.getContext('2d', { willReadFrequently: true })
        if (!ctx) return resolve(null)

        const size = 64
        canvas.width = size
        canvas.height = size
        ctx.drawImage(img, 0, 0, size, size)

        // Sample center region to prioritize the garment over peripheral background
        const inset = Math.floor(size * 0.15)
        const sampleSize = size - inset * 2
        const imageData = ctx.getImageData(inset, inset, sampleSize, sampleSize)
        const data = imageData.data

        let totalR = 0
        let totalG = 0
        let totalB = 0
        let count = 0

        for (let i = 0; i < data.length; i += 4) {
          const r = data[i]
          const g = data[i + 1]
          const b = data[i + 2]
          const a = data[i + 3]

          // Ignore transparent or near-pure white background pixels
          if (a < 120 || (r > 245 && g > 245 && b > 245)) {
            continue
          }

          totalR += r
          totalG += g
          totalB += b
          count++
        }

        // Fallback to all pixels if all were filtered out
        if (count === 0) {
          for (let i = 0; i < data.length; i += 4) {
            totalR += data[i]
            totalG += data[i + 1]
            totalB += data[i + 2]
            count++
          }
        }

        if (count === 0) return resolve(null)

        const avgR = Math.round(totalR / count)
        const avgG = Math.round(totalG / count)
        const avgB = Math.round(totalB / count)

        const hex = `#${((1 << 24) + (avgR << 16) + (avgG << 8) + avgB)
          .toString(16)
          .slice(1)
          .toUpperCase()}`

        const colorName = getClosestColorName(avgR, avgG, avgB)

        resolve({
          hex,
          colorName,
          r: avgR,
          g: avgG,
          b: avgB,
        })
      } catch {
        resolve(null)
      }
    }

    img.onerror = () => resolve(null)
    img.src = imageUrl
  })
}
