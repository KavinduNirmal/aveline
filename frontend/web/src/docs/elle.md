# Elle — Visual Sourcing & Curation

**Elle** is Aveline's Visual Intelligence agent. Elle bridges the gap between aesthetic inspiration and inventory availability, empowering stylists to compose striking editorial lookbooks in real time.

---

## Visual Intelligence Features

- **Multi-Modal Garment Understanding:** Deconstructs textures, collars, cuts, color warmth, and occasion suitability from garment snapshots.
- **Instant Lookbook Composition:** Creates harmonized 3-to-5 piece ensembles respecting client measurements and designer pairings.
- **Visual Similarity Search:** When an item is sold out or unavailable in a desired size, Elle finds the closest aesthetic alternatives across current stock.
- **Moodboard Ingestion:** Upload an image from Instagram, Pinterest, or a runway show to find matching items in your boutique catalog.

---

## Lookbook Generation Workflow

Stylists can request look compositions through natural conversation or image prompts:

```typescript
// Stylist prompt to Elle

const lookbook = await elle.composeLook({
  clientId: "cli_8921dfa9",
  occasion: "Evening Gallery Opening",
  palette: ["Wine Rose", "Champagne Gold", "Charcoal"],
  mustIncludeSku: "DRS-901-SLK",
});
```

---

## Supported Imagery & Formats

- High-resolution smartphone snapshots (JPEG, PNG, HEIC)
- Digital sketches and fabric swatch captures
- Official manufacturer lookbook assets and studio photography
