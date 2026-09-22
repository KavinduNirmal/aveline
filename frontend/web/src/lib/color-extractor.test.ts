import { describe, expect, it } from 'vitest'
import {
  COLOR_PALETTE,
  analyzeCanvasMetrics,
  extractDominantColor,
  extractVisualAttributesAndColor,
  getClosestColorName,
  isolateGarmentColor,
  inferGarmentFromMetricsAndMetadata,
} from './color-extractor'

/**
 * `inferGarmentFromMetricsAndMetadata` is deliberately nullable now: it returns `null` when it has
 * neither pixel geometry nor a filename keyword to work from, instead of inventing a complete
 * analysis. Every case below supplies a metrics object, so `null` here would be a test bug rather
 * than a result, and asserting that once keeps the individual assertions readable.
 */
function inferWithEvidence(
  metrics: Parameters<typeof inferGarmentFromMetricsAndMetadata>[0],
  imageUrl: string,
  fileName?: string,
  contextHint?: string,
): NonNullable<ReturnType<typeof inferGarmentFromMetricsAndMetadata>> {
  const result = inferGarmentFromMetricsAndMetadata(metrics, imageUrl, fileName, contextHint)
  expect(result).not.toBeNull()
  return result as NonNullable<ReturnType<typeof inferGarmentFromMetricsAndMetadata>>
}

/**
 * Builds a pixel buffer from a per-pixel painter, so the isolation tests state their frame in
 * terms of what the photograph contains rather than as a wall of channel indices.
 */
function frameOf(
  width: number,
  height: number,
  painter: (x: number, y: number) => [number, number, number],
): Uint8ClampedArray {
  const buffer = new Uint8ClampedArray(width * height * 4)
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const [r, g, b] = painter(x, y)
      const offset = (y * width + x) * 4
      buffer[offset] = r
      buffer[offset + 1] = g
      buffer[offset + 2] = b
      buffer[offset + 3] = 255
    }
  }
  return buffer
}

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
      expect(shadeNames).toContain('Ruby Red')
      expect(shadeNames).toContain('Deep Maroon')
      expect(shadeNames).toContain('Rose Gold')
      expect(shadeNames).toContain('Heirloom Ivory')
    })
  })

  describe('getClosestColorName', () => {
    it('accurately differentiates green shades rather than defaulting to Sage Green', () => {
      expect(getClosestColorName(15, 81, 50)).toBe('Emerald Green')
      expect(getClosestColorName(18, 90, 55)).toBe('Emerald Green')
      expect(getClosestColorName(34, 139, 34)).toBe('Forest Green')
      expect(getClosestColorName(40, 145, 38)).toBe('Forest Green')
      expect(getClosestColorName(85, 107, 47)).toBe('Olive Green')
      expect(getClosestColorName(90, 115, 50)).toBe('Olive Green')
      expect(getClosestColorName(152, 255, 152)).toBe('Mint Green')
      expect(getClosestColorName(0, 66, 37)).toBe('Bottle Green')
      expect(getClosestColorName(0, 128, 128)).toBe('Peacock Teal')
      expect(getClosestColorName(156, 175, 136)).toBe('Sage Green')
    })

    it('accurately resolves non-green shades', () => {
      expect(getClosestColorName(128, 0, 32)).toBe('Royal Burgundy')
      expect(getClosestColorName(226, 114, 91)).toBe('Burnt Terracotta')
      expect(getClosestColorName(30, 41, 59)).toBe('Midnight Navy')
      expect(getClosestColorName(255, 255, 255)).toBe('Pure White')
      expect(getClosestColorName(18, 18, 18)).toBe('Midnight Black')
      expect(getClosestColorName(247, 231, 206)).toBe('Champagne Gold')
      expect(getClosestColorName(255, 218, 185)).toBe('Peach')
      expect(getClosestColorName(255, 127, 80)).toBe('Sunset Coral')
      expect(getClosestColorName(179, 139, 109)).toBe('Taupe')
      expect(getClosestColorName(192, 192, 192)).toBe('Silver')
    })
  })

  describe('analyzeCanvasMetrics', () => {
    it('returns null when context throws or image data is empty', () => {
      const mockCtx = {
        getImageData: () => {
          throw new Error('Canvas error')
        },
      } as unknown as CanvasRenderingContext2D

      expect(analyzeCanvasMetrics(mockCtx, 10, 10)).toBeNull()
    })

    it('processes pixel array and extracts metrics', () => {
      const width = 4
      const height = 4
      const buffer = new Uint8ClampedArray(width * height * 4)

      for (let i = 0; i < width * height; i++) {
        const offset = i * 4
        // High saturation emerald green fabric with alpha 255
        buffer[offset] = 15 // R
        buffer[offset + 1] = 81 // G
        buffer[offset + 2] = 50 // B
        buffer[offset + 3] = 255 // A
      }

      // Add a low alpha pixel (should be skipped)
      buffer[3] = 50

      // Add a specular highlight pixel
      buffer[4] = 220
      buffer[5] = 200
      buffer[6] = 50
      buffer[7] = 255

      const mockCtx = {
        getImageData: () => ({
          data: buffer,
          width,
          height,
          colorSpace: 'srgb',
        }),
      } as unknown as CanvasRenderingContext2D

      const result = analyzeCanvasMetrics(mockCtx, width, height)
      expect(result).not.toBeNull()
      expect(result?.color.colorName).toBe('Emerald Green')
      expect(result?.aspectRatio).toBe(1)
      expect(result?.isRoyalJewelTone).toBe(true)
    })

    it('handles studio backdrops and calculates flare ratio', () => {
      const width = 10
      const height = 10
      const buffer = new Uint8ClampedArray(width * height * 4)

      for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
          const idx = (y * width + x) * 4
          if (y < 3) {
            // White backdrop at top
            buffer[idx] = 255
            buffer[idx + 1] = 255
            buffer[idx + 2] = 255
            buffer[idx + 3] = 255
          } else if (y < 6) {
            // Ruby red in middle
            buffer[idx] = 155
            buffer[idx + 1] = 17
            buffer[idx + 2] = 30
            buffer[idx + 3] = 255
          } else {
            // Ruby red at bottom (wide flare)
            buffer[idx] = 155
            buffer[idx + 1] = 17
            buffer[idx + 2] = 30
            buffer[idx + 3] = 255
          }
        }
      }

      const mockCtx = {
        getImageData: () => ({
          data: buffer,
          width,
          height,
          colorSpace: 'srgb',
        }),
      } as unknown as CanvasRenderingContext2D

      const result = analyzeCanvasMetrics(mockCtx, width, height)
      expect(result).not.toBeNull()
      expect(result?.isWarmEthnicTone).toBe(true)
    })
  })

  describe('extractVisualAttributesAndColor', () => {
    it('returns null for empty imageUrl', async () => {
      const result = await extractVisualAttributesAndColor('')
      expect(result).toBeNull()
    })

    it('classifies Lehengas variants from metadata and context hints', async () => {
      const bridal = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'bridal_lehenga_red.jpg')
      expect(bridal?.category).toBe('Lehengas')
      expect(bridal?.garmentType).toBe('Embroidered Bridal Lehenga')
      expect(bridal?.style).toBe('Royal Bridal')

      const chevron = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'chevron_choli.jpg')
      expect(chevron?.category).toBe('Lehengas')
      expect(chevron?.garmentType).toBe('Chevron Embroidered Lehenga')

      const velvet = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'velvet_ghagra_zari.jpg')
      expect(velvet?.category).toBe('Lehengas')
      expect(velvet?.garmentType).toBe('Velvet Bridal Lehenga')

      const generic = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'lehenga.jpg')
      expect(generic?.category).toBe('Lehengas')
      expect(generic?.garmentType).toBe('Flared Silk Lehenga')
    })

    it('classifies Sarees variants from metadata and context hints', async () => {
      const banarasi = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'banarasi_saree.jpg')
      expect(banarasi?.category).toBe('Sarees')
      expect(banarasi?.garmentType).toBe('Banarasi Silk Brocade Saree')

      const chanderi = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'chanderi_sari.jpg')
      expect(chanderi?.category).toBe('Sarees')
      expect(chanderi?.garmentType).toBe('Chanderi Handloom Saree')

      const organza = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'floral_organza_saree.jpg')
      expect(organza?.category).toBe('Sarees')
      expect(organza?.garmentType).toBe('Floral Organza Saree')

      const kanjeevaram = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'kanjeevaram_pallu.jpg')
      expect(kanjeevaram?.category).toBe('Sarees')
      expect(kanjeevaram?.garmentType).toBe('Silk Kanjeevaram Saree')
    })

    it('classifies Gowns variants from metadata and context hints', async () => {
      const ballgown = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'silk_ballgown.jpg')
      expect(ballgown?.category).toBe('Gowns')
      expect(ballgown?.garmentType).toBe('Luminous Silk Ballgown')

      const mermaid = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'mermaid_dress.jpg')
      expect(mermaid?.category).toBe('Gowns')
      expect(mermaid?.garmentType).toBe('Mermaid Evening Gown')

      const organza = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'organza_maxi.jpg')
      expect(organza?.category).toBe('Gowns')
      expect(organza?.garmentType).toBe('Luminous Organza Evening Gown')

      const velvetGown = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'velvet_cocktail_dress.jpg')
      expect(velvetGown?.category).toBe('Gowns')
      expect(velvetGown?.garmentType).toBe('Velvet Evening Gown')

      const evening = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'evening_dress.jpg')
      expect(evening?.category).toBe('Gowns')
      expect(evening?.garmentType).toBe('Luminous Evening Gown')
    })

    it('classifies Kurtas & Tunics variants from metadata and context hints', async () => {
      const anarkali = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'anarkali_chikankari.jpg')
      expect(anarkali?.category).toBe('Kurtas & Tunics')
      expect(anarkali?.garmentType).toBe('Anarkali Kurta & Tunic')

      const blouse = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'silk_blouse.jpg')
      expect(blouse?.category).toBe('Kurtas & Tunics')
      expect(blouse?.garmentType).toBe('Embroidered Silk Blouse')

      const linen = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'linen_kurti.jpg')
      expect(linen?.category).toBe('Kurtas & Tunics')
      expect(linen?.garmentType).toBe('Straight Handloom Kurti')
      expect(linen?.fabric).toBe('Handloom Linen')

      const cotton = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'cotton_salwar.jpg')
      expect(cotton?.category).toBe('Kurtas & Tunics')
      expect(cotton?.fabric).toBe('Handloom Cotton')
    })

    it('classifies Outerwear variants from metadata and context hints', async () => {
      const sherwani = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'silk_sherwani.jpg')
      expect(sherwani?.category).toBe('Outerwear')
      expect(sherwani?.garmentType).toBe('Handcrafted Silk Sherwani')

      const cape = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'embroidered_cape.jpg')
      expect(cape?.category).toBe('Outerwear')
      expect(cape?.garmentType).toBe('Embroidered Cape')

      const velvetJacket = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'velvet_jacket.jpg')
      expect(velvetJacket?.category).toBe('Outerwear')
      expect(velvetJacket?.garmentType).toBe('Structured Velvet Jacket')

      const blazer = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'boutique_blazer.jpg')
      expect(blazer?.category).toBe('Outerwear')
      expect(blazer?.garmentType).toBe('Tailored Boutique Blazer')
    })

    it('classifies Drapes & Shawls variants from metadata and context hints', async () => {
      const pashmina = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'pashmina_shawl.jpg')
      expect(pashmina?.category).toBe('Drapes & Shawls')
      expect(pashmina?.garmentType).toBe('Handwoven Cashmere Shawl')

      const dupatta = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'silk_dupatta.jpg')
      expect(dupatta?.category).toBe('Drapes & Shawls')
      expect(dupatta?.garmentType).toBe('Pure Silk Dupatta')

      const stole = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'stole_drape.jpg')
      expect(stole?.category).toBe('Drapes & Shawls')
      expect(stole?.garmentType).toBe('Handwoven Cashmere Shawl')
    })

    it('classifies Jewelry & Accessories variants from metadata and context hints', async () => {
      const choker = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'polki_choker.jpg')
      expect(choker?.category).toBe('Jewelry & Accessories')
      expect(choker?.garmentType).toBe('Polki Diamond Choker')
      expect(choker?.stylingNotes).toContain('Pair with classic silk sarees')

      const necklace = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'kundan_necklace.jpg')
      expect(necklace?.category).toBe('Jewelry & Accessories')
      expect(necklace?.garmentType).toBe('Heirloom Kundan Necklace')

      const earring = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'kundan_earring.jpg')
      expect(earring?.category).toBe('Jewelry & Accessories')
      expect(earring?.garmentType).toBe('Artisan Kundan Earrings')

      const clutch = await extractVisualAttributesAndColor('data:image/jpeg;base64,xxx', 'artisan_clutch_bag.jpg')
      expect(clutch?.category).toBe('Jewelry & Accessories')
      expect(clutch?.garmentType).toBe('Artisan Minaudière Clutch')
    })
  })

  describe('inferGarmentFromMetricsAndMetadata (Geometric Silhouette Inference)', () => {
    it('infers Lehengas when flareRatio > 1.35', () => {
      const warmMetric = {
        color: { hex: '#9B111E', colorName: 'Ruby Red', r: 155, g: 17, b: 30 },
        aspectRatio: 1.5,
        topMassWidth: 10,
        midMassWidth: 20,
        bottomMassWidth: 35,
        flareRatio: 1.75,
        highFreqEdgeCount: 70,
        specularPoints: 5,
        isWarmEthnicTone: true,
        isRoyalJewelTone: false,
      }
      const res = inferWithEvidence(warmMetric, 'data:image/jpeg;base64,123')
      expect(res.category).toBe('Lehengas')
      expect(res.garmentType).toBe('Embroidered Bridal Lehenga')
      expect(res.pattern).toBe('Gold Zari Brocade')

      const coolMetric = {
        ...warmMetric,
        color: { hex: '#4169E1', colorName: 'Royal Blue', r: 65, g: 105, b: 225 },
        isWarmEthnicTone: false,
        highFreqEdgeCount: 20,
      }
      const coolRes = inferWithEvidence(coolMetric, 'data:image/jpeg;base64,123')
      expect(coolRes.category).toBe('Lehengas')
      expect(coolRes.garmentType).toBe('Flared Silk Lehenga')
      expect(coolRes.pattern).toBe('French Knot Embroidery')
    })

    it('infers Sarees when drape texture / warm ethnic / royal tones are detected', () => {
      const banarasiMetric = {
        color: { hex: '#800020', colorName: 'Royal Burgundy', r: 128, g: 0, b: 32 },
        aspectRatio: 1.4,
        topMassWidth: 20,
        midMassWidth: 20,
        bottomMassWidth: 24,
        flareRatio: 1.2,
        highFreqEdgeCount: 95,
        specularPoints: 12,
        isWarmEthnicTone: true,
        isRoyalJewelTone: false,
      }
      const banarasiRes = inferWithEvidence(banarasiMetric, 'data:image/jpeg;base64,123')
      expect(banarasiRes.category).toBe('Sarees')
      expect(banarasiRes.garmentType).toBe('Banarasi Silk Brocade Saree')

      const kanjeevaramMetric = {
        ...banarasiMetric,
        highFreqEdgeCount: 45,
      }
      const kanjeevaramRes = inferWithEvidence(kanjeevaramMetric, 'data:image/jpeg;base64,123')
      expect(kanjeevaramRes.category).toBe('Sarees')
      expect(kanjeevaramRes.garmentType).toBe('Silk Kanjeevaram Saree')
    })

    it('infers Kurtas & Tunics for fitted tunic proportions', () => {
      const tunicMetric = {
        color: { hex: '#9CAF88', colorName: 'Sage Green', r: 156, g: 175, b: 136 },
        aspectRatio: 1.2,
        topMassWidth: 20,
        midMassWidth: 20,
        bottomMassWidth: 21,
        flareRatio: 1.05,
        highFreqEdgeCount: 15,
        specularPoints: 2,
        isWarmEthnicTone: false,
        isRoyalJewelTone: false,
      }
      const res = inferWithEvidence(tunicMetric, 'data:image/jpeg;base64,123')
      expect(res.category).toBe('Kurtas & Tunics')
      expect(res.garmentType).toBe('Silk Kurta Set')
    })

    it('infers Gowns for monochrome evening vertical silhouettes', () => {
      const gownMetric = {
        color: { hex: '#121212', colorName: 'Midnight Black', r: 18, g: 18, b: 18 },
        aspectRatio: 1.6,
        topMassWidth: 20,
        midMassWidth: 20,
        bottomMassWidth: 24,
        flareRatio: 1.2,
        highFreqEdgeCount: 10,
        specularPoints: 2,
        isWarmEthnicTone: false,
        isRoyalJewelTone: false,
      }
      const res = inferWithEvidence(gownMetric, 'data:image/jpeg;base64,123')
      expect(res.category).toBe('Gowns')
      expect(res.garmentType).toBe('Luminous Evening Gown')
    })

    it('infers landscape/square framing for Jewelry or Drapes', () => {
      const jewelryMetric = {
        color: { hex: '#D4AF37', colorName: 'Antique Gold', r: 212, g: 175, b: 55 },
        aspectRatio: 0.8,
        topMassWidth: 20,
        midMassWidth: 20,
        bottomMassWidth: 20,
        flareRatio: 1.0,
        highFreqEdgeCount: 80,
        specularPoints: 25,
        isWarmEthnicTone: true,
        isRoyalJewelTone: false,
      }
      const jewRes = inferWithEvidence(jewelryMetric, 'data:image/jpeg;base64,123')
      expect(jewRes.category).toBe('Jewelry & Accessories')
      expect(jewRes.garmentType).toBe('Heirloom Kundan Necklace')

      const shawlMetric = {
        ...jewelryMetric,
        specularPoints: 4,
      }
      const shawlRes = inferWithEvidence(shawlMetric, 'data:image/jpeg;base64,123')
      expect(shawlRes.category).toBe('Drapes & Shawls')
      expect(shawlRes.garmentType).toBe('Handwoven Cashmere Shawl')
    })
  })

  describe('isolateGarmentColor', () => {
    it('reports a dark saturated green as a green, not as black', () => {
      // The regression behind this case: rgb(7,29,17) is nine points off pure black per channel,
      // and the previous RGB metric weighted green 4x, so every shadowed bottle-green pixel
      // resolved to Midnight Black.
      expect(getClosestColorName(7, 29, 17)).not.toBe('Midnight Black')
      expect(getClosestColorName(7, 29, 17)).toMatch(/Green/)
      expect(getClosestColorName(15, 38, 24)).toMatch(/Green/)
      // A genuinely neutral dark pixel must still be black.
      expect(getClosestColorName(18, 18, 18)).toBe('Midnight Black')
      expect(getClosestColorName(12, 12, 14)).toBe('Midnight Black')
    })

    it('is not fooled by a room that surrounds the garment', () => {
      // The failing upload: a green saree photographed against a cream wall, a warm wood console
      // and cane baskets. The wall and console are more saturated than the old 0.16 gate and
      // occupy more of the frame than the fabric, so a whole-frame histogram reported taupe.
      const width = 64
      const height = 64
      const buffer = frameOf(width, height, (x, y) => {
        const inGarment = x >= 22 && x <= 42 && y >= 18 && y <= 58
        if (inGarment) return [15, 42, 28] // deep bottle green
        return y < 26 ? [200, 182, 158] : [176, 138, 106] // cream wall, warm wood console
      })

      const result = isolateGarmentColor(buffer, width, height)
      expect(result).not.toBeNull()
      // Lowercase, because `<input type="color">` rejects `#RRGGBB` and silently falls back to
      // black; the form binds this value straight to the swatch.
      expect(result!.hex).toMatch(/^#[0-9a-f]{6}$/)
      expect(result!.colorName).toMatch(/Green/)
      expect(result!.colorName).not.toBe('Taupe')
      expect(result!.colorName).not.toBe('Desert Camel')
      // The green channels must dominate: this is what a taupe answer got wrong.
      expect(result!.g).toBeGreaterThan(result!.r)
      expect(result!.g).toBeGreaterThan(result!.b)
    })

    it('separates a garment from a larger background object of the same hue family', () => {
      // A saturated brown console is the nearest thing to a green garment's competitor: it passes
      // any absolute saturation gate and is bigger than the garment. The centre prior is what
      // stops it winning on raw pixel count.
      const width = 64
      const height = 64
      const buffer = frameOf(width, height, (x, y) => {
        const inGarment = x >= 24 && x <= 40 && y >= 20 && y <= 56
        return inGarment ? [26, 80, 52] : [140, 110, 80]
      })

      const result = isolateGarmentColor(buffer, width, height)
      expect(result).not.toBeNull()
      expect(result!.colorName).toMatch(/Green/)
    })

    it('resolves achromatic garments instead of returning nothing', () => {
      const width = 64
      const height = 64
      const garmentBox = (x: number, y: number) => x >= 20 && x <= 44 && y >= 16 && y <= 56

      const blackOnWhite = frameOf(width, height, (x, y) =>
        garmentBox(x, y) ? [18, 18, 18] : [245, 245, 245],
      )
      const blackResult = isolateGarmentColor(blackOnWhite, width, height)
      expect(blackResult).not.toBeNull()
      expect(getClosestColorName(blackResult!.r, blackResult!.g, blackResult!.b)).toBe('Midnight Black')

      const blackOnGrey = frameOf(width, height, (x, y) =>
        garmentBox(x, y) ? [18, 18, 18] : [210, 210, 210],
      )
      const blackOnGreyResult = isolateGarmentColor(blackOnGrey, width, height)
      expect(blackOnGreyResult).not.toBeNull()
      expect(getClosestColorName(blackOnGreyResult!.r, blackOnGreyResult!.g, blackOnGreyResult!.b)).toBe(
        'Midnight Black',
      )

      // A tinted neutral carries just enough chroma to be segmented away from a chromatic
      // surround, which is the realistic case for ivory and champagne pieces.
      const tintedIvoryOnTaupe = frameOf(width, height, (x, y) =>
        garmentBox(x, y) ? [240, 232, 210] : [120, 110, 96],
      )
      const ivoryResult = isolateGarmentColor(tintedIvoryOnTaupe, width, height)
      expect(ivoryResult).not.toBeNull()
      const resolved = getClosestColorName(ivoryResult!.r, ivoryResult!.g, ivoryResult!.b)
      expect(resolved).toBe('Heirloom Ivory')
      expect(ivoryResult!.r).toBeGreaterThan(200)
    })

    it('documents the known limit: a pure white piece on a neutral surround reads as the surround', () => {
      // Honest characterisation rather than an aspiration. `rgb(255,255,240)` is achromatic and
      // bright, so the backdrop filter (rightly, for a catalogue full of white studio walls) drops
      // it, and a darker surround is then taken for the subject. Real multimodal extraction covers
      // this case; the client heuristic cannot, and must not pretend otherwise.
      const width = 64
      const height = 64
      const pureIvoryOnDimNeutral = frameOf(width, height, (x, y) =>
        x >= 20 && x <= 44 && y >= 16 && y <= 56 ? [255, 255, 240] : [80, 76, 70],
      )
      const result = isolateGarmentColor(pureIvoryOnDimNeutral, width, height)
      expect(result).not.toBeNull()
      // It resolves to the surround, not to ivory: the limit, stated so a future change that
      // improves it is visible as a test change.
      expect(getClosestColorName(result!.r, result!.g, result!.b)).not.toBe('Heirloom Ivory')
    })

    it('returns null for a degenerate frame rather than throwing', () => {
      expect(isolateGarmentColor(new Uint8ClampedArray(0), 0, 0)).toBeNull()
      expect(isolateGarmentColor(new Uint8ClampedArray(4), 4, 4)).toBeNull()
    })

    it('keeps the garment colour when the frame halves in resolution', () => {
      // The analyser runs at a fixed SAMPLE_SIZE, so proportions rather than pixels decide. The
      // same scene at two scales must not change the answer.
      const build = (width: number, height: number) => frameOf(width, height, (x, y) => {
        const inGarment = x / width > 0.34 && x / width < 0.66 && y / height > 0.28 && y / height < 0.9
        return inGarment ? [15, 42, 28] : [190, 170, 145]
      })

      const small = isolateGarmentColor(build(64, 64), 64, 64)
      const large = isolateGarmentColor(build(160, 160), 160, 160)
      expect(small).not.toBeNull()
      expect(large).not.toBeNull()
      expect(small!.colorName).toBe(large!.colorName)
    })
  })

  describe('extractDominantColor', () => {
    it('returns null when given an empty URL', async () => {
      const result = await extractDominantColor('')
      expect(result).toBeNull()
    })

    it('returns null for an image it cannot decode, rather than a fabricated colour', async () => {
      // jsdom has no image decoder, so this payload yields no pixels and therefore no colour. The
      // test used to assert `not.toBeNull()` here and passed only because the extractor substituted
      // a hardcoded `Crimson Red` / `#DC2626` for every image it could not read; that assertion was
      // locking in the fabrication rather than testing extraction.
      const result = await extractDominantColor('data:image/png;base64,abc')
      expect(result).toBeNull()
    })
  })
})


