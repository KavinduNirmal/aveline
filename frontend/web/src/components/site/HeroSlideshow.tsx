import { useState, useEffect } from 'react'
import { motion, AnimatePresence } from 'motion/react'
import { Blossom } from '@/components/auth/Blossom'
import { cn } from '@/lib/utils'

interface Slide {
  id: number
  imageUrl: string
  title: string
  boutique: string
  location: string
  floatingPill: {
    agent: 'ava' | 'elle' | 'lina'
    text: string
    subtext: string
    color: string
    badgeBg: string
    textColor: string
  }
}

const SLIDES: Slide[] = [
  {
    id: 1,
    imageUrl: 'https://images.pexels.com/photos/1884581/pexels-photo-1884581.jpeg?auto=compress&cs=tinysrgb&w=1400',
    title: 'The Silk & Linen Atelier',
    boutique: 'Maison Galle Fort',
    location: 'Lighthouse Street, Galle',
    floatingPill: {
      agent: 'ava',
      text: 'Ava recalled preference',
      subtext: 'Prefers Ceylon mulberry silks & jewel tones',
      color: 'border-rose-200 bg-white/95 text-rose-950',
      badgeBg: 'bg-memory text-white',
      textColor: 'text-memory',
    },
  },
  {
    id: 2,
    imageUrl: 'https://images.pexels.com/photos/974911/pexels-photo-974911.jpeg?auto=compress&cs=tinysrgb&w=1400',
    title: 'Couture Fitting Studio',
    boutique: 'Cinnamon Row Tailors',
    location: 'Colombo 07',
    floatingPill: {
      agent: 'elle',
      text: 'Elle matched lookbook',
      subtext: '3 cocktail gowns curated for wedding guest',
      color: 'border-amber-200 bg-white/95 text-amber-950',
      badgeBg: 'bg-visual text-white',
      textColor: 'text-visual',
    },
  },
  {
    id: 3,
    imageUrl: 'https://images.pexels.com/photos/3755706/pexels-photo-3755706.jpeg?auto=compress&cs=tinysrgb&w=1400',
    title: 'Artisanal Ready-To-Wear',
    boutique: 'Studio Nine Colombo',
    location: 'Horton Place, Colombo',
    floatingPill: {
      agent: 'lina',
      text: 'Lina validated margin',
      subtext: 'LKR 45,000 order prepared with 24h hold',
      color: 'border-primary/30 bg-white/95 text-neutral-900',
      badgeBg: 'bg-commerce text-white',
      textColor: 'text-commerce',
    },
  },
]

export function HeroSlideshow({ className }: { className?: string }) {
  const [currentIndex, setCurrentIndex] = useState(0)

  useEffect(() => {
    const timer = setInterval(() => {
      setCurrentIndex((prev) => (prev + 1) % SLIDES.length)
    }, 5500)
    return () => clearInterval(timer)
  }, [])

  const current = SLIDES[currentIndex]

  return (
    <div
      className={cn(
        'relative h-[440px] w-full select-none overflow-hidden rounded-2xl border-2 border-dashed border-neutral-200/90 bg-neutral-900 shadow-[0_30px_70px_-30px_rgba(30,10,15,0.35)] sm:h-[500px] lg:h-[540px]',
        className
      )}
    >
      {/* Slideshow Images */}
      <AnimatePresence mode="wait">
        <motion.div
          key={current.id}
          initial={{ opacity: 0, scale: 1.04 }}
          animate={{ opacity: 1, scale: 1 }}
          exit={{ opacity: 0, scale: 0.98 }}
          transition={{ duration: 1.1, ease: [0.22, 1, 0.36, 1] }}
          className="absolute inset-0"
        >
          <img
            src={current.imageUrl}
            alt={current.title}
            className="h-full w-full object-cover object-center"
          />
          {/* Rich luxury darkening and gradient overlays */}
          <div className="absolute inset-0 bg-gradient-to-t from-neutral-950/90 via-neutral-900/35 to-neutral-900/25" />
          <div className="absolute inset-0 bg-primary/10 mix-blend-multiply" />
        </motion.div>
      </AnimatePresence>

      {/* Floating Aveline Specialist Glass Pill with Colored Blossom */}
      <div className="absolute left-5 top-5 z-20 max-w-[280px] sm:left-8 sm:top-8 sm:max-w-[340px]">
        <AnimatePresence mode="wait">
          <motion.div
            key={current.id}
            initial={{ opacity: 0, y: -12, scale: 0.95 }}
            animate={{ opacity: 1, y: 0, scale: 1 }}
            exit={{ opacity: 0, y: 12, scale: 0.95 }}
            transition={{ duration: 0.45 }}
            className={cn(
              'flex items-center gap-3 rounded-2xl border p-3 shadow-xl backdrop-blur-md',
              current.floatingPill.color
            )}
          >
            <div className={cn('flex size-9 shrink-0 items-center justify-center rounded-xl shadow-xs', current.floatingPill.badgeBg)}>
              <Blossom className="size-4.5 text-white" />
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex items-center gap-1.5">
                <span className={cn('text-xs sm:text-sm font-semibold leading-tight', current.floatingPill.textColor)}>
                  {current.floatingPill.text}
                </span>
                <Blossom animateCounter counterDuration={14} className="size-3 text-primary" />
              </div>
              <p className="truncate text-[11px] sm:text-xs text-neutral-600">
                {current.floatingPill.subtext}
              </p>
            </div>
          </motion.div>
        </AnimatePresence>
      </div>

      {/* Bottom Atelier Caption & Slide Navigation Indicators */}
      <div className="absolute bottom-6 left-6 right-6 z-20 flex items-end justify-between gap-4 sm:bottom-8 sm:left-8 sm:right-8">
        <div className="text-white">
          <p className="text-[11px] sm:text-xs font-semibold uppercase tracking-widest text-rose-200/90">
            {current.boutique}
          </p>
          <h4 className="font-serif text-xl font-medium leading-snug text-white sm:text-2xl lg:text-3xl">
            {current.title}
          </h4>
          <p className="text-xs sm:text-sm text-neutral-300">
            {current.location}
          </p>
        </div>

        {/* Indicator dots */}
        <div className="flex items-center gap-2 pb-1">
          {SLIDES.map((slide, i) => (
            <button
              key={slide.id}
              type="button"
              onClick={() => setCurrentIndex(i)}
              aria-label={`Go to slide ${i + 1}`}
              className={cn(
                'h-2.5 rounded-full transition-all duration-300',
                currentIndex === i
                  ? 'w-8 bg-white shadow-sm'
                  : 'w-2.5 bg-white/40 hover:bg-white/70'
              )}
            />
          ))}
        </div>
      </div>
    </div>
  )
}
