/**
 * Client-side visual color & garment attribute extractor using HTML5 Canvas.
 * Analyzes image pixel matrices, aspect ratios, geometric flare silhouettes,
 * texture frequency gradients, and image metadata in the browser.
 */

export interface ExtractedColorResult {
  hex: string
  colorName: string
  r: number
  g: number
  b: number
}

export interface VisualAttributesExtractionResult {
  category: 'Sarees' | 'Lehengas' | 'Gowns' | 'Kurtas & Tunics' | 'Outerwear' | 'Drapes & Shawls' | 'Jewelry & Accessories'
  garmentType: string
  suggestedItemName: string
  /**
   * The measured colour, or absent when no pixels were read. A colour *name* is never invented: the
   * previous version fell back to `'Crimson Red'` / `'#DC2626'`, so an image the browser could not
   * read still produced a confident-looking red.
   */
  colorName?: string
  hex?: string
  fabric: string
  pattern: string
  style: string
  description: string
  stylingNotes: string
  /** Absent when no pixels were read, because a confidence nothing measured is not a confidence. */
  confidenceScore?: number
  visualAttributes: string[]
}

export const COLOR_PALETTE: { name: string; r: number; g: number; b: number; hex: string }[] = [
  // Greens & Teals
  { name: 'Emerald Green', r: 15, g: 81, b: 50, hex: '#0F5132' },
  { name: 'Forest Green', r: 34, g: 139, b: 34, hex: '#228B22' },
  { name: 'Bottle Green', r: 0, g: 66, b: 37, hex: '#004225' },
  { name: 'Olive Green', r: 85, g: 107, b: 47, hex: '#556B2F' },
  { name: 'Mint Green', r: 152, g: 255, b: 152, hex: '#98FF98' },
  { name: 'Sage Green', r: 156, g: 175, b: 136, hex: '#9CAF88' },
  { name: 'Dusty Sage', r: 138, g: 154, b: 134, hex: '#8A9A86' },
  { name: 'Peacock Teal', r: 0, g: 128, b: 128, hex: '#008080' },
  { name: 'Turquoise', r: 64, g: 224, b: 208, hex: '#40E0D0' },

  // Blues
  { name: 'Midnight Navy', r: 30, g: 41, b: 59, hex: '#1E293B' },
  { name: 'Midnight Sapphire', r: 15, g: 32, b: 66, hex: '#0F2042' },
  { name: 'Navy Blue', r: 0, g: 0, b: 128, hex: '#000080' },
  { name: 'Royal Blue', r: 65, g: 105, b: 225, hex: '#4169E1' },
  { name: 'Powder Blue', r: 176, g: 224, b: 230, hex: '#B0E0E6' },
  { name: 'Sky Blue', r: 135, g: 206, b: 235, hex: '#87CEEB' },

  // Reds & Berries
  { name: 'Royal Burgundy', r: 128, g: 0, b: 32, hex: '#800020' },
  { name: 'Deep Maroon', r: 90, g: 10, b: 20, hex: '#5A0A14' },
  { name: 'Ruby Red', r: 155, g: 17, b: 30, hex: '#9B111E' },
  { name: 'Deep Crimson', r: 139, g: 0, b: 0, hex: '#8B0000' },
  { name: 'Crimson Red', r: 220, g: 38, b: 38, hex: '#DC2626' },
  { name: 'Dusty Rose', r: 220, g: 174, b: 150, hex: '#DCAE96' },
  { name: 'Rose Gold', r: 183, g: 110, b: 121, hex: '#B76E79' },
  { name: 'Blush Pink', r: 244, g: 194, b: 194, hex: '#F4C2C2' },
  { name: 'Magenta', r: 255, g: 0, b: 255, hex: '#FF00FF' },
  { name: 'Deep Plum', r: 74, g: 14, b: 78, hex: '#4A0E4E' },
  { name: 'Lavender', r: 200, g: 162, b: 200, hex: '#C8A2C8' },
  { name: 'Royal Purple', r: 88, g: 24, b: 69, hex: '#581845' },

  // Earthy & Warm
  { name: 'Burnt Terracotta', r: 226, g: 114, b: 91, hex: '#E2725B' },
  { name: 'Terracotta', r: 183, g: 65, b: 14, hex: '#B7410E' },
  { name: 'Rust', r: 183, g: 65, b: 14, hex: '#B7410E' },
  { name: 'Mustard Ochre', r: 212, g: 175, b: 55, hex: '#D4AF37' },
  { name: 'Desert Camel', r: 193, g: 154, b: 107, hex: '#C19A6B' },
  { name: 'Taupe', r: 179, g: 139, b: 109, hex: '#B38B6D' },
  { name: 'Rich Espresso', r: 61, g: 35, b: 20, hex: '#3D2314' },
  { name: 'Peach', r: 255, g: 218, b: 185, hex: '#FFDAB9' },
  { name: 'Sunset Coral', r: 255, g: 127, b: 80, hex: '#FF7F50' },

  // Metallics & Neutrals
  { name: 'Antique Gold', r: 212, g: 175, b: 55, hex: '#D4AF37' },
  { name: 'Champagne Gold', r: 247, g: 231, b: 206, hex: '#F7E7CE' },
  { name: 'Heirloom Ivory', r: 255, g: 255, b: 240, hex: '#FFFFF0' },
  { name: 'Pure White', r: 255, g: 255, b: 255, hex: '#FFFFFF' },
  { name: 'Silver', r: 192, g: 192, b: 192, hex: '#C0C0C0' },
  { name: 'Slate Grey', r: 112, g: 128, b: 144, hex: '#708090' },
  { name: 'Charcoal Grey', r: 55, g: 65, b: 81, hex: '#374151' },
  { name: 'Midnight Black', r: 18, g: 18, b: 18, hex: '#121212' },
]

/** Hue in degrees (0-360), or -1 for a fully achromatic pixel. */
function hueDegrees(r: number, g: number, b: number): number {
  const max = Math.max(r, g, b)
  const min = Math.min(r, g, b)
  const delta = max - min
  if (delta === 0) return -1

  let hue: number
  if (max === r) hue = ((g - b) / delta) % 6
  else if (max === g) hue = (b - r) / delta + 2
  else hue = (r - g) / delta + 4

  hue *= 60
  return hue < 0 ? hue + 360 : hue
}

/** HSL lightness (0-1) and saturation (0-1). */
function lightnessOf(r: number, g: number, b: number): number {
  return (Math.max(r, g, b) + Math.min(r, g, b)) / 510
}

function saturationOf(r: number, g: number, b: number): number {
  const max = Math.max(r, g, b)
  if (max === 0) return 0
  const min = Math.min(r, g, b)
  const sat = (max - min) / max
  // Dampen saturation heavily for dark pixels where noise/ambient tint creates false saturation
  return max < 120 ? sat * (max / 120) : sat
}

/**
 * How much a hue difference of this many degrees is allowed to weigh against a lightness
 * difference. Hue dominates for saturated fabric; the term is skipped entirely for greys.
 */
const HUE_WEIGHT = 1.3

/**
 * Finds the nearest human-readable colour name.
 *
 * Hue is compared before lightness, which is what makes a deep emerald resolve to a green
 * rather than to black. The previous formulation compared raw RGB with a 4x weight on the green
 * channel, so a shadowed bottle-green pixel `rgb(7,29,17)` sat closer to `Midnight Black`
 * `rgb(18,18,18)` than to any green swatch: the difference is `(11,11,1)`, giving the black
 * swatch almost no penalty while the green channel's 11-point gap cost 4x. Dark, saturated
 * garments are exactly the case a boutique catalogs most, so the metric now treats hue as the
 * primary signal and only falls back to lightness for achromatic pixels.
 */
export function getClosestColorName(r: number, g: number, b: number): string {
  const saturation = saturationOf(r, g, b)
  const lightness = lightnessOf(r, g, b)
  const hue = hueDegrees(r, g, b)

  let closest = COLOR_PALETTE[0].name
  let minDistance = Infinity
  let closestAchromatic = COLOR_PALETTE[0].name
  let minAchromaticDistance = Infinity

  for (const item of COLOR_PALETTE) {
    const itemLightness = lightnessOf(item.r, item.g, item.b)
    const itemSaturation = saturationOf(item.r, item.g, item.b)

    // A grey pixel cannot carry hue information, so track the best grey-only match as well. Only
    // genuinely grey swatches compete: without the saturation guard a near-black pixel took
    // `Navy Blue` on lightness alone, because a navy swatch is darker than black is light.
    const achromaticSwatch = itemSaturation < 0.15
    const achromaticDistance = Math.abs(lightness - itemLightness) * 2
    if (achromaticSwatch && achromaticDistance < minAchromaticDistance) {
      minAchromaticDistance = achromaticDistance
      closestAchromatic = item.name
    }

    // If the swatch is achromatic, it can only compete on lightness.
    // If the pixel itself is effectively grey (saturation < 0.15), it ALSO only competes on lightness
    // against other achromatic swatches. It should not be compared against chromatic swatches using hue.
    if (achromaticSwatch) {
      // We already tracked it in closestAchromatic. We don't evaluate hue distance for it.
      continue
    }
    if (saturation < 0.15) {
      // The pixel is grey. It has no hue. We don't compare it against chromatic swatches.
      continue
    }

    let deltaHue = Math.abs(hue - hueDegrees(item.r, item.g, item.b))
    if (deltaHue > 180) deltaHue = 360 - deltaHue

    const distance = (deltaHue / 180) * HUE_WEIGHT + Math.abs(lightness - itemLightness)
    if (distance < minDistance) {
      minDistance = distance
      closest = item.name
    }
  }

  // Every swatch was chromatic and the pixel was grey: fall back to the closest grey swatch.
  return minDistance === Infinity ? closestAchromatic : closest
}

export interface CanvasAnalysisMetrics {
  color: ExtractedColorResult
  aspectRatio: number
  topMassWidth: number
  midMassWidth: number
  bottomMassWidth: number
  flareRatio: number
  highFreqEdgeCount: number
  specularPoints: number
  isWarmEthnicTone: boolean
  isRoyalJewelTone: boolean
  /** Pixels in the isolated garment segment; 0 when the achromatic fallback was used. */
  garmentPixelCount?: number
  /** True when no chromatic garment segment was found (black or ivory pieces). */
  garmentAchromatic?: boolean
}

/** Formats channels as a lowercase `#rrggbb`, the casing `<input type="color">` accepts. */
function toHex(r: number, g: number, b: number): string {
  return `#${((1 << 24) + (r << 16) + (g << 8) + b).toString(16).slice(1).toLowerCase()}`
}

/** The isolated garment colour plus the evidence behind it. */
export interface IsolatedGarmentColor {
  r: number
  g: number
  b: number
  hex: string
  colorName: string
  /** Pixels retained in the winning segment. */
  pixelCount: number
  /** Hue family of the segment in degrees, or -1 when achromatic. */
  familyHue: number
  achromatic: boolean
}

function isBackdropPixel(r: number, g: number, b: number): boolean {
  const saturation = saturationOf(r, g, b)
  const luminance = 0.299 * r + 0.587 * g + 0.114 * b
  // Neutral studio backdrops (white, light grey) and extreme glare/shadow.
  // Aggressively filtering blacks (luminance < 14) or greys caused black/grey 
  // garments to be ignored, leaving only skin tones (Dusty Rose) to be detected.
  return (
    (saturation < 0.12 && luminance > 220) ||
    luminance > 250
  )
}

/**
 * A centre-weighted prior over where a worn garment sits in a catalogue photo.
 *
 * This is what stops a full-bleed mirror selfie from being classified by its room. The failing
 * case that motivated it: a green saree shot in a bedroom where the cream wall and warm wood
 * console are more saturated than the `0.16` gate the old sampler used and occupy far more of
 * the frame than the fabric does. The old code binned the entire frame with a mild centre
 * weight, so the wall won on pixel count and the saree was reported as taupe.
 *
 * Bounded so that a garment filling the frame is unaffected, and wide enough that a garment
 * hanging off-centre still qualifies.
 */
function passesRegionPrior(x: number, y: number, width: number, height: number): boolean {
  const nx = (x / width - 0.5) / 0.3
  const ny = (y / height - 0.52) / 0.38
  return nx * nx + ny * ny <= 1.6
}

/**
 * Segments the garment out of the frame and returns its colour.
 *
 * Each stage addresses a distinct way a whole-frame histogram went wrong:
 *
 * 1. A **relative** saturation floor. An absolute gate cannot separate a cream garment from a
 *    cream wall, but a garment is reliably more saturated than its surroundings, so the gate
 *    scales with the most saturated pixels present.
 * 2. **Connected-component** selection scored by size, centre prior, and how *solid* the segment
 *    is at the centre of the frame. Solidity is what stops a wall or console that merely rings
 *    the subject from winning on pixel count.
 * 3. **Hue-family modal colour at a brightness percentile** inside the winning segment. A plain
 *    mean of the segment is dragged toward shadow by folds and mirror shading; the modal hue
 *    family's percentile reports the fabric's actual colour.
 *
 * The frame is segmented twice, once for chromatic fabric and once for neutral fabric, and the
 * better-scoring segment wins. Scored on solidity, the two do not compete wrongly: the neutral
 * mask of the failing mirror selfie collects only scattered grey, while its chromatic mask finds
 * the saree. A black or ivory piece is the reverse.
 *
 * Returns `null` only when the frame yields no usable pixels at all.
 */
export function isolateGarmentColor(
  data: Uint8ClampedArray,
  width: number,
  height: number,
): IsolatedGarmentColor | null {
  const total = width * height
  if (total === 0 || data.length < total * 4) return null

  // Stage 1: the relative saturation floor, from the most saturated pixels present.
  let maxSaturation = 0
  for (let p = 0; p < total; p++) {
    const i = p * 4
    if (data[i + 3] < 128) continue
    const saturation = saturationOf(data[i], data[i + 1], data[i + 2])
    if (saturation > maxSaturation) maxSaturation = saturation
  }
  const saturationFloor = Math.max(0.14, Math.min(0.55, maxSaturation * 0.72))

  const chromaticMask = new Uint8Array(total)

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const p = y * width + x
      const i = p * 4
      if (data[i + 3] < 128) continue
      const r = data[i]
      const g = data[i + 1]
      const b = data[i + 2]
      if (isBackdropPixel(r, g, b)) continue
      if (!passesRegionPrior(x, y, width, height)) continue
      if (saturationOf(r, g, b) >= saturationFloor) chromaticMask[p] = 1
    }
  }

  const chromatic = strongestSegment(data, chromaticMask, width, height, total)

  // The neutral pass is for neutral fabric, so it must not simply collect everything the
  // chromatic gate rejected: in the failing mirror selfie that is the *entire room*, and the wood
  // console alone then beats the saree on pixel count. Pixels close to the chromatic winner's
  // colour are that garment's shadowed side, not a second subject, so they are excluded too.
  const chromaticMean: [number, number, number] | null = chromatic
    ? [chromatic.color.r, chromatic.color.g, chromatic.color.b]
    : null

  const neutralMask = new Uint8Array(total)
  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const p = y * width + x
      const i = p * 4
      if (data[i + 3] < 128) continue
      const r = data[i]
      const g = data[i + 1]
      const b = data[i + 2]
      if (isBackdropPixel(r, g, b)) continue
      if (!passesRegionPrior(x, y, width, height)) continue
      if (saturationOf(r, g, b) >= saturationFloor) continue
      if (chromaticMean !== null) {
        const dr = r - chromaticMean[0]
        const dg = g - chromaticMean[1]
        const db = b - chromaticMean[2]
        // Same hue family as the chromatic winner: part of that garment's shading, not a subject.
        if (dr * dr + dg * dg + db * db < 140 * 140) continue
      }
      neutralMask[p] = 1
    }
  }
  const neutral = strongestSegment(data, neutralMask, width, height, total)

  // Prefer the solidly-centred segment; fall back only when one side found nothing at all.
  let best: IsolatedGarmentColor | null
  if (chromatic !== null && neutral !== null) {
    best = neutral.score > chromatic.score ? neutral.color : chromatic.color
  } else {
    best = chromatic?.color ?? neutral?.color ?? null
  }

  // Nothing segmented: a near-greyscale frame. Decide by whether the centre is darker or lighter
  // than its surroundings, which is the only signal left.
  return best ?? isolateAchromaticColor(data, width, height)
}

/**
 * Flood-fills every colour-tolerant component in a mask and returns the colour of the
 * best-scoring one, or `null` when the mask is empty.
 */
function strongestSegment(
  data: Uint8ClampedArray,
  mask: Uint8Array,
  width: number,
  height: number,
  total: number,
): { color: IsolatedGarmentColor; score: number } | null {
  const labels = new Int32Array(total).fill(-1)
  const components: {
    id: number
    count: number
    cx: number
    cy: number
    centreRatio: number
    fillRatio: number
  }[] = []
  const stack: number[] = []

  for (let seedY = 0; seedY < height; seedY++) {
    for (let seedX = 0; seedX < width; seedX++) {
      const seed = seedY * width + seedX
      if (!mask[seed] || labels[seed] !== -1) continue

      const id = components.length
      let count = 0
      let sumX = 0
      let sumY = 0
      let minX = width
      let maxX = -1
      let minY = height
      let maxY = -1
      stack.length = 0
      stack.push(seed)
      labels[seed] = id

      while (stack.length > 0) {
        const p = stack.pop() as number
        const py = (p / width) | 0
        const px = p % width
        const pi = p * 4
        count++
        sumX += px
        sumY += py
        if (px < minX) minX = px
        if (px > maxX) maxX = px
        if (py < minY) minY = py
        if (py > maxY) maxY = py

        for (let k = 0; k < 4; k++) {
          const nx = px + (k === 0 ? 1 : k === 1 ? -1 : 0)
          const ny = py + (k === 2 ? 1 : k === 3 ? -1 : 0)
          if (nx < 0 || ny < 0 || nx >= width || ny >= height) continue
          const q = ny * width + nx
          if (!mask[q] || labels[q] !== -1) continue
          const qi = q * 4
          const dr = data[qi] - data[pi]
          const dg = data[qi + 1] - data[pi + 1]
          const db = data[qi + 2] - data[pi + 2]
          // Grow across folds and shading, but not across a colour boundary.
          // A strict limit is required so contrasting embroidery (like gold motifs on black)
          // does not merge into the base fabric and hijack the hue extraction.
          if (dr * dr + dg * dg + db * db > 90 * 90) continue
          labels[q] = id
          stack.push(q)
        }
      }

      // Two shape tests, because a pixel count alone cannot tell a garment from a room.
      // `centreRatio`: a garment fills the middle of the frame; a wall that merely *surrounds* the
      // garment is a ring. `fillRatio`: a garment fills its own bounding box; the ring does not.
      let withinCentre = 0
      for (let y = minY; y <= maxY; y++) {
        for (let x = minX; x <= maxX; x++) {
          const p = y * width + x
          if (labels[p] !== id) continue
          const dx = (x / width - 0.5) / 0.2
          const dy = (y / height - 0.55) / 0.25
          if (dx * dx + dy * dy <= 1) withinCentre++
        }
      }
      const boundingBoxArea = (maxX - minX + 1) * (maxY - minY + 1)

      components.push({
        id,
        count,
        cx: sumX / count / width,
        cy: sumY / count / height,
        centreRatio: withinCentre / count,
        fillRatio: count / boundingBoxArea,
      })
    }
  }

  if (components.length === 0) return null

  const segmentScore = (c: {
    count: number
    cx: number
    cy: number
    centreRatio: number
    fillRatio: number
  }): number => {
    // A background wall forms a ring/horseshoe around the model, meaning it has huge count 
    // but almost no pixels in the dead center (since the model blocks it).
    if (c.centreRatio < 0.15) return 0
    
    const dx = (c.cx - 0.5) / 0.42
    const dy = (c.cy - 0.62) / 0.45
    // Cubing centreRatio brutally destroys background walls that wrap around the model.
    // They have a huge pixel count and a perfectly centered centroid, but their actual pixels
    // are distributed outside the center ellipse, so centreRatio is tiny (e.g. 0.15).
    // A true garment sits squarely in the center and has a centreRatio near 1.0.
    return c.count * Math.exp(-(dx * dx + dy * dy)) * Math.pow(c.centreRatio, 3) * c.fillRatio
  }

  let best = components[0]
  let bestScore = segmentScore(best)
  for (const component of components) {
    const score = segmentScore(component)
    if (score > bestScore) {
      bestScore = score
      best = component
    }
  }

  // Stage 3: the winning segment's hue family, reported at a brightness percentile.
  const color = modalColorOfMask(data, labels, best.id, total)
  if (color === null) return null
  return { color: { ...color, pixelCount: best.count }, score: bestScore }
}

/**
 * The garment's hue family: the modal saturated colour inside the mask, then the pixels sharing
 * that hue, reported at a brightness percentile. A plain mean of the segment is dragged toward
 * shadow by folds and mirror shading; the modal family's percentile reports the readable fabric
 * colour instead.
 */
function modalColorOfMask(
  data: Uint8ClampedArray,
  mask: Uint8Array | Int32Array,
  expectedIndex: number,
  total: number,
): IsolatedGarmentColor | null {
  const familyBins = new Map<string, { count: number; r: number; g: number; b: number }>()
  const members: [number, number, number][] = []

  for (let p = 0; p < total; p++) {
    if (mask[p] !== expectedIndex) continue
    const i = p * 4
    const r = data[i]
    const g = data[i + 1]
    const b = data[i + 2]
    members.push([r, g, b])
    const key = `${(r / 32) | 0}_${(g / 32) | 0}_${(b / 32) | 0}`
    const bin = familyBins.get(key) ?? { count: 0, r: 0, g: 0, b: 0 }
    bin.count++
    bin.r += r
    bin.g += g
    bin.b += b
    familyBins.set(key, bin)
  }

  if (members.length === 0) return null

  let peak: { count: number; r: number; g: number; b: number } | null = null
  for (const bin of familyBins.values()) {
    if (peak === null || bin.count > peak.count) peak = bin
  }
  const peakBin = peak as { count: number; r: number; g: number; b: number }
  const familyHue = hueDegrees(
    peakBin.r / peakBin.count,
    peakBin.g / peakBin.count,
    peakBin.b / peakBin.count,
  )

  // Keep only pixels sharing the dominant hue, so a stray highlight or shadow cluster cannot
  // shift the reported colour.
  let family = members.filter(([r, g, b]) => {
    const hue = hueDegrees(r, g, b)
    if (hue < 0 || familyHue < 0) return false
    let delta = Math.abs(hue - familyHue)
    if (delta > 180) delta = 360 - delta
    return delta <= 22
  })
  if (family.length < 8) family = members

  family.sort(
    (a, b) =>
      a[0] * 0.299 + a[1] * 0.587 + a[2] * 0.114 - (b[0] * 0.299 + b[1] * 0.587 + b[2] * 0.114),
  )
  // A percentile rather than a mean: folds and mirror shading push the mean toward shadow, while
  // the fabric's readable colour sits above it.
  const [r, g, b] = family[Math.min(family.length - 1, Math.floor(0.72 * (family.length - 1)))]

  return {
    r,
    g,
    b,
    hex: toHex(r, g, b),
    colorName: getClosestColorName(r, g, b),
    pixelCount: members.length,
    familyHue,
    achromatic: false,
  }
}

/** Fallback for black/ivory garments: darker-or-lighter than surroundings, by percentile. */
function isolateAchromaticColor(
  data: Uint8ClampedArray,
  width: number,
  height: number,
): IsolatedGarmentColor | null {
  const centre: [number, number, number][] = []
  const surround: [number, number, number][] = []

  for (let y = 0; y < height; y++) {
    for (let x = 0; x < width; x++) {
      const i = (y * width + x) * 4
      if (data[i + 3] < 128) continue
      const r = data[i]
      const g = data[i + 1]
      const b = data[i + 2]
      const nx = (x / width - 0.5) / 0.28
      const ny = (y / height - 0.55) / 0.35
      if (nx * nx + ny * ny <= 1) centre.push([r, g, b])
      else surround.push([r, g, b])
    }
  }

  if (centre.length === 0 || surround.length === 0) return null

  const meanLuminance = (pixels: [number, number, number][]): number =>
    pixels.reduce((sum, [r, g, b]) => sum + 0.299 * r + 0.587 * g + 0.114 * b, 0) / pixels.length

  const centreIsSubject = meanLuminance(centre) < meanLuminance(surround)
  const pool = centreIsSubject ? centre : surround
  pool.sort((a, b) => a[0] * 0.299 + a[1] * 0.587 + a[2] * 0.114 - (b[0] * 0.299 + b[1] * 0.587 + b[2] * 0.114))

  // Nearest quartile to the subject's own tone, to shed antialiased edge pixels either way.
  const chosen = centreIsSubject
    ? pool[Math.floor(pool.length * 0.25)]
    : pool[Math.floor(pool.length * 0.75)]
  const [r, g, b] = chosen

  return {
    r,
    g,
    b,
    hex: toHex(r, g, b),
    colorName: getClosestColorName(r, g, b),
    pixelCount: pool.length,
    familyHue: -1,
    achromatic: true,
  }
}

export function analyzeCanvasMetrics(ctx: CanvasRenderingContext2D, width: number, height: number): CanvasAnalysisMetrics | null {
  try {
    const imageData = ctx.getImageData(0, 0, width, height)
    const data = imageData.data
    if (width === 0 || height === 0 || data.length < width * height * 4) return null

    // Silhouette geometry is measured over every non-backdrop pixel, including the garment.
    let topMass = 0
    let midMass = 0
    let bottomMass = 0
    let highFreqEdges = 0
    let specularHighlights = 0

    const topBound = Math.floor(height * 0.33)
    const midBound = Math.floor(height * 0.66)

    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        const i = (y * width + x) * 4
        const r = data[i]
        const g = data[i + 1]
        const b = data[i + 2]
        if (data[i + 3] < 128) continue

        const saturation = saturationOf(r, g, b)
        const luminance = 0.299 * r + 0.587 * g + 0.114 * b

        if (luminance > 210 && (r > 185 || g > 165) && saturation > 0.18) specularHighlights++

        if (isBackdropPixel(r, g, b)) continue

        if (y < topBound) topMass++
        else if (y < midBound) midMass++
        else bottomMass++

        if (x > 0 && y > 0) {
          const prevIndex = ((y - 1) * width + (x - 1)) * 4
          const prevLuminance =
            0.299 * data[prevIndex] + 0.587 * data[prevIndex + 1] + 0.114 * data[prevIndex + 2]
          if (Math.abs(luminance - prevLuminance) > 30) highFreqEdges++
        }
      }
    }

    // Colour comes from the isolated garment, never from the frame as a whole.
    const garment = isolateGarmentColor(data, width, height)
    if (garment === null) return null

    const { r, g, b } = garment
    const flareRatio = midMass > 0 ? bottomMass / midMass : 1.0

    const isRedOrMaroon = r > g + 20 && r > b + 20
    const isGoldOrMustard = r > 150 && g > 120 && b < 110
    const isPinkOrRose = r > 170 && b > 110 && g < r - 20
    const isWarmEthnicTone =
      isRedOrMaroon ||
      isGoldOrMustard ||
      isPinkOrRose ||
      garment.colorName.includes('Red') ||
      garment.colorName.includes('Crimson') ||
      garment.colorName.includes('Maroon') ||
      garment.colorName.includes('Burgundy') ||
      garment.colorName.includes('Gold') ||
      garment.colorName.includes('Rose') ||
      garment.colorName.includes('Pink') ||
      garment.colorName.includes('Rust') ||
      garment.colorName.includes('Coral')

    const isRoyalJewelTone =
      (b > r + 25 && b > g + 15) ||
      (g > r + 20 && g > b + 15) ||
      garment.colorName.includes('Sapphire') ||
      garment.colorName.includes('Navy') ||
      garment.colorName.includes('Emerald') ||
      garment.colorName.includes('Bottle') ||
      garment.colorName.includes('Forest') ||
      garment.colorName.includes('Teal')

    return {
      color: { hex: garment.hex, colorName: garment.colorName, r, g, b },
      aspectRatio: height / Math.max(1, width),
      topMassWidth: topMass,
      midMassWidth: midMass,
      bottomMassWidth: bottomMass,
      flareRatio,
      highFreqEdgeCount: highFreqEdges,
      specularPoints: specularHighlights,
      isWarmEthnicTone,
      isRoyalJewelTone,
      garmentPixelCount: garment.pixelCount,
      garmentAchromatic: garment.achromatic,
    }
  } catch {
    return null
  }
}

/**
 * Internal sampling resolution. The segmenter needs enough spatial resolution for connected
 * components to separate a garment from furniture and wall: at the previous 128px a saree's
 * drape and the console behind it merged into one blob.
 */
const SAMPLE_SIZE = 256

/** Loads a URL onto a canvas and runs the analysis; resolves null on any load or CORS failure. */
function loadAndAnalyze(imageUrl: string, useCrossOrigin: boolean): Promise<CanvasAnalysisMetrics | null> {
  return new Promise<CanvasAnalysisMetrics | null>((resolve) => {
    const img = new Image()
    if (useCrossOrigin) img.crossOrigin = 'anonymous'
    img.onload = () => {
      try {
        const canvas = document.createElement('canvas')
        canvas.width = SAMPLE_SIZE
        canvas.height = SAMPLE_SIZE
        const ctx = canvas.getContext('2d', { willReadFrequently: true })
        if (!ctx) return resolve(null)
        ctx.drawImage(img, 0, 0, SAMPLE_SIZE, SAMPLE_SIZE)
        resolve(analyzeCanvasMetrics(ctx, SAMPLE_SIZE, SAMPLE_SIZE))
      } catch {
        resolve(null)
      }
    }
    img.onerror = () => resolve(null)
    img.src = imageUrl
  })
}

/**
 * Samples pixels from an image element or URL to extract authentic dominant fabric color.
 */
export async function extractDominantColor(imageUrl: string): Promise<IsolatedGarmentColor | null> {
  const fullResult = await extractVisualAttributesAndColor(imageUrl)
  // No pixels were read, so there is no colour to report. Returning the extractor's fabricated
  // default here is what made an unreadable image look like a confidently-measured crimson one.
  if (!fullResult?.hex || !fullResult.colorName) return null
  return {
    hex: fullResult.hex,
    colorName: fullResult.colorName,
    // Read the actual channels back off the resolved hex. These were previously hardcoded to the
    // emerald swatch, so every caller received `rgb(15,81,50)` no matter what the image showed.
    ...hexToRgb(fullResult.hex),
    pixelCount: 0,
    familyHue: -1,
    achromatic: false,
  }
}

/** Parses `#RRGGBB` back into channels, so callers never desynchronise from the hex. */
export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const normalized = hex.replace('#', '')
  const value = Number.parseInt(normalized.length === 3
    ? normalized.split('').map((c) => c + c).join('')
    : normalized, 16)
  if (Number.isNaN(value)) return { r: 0, g: 0, b: 0 }
  return { r: (value >> 16) & 255, g: (value >> 8) & 255, b: value & 255 }
}

/**
 * High-precision visual attribute & cloth classifier analyzing canvas geometry,
 * flare proportions, micro-texture gradients, and file metadata.
 */
export async function extractVisualAttributesAndColor(
  imageUrl: string,
  fileName?: string,
  contextHint?: string,
): Promise<VisualAttributesExtractionResult | null> {
  if (!imageUrl) return null

  // 1. Load image into Canvas to inspect pixel geometry when browser DOM is available
  let metrics: CanvasAnalysisMetrics | null = null

  if (typeof window !== 'undefined' && typeof document !== 'undefined') {
    try {
      // Only set crossOrigin for remote HTTP URLs to prevent a tainted canvas on data URLs.
      const isRemote = imageUrl.startsWith('http://') || imageUrl.startsWith('https://')
      metrics = await loadAndAnalyze(imageUrl, isRemote)
    } catch {
      // Continue with metadata parsing
    }
  }

  return inferGarmentFromMetricsAndMetadata(metrics, imageUrl, fileName, contextHint)
}

/**
 * Pure classifier synthesizing canvas metrics, pixel geometry, aspect ratios,
 * and metadata keywords to produce visual attributes.
 */
export function inferGarmentFromMetricsAndMetadata(
  metrics: CanvasAnalysisMetrics | null,
  imageUrl: string,
  fileName?: string,
  contextHint?: string,
): VisualAttributesExtractionResult | null {
  // Synthesize semantic metadata cues from filename & URL
  const rawLower = imageUrl.startsWith('data:') ? '' : imageUrl.toLowerCase()
  const combined = `${rawLower} ${fileName || ''} ${contextHint || ''}`.toLowerCase()

  let detectedCategory: 'Sarees' | 'Lehengas' | 'Gowns' | 'Kurtas & Tunics' | 'Outerwear' | 'Drapes & Shawls' | 'Jewelry & Accessories' = 'Sarees'
  let detectedGarment = 'Silk Kanjeevaram Saree'
  let detectedFabric = 'Pure Mulberry Silk'
  let detectedPattern = 'Gold Zari Brocade'
  let detectedStyle = 'Traditional Heirloom'

  // Explicit Keyword Matching
  if (
    combined.includes('lehenga') ||
    combined.includes('ghagra') ||
    combined.includes('choli')
  ) {
    detectedCategory = 'Lehengas'
    detectedGarment = combined.includes('bridal') ? 'Embroidered Bridal Lehenga'
      : combined.includes('chevron') ? 'Chevron Embroidered Lehenga'
      : combined.includes('velvet') ? 'Velvet Bridal Lehenga'
      : 'Flared Silk Lehenga'
    detectedFabric = combined.includes('velvet') ? 'Micro Velvet' : 'Pure Mulberry Silk'
    detectedPattern = combined.includes('zari') ? 'Gold Zari Brocade' : 'French Knot Embroidery'
    detectedStyle = 'Royal Bridal'
  } else if (
    combined.includes('saree') ||
    combined.includes('sari') ||
    combined.includes('kanjeevaram') ||
    combined.includes('banarasi') ||
    combined.includes('chanderi') ||
    combined.includes('pallu')
  ) {
    detectedCategory = 'Sarees'
    detectedGarment = combined.includes('banarasi') ? 'Banarasi Silk Brocade Saree'
      : combined.includes('chanderi') ? 'Chanderi Handloom Saree'
      : combined.includes('organza') ? 'Floral Organza Saree'
      : 'Silk Kanjeevaram Saree'
    detectedFabric = combined.includes('organza') ? 'Pure Organza'
      : combined.includes('banarasi') ? 'Banarasi Brocade'
      : 'Pure Mulberry Silk'
    detectedPattern = combined.includes('floral') ? 'Botanical Floral Weave' : 'Gold Zari Brocade'
    detectedStyle = 'Traditional Heirloom'
  } else if (
    combined.includes('gown') ||
    combined.includes('maxi') ||
    combined.includes('ballgown') ||
    combined.includes('mermaid') ||
    combined.includes('cocktail dress') ||
    combined.includes('evening dress') ||
    combined.includes('dress')
  ) {
    detectedCategory = 'Gowns'
    detectedGarment = combined.includes('ballgown') ? 'Luminous Silk Ballgown'
      : combined.includes('mermaid') ? 'Mermaid Evening Gown'
      : combined.includes('organza') ? 'Luminous Organza Evening Gown'
      : combined.includes('velvet') ? 'Velvet Evening Gown'
      : 'Luminous Evening Gown'
    detectedFabric = combined.includes('organza') ? 'Pure Organza'
      : combined.includes('chiffon') ? 'Pure Chiffon'
      : combined.includes('velvet') ? 'Micro Velvet'
      : 'Duchess Satin'
    detectedPattern = 'Solid Satin Sheen'
    detectedStyle = 'Contemporary Luxe'
  } else if (
    combined.includes('kurta') ||
    combined.includes('kurti') ||
    combined.includes('anarkali') ||
    combined.includes('tunic') ||
    combined.includes('salwar') ||
    combined.includes('blouse')
  ) {
    detectedCategory = 'Kurtas & Tunics'
    detectedGarment = combined.includes('anarkali') ? 'Anarkali Kurta & Tunic'
      : combined.includes('blouse') ? 'Embroidered Silk Blouse'
      : 'Straight Handloom Kurti'
    detectedFabric = combined.includes('linen') ? 'Handloom Linen'
      : combined.includes('cotton') ? 'Handloom Cotton'
      : 'Pure Mulberry Silk'
    detectedPattern = combined.includes('chikankari') ? 'Chikankari Motif' : 'Handloom Weave'
    detectedStyle = 'Contemporary Luxe'
  } else if (
    combined.includes('blazer') ||
    combined.includes('jacket') ||
    combined.includes('coat') ||
    combined.includes('outerwear') ||
    combined.includes('cape') ||
    combined.includes('sherwani')
  ) {
    detectedCategory = 'Outerwear'
    detectedGarment = combined.includes('sherwani') ? 'Handcrafted Silk Sherwani'
      : combined.includes('cape') ? 'Embroidered Cape'
      : combined.includes('velvet') ? 'Structured Velvet Jacket'
      : 'Tailored Boutique Blazer'
    detectedFabric = combined.includes('velvet') ? 'Micro Velvet' : 'Pure Mulberry Silk'
    detectedPattern = 'Solid Satin Sheen'
    detectedStyle = 'Contemporary Luxe'
  } else if (
    combined.includes('shawl') ||
    combined.includes('dupatta') ||
    combined.includes('stole') ||
    combined.includes('scarf') ||
    combined.includes('drape') ||
    combined.includes('pashmina')
  ) {
    detectedCategory = 'Drapes & Shawls'
    detectedGarment = combined.includes('pashmina') || combined.includes('cashmere') ? 'Handwoven Cashmere Shawl'
      : combined.includes('dupatta') ? 'Pure Silk Dupatta'
      : 'Handwoven Cashmere Shawl'
    detectedFabric = 'Cashmere Pashmina'
    detectedPattern = 'Gold Zari Brocade'
    detectedStyle = 'Traditional Heirloom'
  } else if (
    combined.includes('jewelry') ||
    combined.includes('necklace') ||
    combined.includes('earring') ||
    combined.includes('bangle') ||
    combined.includes('choker') ||
    combined.includes('kundan') ||
    combined.includes('clutch') ||
    combined.includes('bag')
  ) {
    detectedCategory = 'Jewelry & Accessories'
    detectedGarment = combined.includes('choker') ? 'Polki Diamond Choker'
      : combined.includes('necklace') ? 'Heirloom Kundan Necklace'
      : combined.includes('earring') ? 'Artisan Kundan Earrings'
      : combined.includes('clutch') || combined.includes('bag') ? 'Artisan Minaudière Clutch'
      : 'Heirloom Kundan Necklace'
    detectedFabric = '22K Gold & Precious Gems'
    detectedPattern = 'Handcrafted Embellishment'
    detectedStyle = 'Traditional Heirloom'
  } else if (metrics) {
    // 3. Autonomous Geometric Silhouette & Texture Inference (when no filename keywords exist)
    if (metrics.aspectRatio >= 1.0) {
      // Full-length vertical garment
      if (metrics.flareRatio > 1.35) {
        // High bottom flare -> Lehenga
        detectedCategory = 'Lehengas'
        detectedGarment = metrics.isWarmEthnicTone ? 'Embroidered Bridal Lehenga' : 'Flared Silk Lehenga'
        detectedFabric = metrics.isWarmEthnicTone ? 'Micro Velvet' : 'Pure Mulberry Silk'
        detectedPattern = metrics.highFreqEdgeCount > 60 ? 'Gold Zari Brocade' : 'French Knot Embroidery'
        detectedStyle = 'Royal Bridal'
      } else if (metrics.isWarmEthnicTone || metrics.highFreqEdgeCount > 40 || metrics.specularPoints > 10 || metrics.isRoyalJewelTone) {
        // Traditional drape silhouette with zari / rich ethnic palette -> Saree
        detectedCategory = 'Sarees'
        detectedGarment = metrics.highFreqEdgeCount > 80 ? 'Banarasi Silk Brocade Saree' : 'Silk Kanjeevaram Saree'
        detectedFabric = 'Pure Mulberry Silk'
        detectedPattern = 'Gold Zari Brocade'
        detectedStyle = 'Traditional Heirloom'
      } else if (metrics.flareRatio < 1.15 && metrics.aspectRatio < 1.3) {
        // Fitted/tunic proportion -> Kurti / Kurta Set
        detectedCategory = 'Kurtas & Tunics'
        detectedGarment = 'Silk Kurta Set'
        detectedFabric = 'Handloom Chanderi Silk'
        detectedPattern = 'Woven Buta Motif'
        detectedStyle = 'Contemporary Luxe'
      } else {
        // Contemporary monochrome evening silhouette -> Gown
        detectedCategory = 'Gowns'
        detectedGarment = 'Luminous Evening Gown'
        detectedFabric = 'Duchess Satin'
        detectedPattern = 'Solid Satin Sheen'
        detectedStyle = 'Contemporary Luxe'
      }
    } else {
      // Landscape or square framing (accessories, shawls, jewelry)
      if (metrics.specularPoints > 15) {
        detectedCategory = 'Jewelry & Accessories'
        detectedGarment = 'Heirloom Kundan Necklace'
        detectedFabric = '22K Gold & Precious Gems'
        detectedPattern = 'Handcrafted Embellishment'
        detectedStyle = 'Traditional Heirloom'
      } else {
        detectedCategory = 'Drapes & Shawls'
        detectedGarment = 'Handwoven Cashmere Shawl'
        detectedFabric = 'Cashmere Pashmina'
        detectedPattern = 'Gold Zari Brocade'
        detectedStyle = 'Traditional Heirloom'
      }
    }
  } else {
    // Neither pixel geometry nor a filename/metadata keyword: there is nothing to infer. Returning
    // the seeded defaults here produced a complete, confident-looking analysis ("Crimson Red Pure
    // Mulberry Silk") of an image the browser never read — a tainted canvas, a CORS-blocked URL, a
    // decode failure or SSR. Absence of evidence is reported as absence.
    return null
  }

  // Authentic Color Resolution: the pixels, or nothing. The previous `|| 'Crimson Red'` /
  // `|| '#DC2626'` pair invented a colour for every unreadable image.
  const resolvedColorName = metrics?.color?.colorName
  const resolvedHex = metrics?.color?.hex

  const colourPrefix = resolvedColorName ? `${resolvedColorName} ` : ''
  const suggestedItemName = `${colourPrefix}${detectedFabric} ${detectedGarment}`.replace(/\s+/g, ' ').trim()

  const description = `Exquisite ${resolvedColorName ? `${resolvedColorName.toLowerCase()} ` : ''}${detectedGarment.toLowerCase()} crafted from premium ${detectedFabric.toLowerCase()} featuring a refined ${detectedPattern.toLowerCase()} aesthetic with fluid drape. Designed with timeless boutique elegance, ideal for celebratory soirees.`
  const stylingNotes = detectedCategory === 'Jewelry & Accessories'
    ? 'Pair with classic silk sarees or deep neckline evening gowns for maximum brilliance.'
    : 'Pair with fine artisan jewelry, tonal evening accessories, and structured footwear for a polished boutique statement.'

  return {
    category: detectedCategory,
    garmentType: detectedGarment,
    suggestedItemName,
    colorName: resolvedColorName,
    hex: resolvedHex,
    fabric: detectedFabric,
    pattern: detectedPattern,
    style: detectedStyle,
    description: `${description} Styling: ${stylingNotes}`,
    stylingNotes,
    // Only when pixels were actually read. The previous unconditional `0.95` let a classification
    // made purely from a filename claim 95% confidence, which the attribute badge then displayed.
    confidenceScore: metrics ? 0.95 : undefined,
    visualAttributes: [
      detectedCategory,
      detectedGarment,
      ...(resolvedColorName ? [resolvedColorName] : []),
      detectedFabric,
      detectedPattern,
    ],
  }
}
