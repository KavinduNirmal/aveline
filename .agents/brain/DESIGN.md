---
name: Aveline
colors:
  surface: '#fff8f7'
  surface-dim: '#e1d8d8'
  surface-bright: '#fff8f7'
  surface-container-lowest: '#ffffff'
  surface-container-low: '#fbf2f1'
  surface-container: '#f5eceb'
  surface-container-high: '#efe6e6'
  surface-container-highest: '#e9e0e0'
  on-surface: '#1e1b1b'
  on-surface-variant: '#534244'
  inverse-surface: '#342f2f'
  inverse-on-surface: '#f8efee'
  outline: '#867274'
  outline-variant: '#d9c1c3'
  surface-tint: '#954554'
  primary: '#5d1a29'
  on-primary: '#ffffff'
  primary-container: '#7a303f'
  on-primary-container: '#ff9cab'
  inverse-primary: '#ffb2bc'
  secondary: '#625d5d'
  on-secondary: '#ffffff'
  secondary-container: '#e6dedd'
  on-secondary-container: '#666161'
  tertiary: '#3b3030'
  on-tertiary: '#ffffff'
  tertiary-container: '#534646'
  on-tertiary-container: '#c6b4b4'
  error: '#ba1a1a'
  on-error: '#ffffff'
  error-container: '#ffdad6'
  on-error-container: '#93000a'
  primary-fixed: '#ffd9dd'
  primary-fixed-dim: '#ffb2bc'
  on-primary-fixed: '#3e0214'
  on-primary-fixed-variant: '#772e3d'
  secondary-fixed: '#e8e1e0'
  secondary-fixed-dim: '#ccc5c4'
  on-secondary-fixed: '#1e1b1b'
  on-secondary-fixed-variant: '#4a4645'
  tertiary-fixed: '#f1dede'
  tertiary-fixed-dim: '#d5c2c2'
  on-tertiary-fixed: '#231919'
  on-tertiary-fixed-variant: '#504444'
  background: '#fff8f7'
  on-background: '#1e1b1b'
  surface-variant: '#e9e0e0'
typography:
  display-lg:
    fontFamily: Playfair Display
    fontSize: 40px
    fontWeight: '500'
    lineHeight: '1.2'
    letterSpacing: -0.01em
  display-lg-mobile:
    fontFamily: Playfair Display
    fontSize: 32px
    fontWeight: '500'
    lineHeight: '1.2'
  headline-md:
    fontFamily: Playfair Display
    fontSize: 28px
    fontWeight: '500'
    lineHeight: '1.3'
  title-lg:
    fontFamily: Hanken Grotesk
    fontSize: 20px
    fontWeight: '600'
    lineHeight: '1.4'
  body-lg:
    fontFamily: Hanken Grotesk
    fontSize: 18px
    fontWeight: '400'
    lineHeight: '1.6'
  body-md:
    fontFamily: Hanken Grotesk
    fontSize: 16px
    fontWeight: '400'
    lineHeight: '1.6'
  label-md:
    fontFamily: Hanken Grotesk
    fontSize: 14px
    fontWeight: '500'
    lineHeight: '1.2'
    letterSpacing: 0.05em
  label-sm:
    fontFamily: Hanken Grotesk
    fontSize: 12px
    fontWeight: '600'
    lineHeight: '1.2'
rounded:
  sm: 0.25rem
  DEFAULT: 0.5rem
  md: 0.75rem
  lg: 1rem
  xl: 1.5rem
  full: 9999px
spacing:
  section-gap: 2rem
  card-padding: 1.5rem
  element-gap: 1rem
  container-margin: 1.25rem
  gutter: 1rem
---

## Brand & Style
The design system is rooted in the philosophy of "Quiet Luxury"—an aesthetic that prioritizes substance, craft, and understated elegance over loud branding. Designed for high-end boutique associates in Sri Lanka, the UI evokes the warmth of a sun-drenched atelier and the precision of a personalized concierge service.

The style is **Modern Minimalist with Tactile warmth**. It avoids the clinical coldness of traditional SaaS by using organic tones and generous whitespace. The interface feels "invisible," acting as a calm companion to the associate rather than a demanding tool. Every interaction should feel intentional, smooth, and high-touch.

## Colors
The palette is inspired by natural fibers—cashmere, linen, and silk—interspersed with deep architectural accents.

- **Primary (Maroon/Burgundy):** Used for key actions, active states, and primary navigation. It represents authority and the heritage of the boutique.
- **Surface & Backgrounds:** We use a "warm oatmeal" base instead of pure white to reduce eye strain and provide a premium, paper-like feel.
- **Agent States:** 
  - **Memory:** A muted magenta-rose, used for historical data and relationship tracking.
  - **Visual:** A soft gold/brass, used for aesthetic curation and product discovery.
  - **Commerce:** The primary maroon, representing the finality of a transaction.
- **Functional Neutrals:** Text is rendered in a soft charcoal rather than black to maintain the low-contrast, "quiet" luxury feel.

## Typography
The typography pairing balances classical elegance with modern legibility. 

**Playfair Display** is used for emotive moments: greetings, section headers, and key numbers. Its high contrast stems from traditional calligraphy, grounding the digital product in human craftsmanship.

**Hanken Grotesk** (a refined alternative to Inter) provides a clean, neutral counterpoint for functional UI elements, lists, and dense body text. It is highly legible on small screens while maintaining a contemporary, sharp edge.

**Hierarchy Rules:**
- Use `display-lg` for personalized greetings.
- Use `label-md` with uppercase styling for category headers and overlines to create a sense of organized luxury.
- Maintain generous line-height (1.6) for body copy to ensure a "breezy" reading experience.

## Layout & Spacing
The layout follows a **Fixed-Fluid Hybrid** model. While the content centers within a max-width container on desktop to preserve white space, the internal components utilize a fluid system.

- **Vertical Rhythm:** A strict 32px (`2rem`) gap exists between major sections (e.g., between "What's New" and "Today's Brief") to provide visual breathing room.
- **Card Internals:** All cards utilize 24px (`1.5rem`) padding. This "loose" padding communicates that the information is premium and not "crammed."
- **Margins:** Mobile margins are set to 20px to allow the background color to frame the content effectively.
- **Grid:** A 12-column grid is used for desktop, while a 2-column or single-stack grid is preferred for mobile to maintain the focus on one task at a time.

## Elevation & Depth
Depth in this design system is achieved through **Tonal Layering** and **Soft Ambient Shadows** rather than harsh borders.

1.  **Level 0 (Base):** The oatmeal background (`#F9F1F0`).
2.  **Level 1 (Cards):** Slightly lighter or subtly tinted surfaces. If a card needs to pop, use a very soft shadow: `0 4px 20px rgba(122, 48, 63, 0.06)`. Note the subtle maroon tint in the shadow to keep it warm.
3.  **Level 2 (Interactive):** Elements like "Clocked In" use a solid fill to denote the highest state of importance.
4.  **Glassmorphism:** Use sparingly for floating action bars or navigation overlays with a 12px blur and 80% opacity of the background color to maintain the sense of place.

## Shapes
The shape language is "Softly Geometric." We avoid sharp corners to keep the UI approachable and human.

- **Standard Radius:** 16px (`rounded-lg`) is the default for all cards and primary containers.
- **Small Elements:** Buttons and input fields use 12px or 8px depending on size.
- **Icon Enclosures:** Icons are often housed in soft-circle or highly rounded squircle containers to mimic the look of physical jewelry or fabric buttons.

## Components
- **Buttons:**
    - **Primary:** Solid Burgundy (`#7A303F`) with white text. High-contrast, no border.
    - **Secondary:** Transparent with a thin (1px) Burgundy border or a tonal fill (`#E8D5D5`).
- **Cards:** Use the 16px corner radius. Group related items (like "Today's Brief") into stacked cards with 8px gaps between them to show they are part of a single collection.
- **Input Fields:** Soft beige backgrounds with a subtle bottom border that transforms into a full border on focus.
- **Chips/Badges:** Use the Agent State colors (Gold, Magenta, Maroon) with 10% opacity fills and 100% opacity text for a sophisticated, low-contrast look.
- **Navigation:** A persistent bottom bar on mobile using high-quality line icons with the primary maroon color for the active state.
- **In-App Notifications:** Subtle banners that slide in from the top, using the maroon fill with white text to ensure immediate visibility without feeling "alarming."
