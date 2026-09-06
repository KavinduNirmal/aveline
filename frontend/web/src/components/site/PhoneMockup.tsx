import { useState, useEffect, useRef } from 'react'
import { motion, AnimatePresence } from 'motion/react'
import {
  Wifi,
  Battery,
  Sparkles,
  ChevronLeft,
  ChevronRight,
  CheckCircle2,
  XCircle,
  MessageCircle,
  ShieldCheck,
  PackageCheck,
  Mouse,
} from 'lucide-react'
import { Blossom } from '@/components/auth/Blossom'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

export function PhoneMockup() {
  const containerRef = useRef<HTMLDivElement>(null)
  const [activeStep, setActiveStep] = useState(0)
  const [orderApproved, setOrderApproved] = useState(false)

  // Touch gesture tracking for mobile swipe
  const touchStartY = useRef<number | null>(null)
  const touchStartX = useRef<number | null>(null)

  // Intercept wheel events directly on the phone container with non-passive listener
  // This prevents the page from scrolling while user is interacting with the slideshow
  useEffect(() => {
    const el = containerRef.current
    if (!el) return

    let lastAdvanceTime = 0
    let accumulatedDelta = 0
    const COOLDOWN_MS = 380
    const DELTA_THRESHOLD = 30

    const handleWheel = (e: WheelEvent) => {
      const isDown = e.deltaY > 0
      const isUp = e.deltaY < 0

      // If at boundary:
      // At step 0 and scrolling up -> allow native page scroll up
      if (activeStep === 0 && isUp) return

      // At step 5 and scrolling down -> allow native page scroll down
      if (activeStep === 5 && isDown) return

      // Within slides: prevent the webpage from scrolling away!
      e.preventDefault()

      // Reset accumulator if direction flipped
      if ((isDown && accumulatedDelta < 0) || (isUp && accumulatedDelta > 0)) {
        accumulatedDelta = 0
      }

      accumulatedDelta += e.deltaY
      const now = Date.now()

      if (now - lastAdvanceTime > COOLDOWN_MS) {
        if (isDown && accumulatedDelta >= DELTA_THRESHOLD && activeStep < 5) {
          setActiveStep((prev) => Math.min(5, prev + 1))
          lastAdvanceTime = now
          accumulatedDelta = 0
        } else if (isUp && accumulatedDelta <= -DELTA_THRESHOLD && activeStep > 0) {
          setActiveStep((prev) => Math.max(0, prev - 1))
          lastAdvanceTime = now
          accumulatedDelta = 0
        }
      }
    }

    el.addEventListener('wheel', handleWheel, { passive: false })
    return () => {
      el.removeEventListener('wheel', handleWheel)
    }
  }, [activeStep])

  const stepsMeta = [
    { title: 'Aveline Startup', label: '1. Aveline Startup' },
    { title: 'New Arrival & WhatsApp', label: '2. Product & Inquiry' },
    { title: 'Ava — Memory', label: '3. Client Context' },
    { title: 'Elle — Sourcing', label: '4. Visual Analysis' },
    { title: 'Lina — Margin Check', label: '5. Supplier Order' },
    { title: 'Final Client Conversation', label: '6. Deal Closed' },
  ]

  const handleStepClick = (index: number) => {
    setActiveStep(index)
  }

  const handlePrev = () => {
    setActiveStep((prev) => (prev > 0 ? prev - 1 : 5))
  }

  const handleNext = () => {
    setActiveStep((prev) => (prev < 5 ? prev + 1 : 0))
  }

  const handleTouchStart = (e: React.TouchEvent) => {
    touchStartY.current = e.touches[0].clientY
    touchStartX.current = e.touches[0].clientX
  }

  const handleTouchEnd = (e: React.TouchEvent) => {
    if (touchStartY.current === null || touchStartX.current === null) return
    const diffY = touchStartY.current - e.changedTouches[0].clientY
    const diffX = touchStartX.current - e.changedTouches[0].clientX

    if (Math.abs(diffY) > 35 || Math.abs(diffX) > 35) {
      if (diffY > 35 || diffX > 35) {
        // Swiped up or left -> Next slide
        if (activeStep < 5) {
          setActiveStep((prev) => Math.min(5, prev + 1))
        }
      } else if (diffY < -35 || diffX < -35) {
        // Swiped down or right -> Prev slide
        if (activeStep > 0) {
          setActiveStep((prev) => Math.max(0, prev - 1))
        }
      }
    }
    touchStartY.current = null
    touchStartX.current = null
  }

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'ArrowDown' || e.key === 'ArrowRight') {
      if (activeStep < 5) {
        e.preventDefault()
        setActiveStep((prev) => Math.min(5, prev + 1))
      }
    } else if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') {
      if (activeStep > 0) {
        e.preventDefault()
        setActiveStep((prev) => Math.max(0, prev - 1))
      }
    }
  }

  return (
    <div
      ref={containerRef}
      tabIndex={0}
      role="region"
      aria-label="Aveline Interactive Product Walkthrough"
      onKeyDown={handleKeyDown}
      onTouchStart={handleTouchStart}
      onTouchEnd={handleTouchEnd}
      className="relative mx-auto w-full max-w-[325px] select-none focus:outline-hidden"
    >
      {/* Ambient background bloom matching brand tones */}
      <div
        className="pointer-events-none absolute -inset-8 -z-10 rounded-[50px] opacity-70 blur-3xl transition-colors duration-1000"
        style={{
          background:
            activeStep === 0
              ? 'radial-gradient(circle, rgba(176,86,107,0.35) 0%, rgba(201,151,43,0.2) 50%, transparent 70%)'
              : activeStep === 2
                ? 'radial-gradient(circle, rgba(176,86,107,0.4) 0%, rgba(255,224,229,0.2) 60%, transparent 70%)'
                : activeStep === 3
                  ? 'radial-gradient(circle, rgba(201,151,43,0.35) 0%, rgba(253,240,216,0.2) 60%, transparent 70%)'
                  : 'radial-gradient(circle, rgba(139,46,66,0.35) 0%, rgba(239,122,104,0.2) 60%, transparent 70%)',
        }}
      />

      {/* Floating step control pill above phone */}
      <div className="mb-4 flex items-center justify-between gap-2 rounded-2xl border border-dashed border-border/80 bg-card/80 px-3.5 py-2 text-xs shadow-xs backdrop-blur-md">
        <div className="flex items-center gap-2">
          <span className="flex size-2 rounded-full bg-commerce animate-pulse" />
          <span className="font-semibold text-foreground">
            {stepsMeta[activeStep].label}
          </span>
        </div>
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            onClick={handlePrev}
            aria-label="Previous step"
            className="flex size-6 items-center justify-center rounded-lg border border-border text-muted-foreground hover:bg-muted hover:text-foreground transition-colors cursor-pointer"
          >
            <ChevronLeft className="size-3.5" />
          </button>
          <span className="font-mono text-[11px] text-muted-foreground px-1">
            {activeStep + 1}/6
          </span>
          <button
            type="button"
            onClick={handleNext}
            aria-label="Next step"
            className="flex size-6 items-center justify-center rounded-lg border border-border text-muted-foreground hover:bg-muted hover:text-foreground transition-colors cursor-pointer"
          >
            <ChevronRight className="size-3.5" />
          </button>
        </div>
      </div>

      {/* Helper scroll hint badge */}
      <div className="mb-2.5 flex items-center justify-center gap-1.5 text-[11px] font-medium text-muted-foreground/80">
        <Mouse className="size-3 text-commerce animate-bounce" />
        <span>Scroll over phone to advance slides</span>
        <span className="text-neutral-400/60">•</span>
        <span className="text-neutral-400 text-[10px]">or swipe</span>
      </div>

      {/* Physical iPhone 16 Pro Style Hardware Frame - Slim micro-bezel */}
      <div className="relative rounded-[44px] border-[3px] border-[#362e31] bg-[#1e1719] p-1.5 shadow-[0_25px_60px_-12px_rgba(20,10,15,0.22)] ring-1 ring-black/20">
        {/* Screen Bezel & Dynamic Island */}
        <div className="relative h-[680px] w-full overflow-hidden rounded-[38px] bg-[#faf6f5] text-neutral-900 flex flex-col justify-between border border-neutral-200/60 shadow-inner">
          {/* Status Bar */}
          <div className="relative z-30 flex items-center justify-between px-6 pt-3 text-[12px] font-medium text-neutral-800">
            <span>9:41</span>
            {/* Dynamic Island */}
            <div className="absolute left-1/2 top-2.5 h-6 w-24 -translate-x-1/2 rounded-full bg-black flex items-center justify-between px-2.5 shadow-xs">
              <span className="size-2 rounded-full bg-commerce/80 animate-pulse" />
              <div className="size-2.5 rounded-full bg-[#1e1b1b] border border-white/20" />
            </div>
            <div className="flex items-center gap-1.5 text-neutral-700">
              <Wifi className="size-3.5" />
              <Battery className="size-4" />
            </div>
          </div>

          {/* Screen Content Slides */}
          <div className="relative flex-1 overflow-y-auto px-4 pt-1 pb-3 select-none flex flex-col justify-start">
            <AnimatePresence mode="wait">
              {/* =========================================================================
                  SLIDE 0: Aveline 2.0 Aura Startup Screen (Reference Image 1)
                  ========================================================================= */}
              {activeStep === 0 && (
                <motion.div
                  key="slide-0"
                  initial={{ opacity: 0, scale: 0.96 }}
                  animate={{ opacity: 1, scale: 1 }}
                  exit={{ opacity: 0, scale: 0.96 }}
                  transition={{ duration: 0.4 }}
                  className="flex-1 flex flex-col justify-between items-center text-center pt-2 pb-2 min-h-[575px] w-full"
                >
                  {/* Top Section: Header & Greeting */}
                  <div className="flex flex-col items-center">
                    <Badge variant="outline" className="mb-2.5 text-[10px] uppercase tracking-widest text-rose-900 border-rose-200 bg-rose-50/90 shadow-2xs">
                      Aveline 1.0 · Live
                    </Badge>
                    
                    <h3 className="font-serif text-2xl font-medium tracking-tight text-neutral-900 leading-tight">
                      Good morning, Shanali
                    </h3>
                    <p className="mt-1 text-xs text-neutral-500">
                      Your luxury atelier concierge
                    </p>
                  </div>

                  {/* Middle Section: Pulsating Diffuse Gradient Glow + Rotating & Pulsing Blossom */}
                  <div className="relative my-auto py-2 flex flex-col items-center justify-center w-full">
                    <div className="relative flex items-center justify-center size-52">
                      {/* Diffuse pulsating gradient glow (no hard border/circle) */}
                      <motion.div
                        animate={{
                          scale: [1, 1.25, 1],
                          opacity: [0.65, 0.95, 0.65],
                        }}
                        transition={{
                          duration: 4,
                          repeat: Infinity,
                          ease: 'easeInOut',
                        }}
                        className="absolute size-48 rounded-full bg-[radial-gradient(circle,rgba(244,188,198,0.7)_0%,rgba(247,219,161,0.5)_40%,rgba(235,178,191,0.35)_65%,transparent_75%)] blur-2xl pointer-events-none"
                      />
                      <motion.div
                        animate={{
                          scale: [1.15, 0.95, 1.15],
                          opacity: [0.45, 0.75, 0.45],
                        }}
                        transition={{
                          duration: 4.8,
                          repeat: Infinity,
                          ease: 'easeInOut',
                        }}
                        className="absolute size-40 rounded-full bg-[radial-gradient(circle,rgba(247,219,161,0.6)_0%,rgba(244,188,198,0.5)_50%,transparent_75%)] blur-xl pointer-events-none"
                      />

                      {/* Rotating while pulsing Blossom without counter-rotating petals */}
                      <motion.div
                        animate={{
                          rotate: 360,
                          scale: [0.92, 1.08, 0.92],
                        }}
                        transition={{
                          rotate: { duration: 20, repeat: Infinity, ease: 'linear' },
                          scale: { duration: 3.2, repeat: Infinity, ease: 'easeInOut' },
                        }}
                        className="relative z-10 flex items-center justify-center"
                      >
                        <Blossom
                          animateCounter={false}
                          className="size-20 text-primary drop-shadow-[0_4px_24px_rgba(139,46,66,0.3)]"
                        />
                      </motion.div>
                    </div>

                    <p className="mt-4 max-w-[260px] text-xs leading-relaxed text-neutral-600 font-normal">
                      All three subagents are online. Ava, Elle and Lina are synchronized for today’s clienteling.
                    </p>

                    {/* Subagent Status Badges */}
                    <div className="mt-4 flex items-center justify-center gap-1.5 w-full">
                      <span className="flex items-center gap-1 rounded-full bg-rose-50 px-2.5 py-1 text-[10px] font-medium text-rose-800 border border-rose-200/80 shadow-2xs">
                        <span className="size-1.5 rounded-full bg-memory" /> Ava
                      </span>
                      <span className="flex items-center gap-1 rounded-full bg-amber-50 px-2.5 py-1 text-[10px] font-medium text-amber-800 border border-amber-200/80 shadow-2xs">
                        <span className="size-1.5 rounded-full bg-visual" /> Elle
                      </span>
                      <span className="flex items-center gap-1 rounded-full bg-rose-100/70 px-2.5 py-1 text-[10px] font-medium text-rose-900 border border-rose-200 shadow-2xs">
                        <span className="size-1.5 rounded-full bg-commerce" /> Lina
                      </span>
                    </div>
                  </div>

                  {/* Bottom Action (Sticks to Bottom) */}
                  <div className="w-full pt-2">
                    <Button
                      size="sm"
                      onClick={handleNext}
                      className="w-full max-w-[260px] mx-auto bg-primary hover:bg-primary/90 text-white shadow-md cursor-pointer flex items-center justify-center gap-2 transition-transform hover:scale-[1.02]"
                    >
                      <Sparkles className="size-3.5" />
                      <span>Enter Atelier Feed</span>
                    </Button>
                  </div>
                </motion.div>
              )}

              {/* =========================================================================
                  SLIDE 1: Product Match Alert + WhatsApp Embedded Message
                  ========================================================================= */}
              {activeStep === 1 && (
                <motion.div
                  key="slide-1"
                  initial={{ opacity: 0, y: 15 }}
                  animate={{ opacity: 1, y: 0 }}
                  exit={{ opacity: 0, y: -15 }}
                  transition={{ duration: 0.35 }}
                  className="flex flex-col gap-3 py-2"
                >
                  <div className="flex items-center justify-between text-[11px] text-neutral-500">
                    <span className="font-semibold uppercase tracking-wider text-rose-900">
                      Incoming Signals
                    </span>
                    <span>Just now</span>
                  </div>

                  {/* Message 1: Aveline Inventory Alert */}
                  <div className="rounded-2xl border border-rose-100/80 bg-white p-3.5 shadow-sm">
                    <div className="flex items-center gap-2 mb-2">
                      <span className="flex size-5 items-center justify-center rounded-full bg-commerce text-white shadow-2xs">
                        <Blossom className="size-3" />
                      </span>
                      <span className="text-xs font-semibold text-rose-950">Aveline System</span>
                      <span className="ml-auto text-[10px] text-neutral-400">10:12 AM</span>
                    </div>

                    <div className="flex gap-3 items-center">
                      <img
                        src="https://images.pexels.com/photos/291759/pexels-photo-291759.jpeg?auto=compress&cs=tinysrgb&w=200"
                        alt="Peony Drape Gown"
                        className="size-14 shrink-0 rounded-xl object-cover border border-neutral-200/80 shadow-2xs"
                      />
                      <div className="text-[11px] leading-snug text-neutral-700">
                        The new product <span className="font-semibold text-neutral-900">#AVL-902 (Peach Drape Raw-Silk Gown)</span> matches 3 customers according to Ava. Should I draft a message to contact them?
                      </div>
                    </div>

                    <div className="mt-3 flex items-center gap-1.5">
                      <button
                        type="button"
                        onClick={handleNext}
                        className="flex-1 rounded-lg bg-commerce px-2.5 py-1.5 text-center text-[10px] font-semibold text-white hover:brightness-105 transition-colors shadow-2xs cursor-pointer"
                      >
                        Yes, Draft
                      </button>
                      <button
                        type="button"
                        className="flex-1 rounded-lg border border-neutral-200 bg-neutral-50 px-2.5 py-1.5 text-center text-[10px] font-medium text-neutral-700 hover:bg-neutral-100 transition-colors cursor-pointer"
                      >
                        View 3
                      </button>
                      <button
                        type="button"
                        className="rounded-lg border border-neutral-200 bg-neutral-50 px-2 py-1.5 text-center text-[10px] text-neutral-500 hover:bg-neutral-100 transition-colors cursor-pointer"
                      >
                        Later
                      </button>
                    </div>
                  </div>

                  {/* Message 2: Embedded WhatsApp Message */}
                  <div className="rounded-2xl border border-emerald-200/80 bg-[#f4fbf7] p-3.5 shadow-sm">
                    <div className="flex items-center justify-between mb-2">
                      <div className="flex items-center gap-1.5">
                        <span className="flex size-5 items-center justify-center rounded-full bg-emerald-600 text-white shadow-2xs">
                          <MessageCircle className="size-3" />
                        </span>
                        <span className="text-xs font-semibold text-emerald-950">WhatsApp Inquiry</span>
                      </div>
                      <Badge variant="outline" className="text-[9px] border-emerald-300 text-emerald-800 bg-emerald-100/70">
                        VIP Patron
                      </Badge>
                    </div>

                    <div className="flex items-center gap-2 mb-2.5">
                      <img
                        src="https://images.pexels.com/photos/1239291/pexels-photo-1239291.jpeg?auto=compress&cs=tinysrgb&w=150"
                        alt="Sarah Alwis"
                        className="size-7 rounded-full object-cover border border-emerald-300/80"
                      />
                      <div>
                        <p className="text-xs font-semibold text-neutral-900">Sarah Alwis</p>
                        <p className="text-[10px] text-neutral-500">+94 77 892 1440 · Colombo 07</p>
                      </div>
                    </div>

                    <div className="rounded-xl bg-white border border-emerald-100 p-2.5 text-[11px] leading-relaxed text-neutral-800 shadow-2xs">
                      Hi Shanali! It’s Sarah. Do you have anything special for an outdoor sunset ceremony? Here’s my mood board photo 📸
                    </div>

                    <div className="mt-2 flex items-center justify-between text-[10px] text-emerald-700">
                      <span>Attached: moodboard_galle.jpg</span>
                      <button
                        type="button"
                        onClick={handleNext}
                        className="underline hover:text-emerald-900 font-medium flex items-center gap-1 cursor-pointer"
                      >
                        Consult Ava <ChevronRight className="size-3" />
                      </button>
                    </div>
                  </div>
                </motion.div>
              )}

              {/* =========================================================================
                  SLIDE 2: Ava Replies to WhatsApp Embed with Memory Context
                  ========================================================================= */}
              {activeStep === 2 && (
                <motion.div
                  key="slide-2"
                  initial={{ opacity: 0, x: 20 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -20 }}
                  transition={{ duration: 0.35 }}
                  className="flex flex-col gap-3 py-2"
                >
                  <div className="flex items-center gap-2">
                    <span className="flex size-7 items-center justify-center rounded-full bg-memory text-white shadow-2xs">
                      <Blossom className="size-4" />
                    </span>
                    <div>
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs font-semibold text-neutral-900">Ava (Memory Agent)</span>
                        <span className="size-1.5 rounded-full bg-memory animate-pulse" />
                      </div>
                      <span className="text-[10px] text-neutral-500">Replying to WhatsApp inquiry</span>
                    </div>
                  </div>

                  {/* Ava's Memory Message Bubble */}
                  <div className="rounded-2xl border border-rose-200 bg-[#fff2f4] p-4 text-[12px] leading-relaxed text-neutral-800 shadow-sm">
                    <p>
                      Sarah is contacting us after <span className="font-semibold text-rose-900">three months</span>! According to her profile and context, it’s about her sister’s wedding, which is in <span className="font-semibold text-rose-900">3 weeks</span>.
                    </p>
                    <p className="mt-2.5">
                      Let me ask Elle to bring up the collage she designed for her.
                    </p>
                  </div>

                  {/* Ava's Recalled Profile Graph */}
                  <div className="rounded-2xl border border-neutral-200/80 bg-white p-3 text-xs shadow-sm">
                    <div className="flex items-center justify-between mb-2 text-[10px] font-semibold uppercase tracking-wider text-rose-900">
                      <span>Ava's Memory Graph</span>
                      <ShieldCheck className="size-3 text-rose-600" />
                    </div>
                    <div className="space-y-1.5 text-[11px] text-neutral-700">
                      <div className="flex justify-between border-b border-neutral-100 pb-1">
                        <span className="text-neutral-500">Client:</span>
                        <span className="font-medium text-neutral-900">Sarah Alwis (VIP Patron)</span>
                      </div>
                      <div className="flex justify-between border-b border-neutral-100 pb-1">
                        <span className="text-neutral-500">Event Date:</span>
                        <span className="text-rose-900 font-medium">Saturday in Galle (3 wks)</span>
                      </div>
                      <div className="flex justify-between">
                        <span className="text-neutral-500">Past Favorites:</span>
                        <span className="text-neutral-800">Peach & Raw-Silks, UK 10</span>
                      </div>
                    </div>
                  </div>

                  <Button
                    size="sm"
                    onClick={handleNext}
                    className="mt-1 w-full bg-[#b0566b] text-white hover:bg-[#b0566b]/90 text-xs shadow-xs cursor-pointer"
                  >
                    <span>Summon Elle’s Curation</span>
                    <ChevronRight className="size-3.5" />
                  </Button>
                </motion.div>
              )}

              {/* =========================================================================
                  SLIDE 3: Elle Responds to Ava (Visual Analysis & Supplier Request)
                  ========================================================================= */}
              {activeStep === 3 && (
                <motion.div
                  key="slide-3"
                  initial={{ opacity: 0, x: 20 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -20 }}
                  transition={{ duration: 0.35 }}
                  className="flex flex-col gap-3 py-2"
                >
                  <div className="flex items-center gap-2">
                    <span className="flex size-7 items-center justify-center rounded-full bg-visual text-white shadow-2xs">
                      <Blossom className="size-4" />
                    </span>
                    <div>
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs font-semibold text-neutral-900">Elle (Visual Agent)</span>
                        <span className="size-1.5 rounded-full bg-visual animate-pulse" />
                      </div>
                      <span className="text-[10px] text-neutral-500">Replying to Ava</span>
                    </div>
                  </div>

                  {/* Elle's Message Bubble */}
                  <div className="rounded-2xl border border-amber-200 bg-[#fffaf0] p-4 text-[12px] leading-relaxed text-neutral-800 shadow-sm">
                    <p>
                      While I have created a collage, it seems her preferences this time around don’t fit my earlier fit.
                    </p>
                    <p className="mt-2">
                      And the reference image she attached doesn’t resemble any product we currently have in the store. <span className="font-semibold text-amber-900">I’ll ask Lina to find something similar from our suppliers.</span>
                    </p>
                  </div>

                  {/* Visual Comparison Grid */}
                  <div className="rounded-2xl border border-neutral-200/80 bg-white p-3 text-xs shadow-sm">
                    <div className="flex items-center justify-between mb-2 text-[10px] font-semibold uppercase tracking-wider text-amber-900">
                      <span>Visual Discrepancy Found</span>
                      <span className="text-neutral-500 font-normal">Stock vs Reference</span>
                    </div>
                    <div className="grid grid-cols-2 gap-2">
                      <div className="rounded-xl bg-rose-50/60 p-2 border border-rose-100">
                        <span className="text-[9px] text-neutral-500 block mb-1">Store Inventory</span>
                        <img
                          src="https://images.pexels.com/photos/1755428/pexels-photo-1755428.jpeg?auto=compress&cs=tinysrgb&w=200"
                          alt="Store stock"
                          className="h-16 w-full rounded-lg object-cover"
                        />
                        <span className="mt-1 text-[10px] text-rose-800 block font-medium">No Direct Match</span>
                      </div>
                      <div className="rounded-xl bg-amber-50/60 p-2 border border-amber-100">
                        <span className="text-[9px] text-neutral-500 block mb-1">Sarah’s Moodboard</span>
                        <img
                          src="https://images.pexels.com/photos/985635/pexels-photo-985635.jpeg?auto=compress&cs=tinysrgb&w=200"
                          alt="Sarah's moodboard"
                          className="h-16 w-full rounded-lg object-cover"
                        />
                        <span className="mt-1 text-[10px] text-amber-900 block font-medium">Bespoke Peach Drape</span>
                      </div>
                    </div>
                  </div>

                  <Button
                    size="sm"
                    onClick={handleNext}
                    className="mt-1 w-full bg-[#c9972b] text-white hover:bg-[#c9972b]/90 text-xs shadow-xs cursor-pointer"
                  >
                    <span>Request Lina’s Supplier Check</span>
                    <ChevronRight className="size-3.5" />
                  </Button>
                </motion.div>
              )}

              {/* =========================================================================
                  SLIDE 4: Lina Responds to Elle (Suppliers, Margin & Order Approval)
                  ========================================================================= */}
              {activeStep === 4 && (
                <motion.div
                  key="slide-4"
                  initial={{ opacity: 0, x: 20 }}
                  animate={{ opacity: 1, x: 0 }}
                  exit={{ opacity: 0, x: -20 }}
                  transition={{ duration: 0.35 }}
                  className="flex flex-col gap-3 py-2"
                >
                  <div className="flex items-center gap-2">
                    <span className="flex size-7 items-center justify-center rounded-full bg-commerce text-white shadow-2xs">
                      <Blossom className="size-4" />
                    </span>
                    <div>
                      <div className="flex items-center gap-1.5">
                        <span className="text-xs font-semibold text-neutral-900">Lina (Commerce Agent)</span>
                        <span className="size-1.5 rounded-full bg-commerce animate-pulse" />
                      </div>
                      <span className="text-[10px] text-neutral-500">Replying to Elle</span>
                    </div>
                  </div>

                  {/* Lina's Message Bubble */}
                  <div className="rounded-2xl border border-rose-200 bg-[#fff2f4] p-4 text-[12px] leading-relaxed text-neutral-800 shadow-sm">
                    <p>
                      I have found <span className="font-semibold text-neutral-900">3 suppliers</span> with matching dresses, with a <span className="font-semibold text-emerald-700">40% profit margin</span>.
                    </p>
                    <p className="mt-2">
                      I suggest ordering from <span className="font-semibold text-rose-900">XYZ Atelier Studio</span> (Cost: LKR 24,000 · Retail: LKR 42,000). Should I create an order?
                    </p>
                  </div>

                  {/* Supplier Card */}
                  <div className="rounded-2xl border border-neutral-200/80 bg-white p-3 text-xs shadow-sm">
                    <div className="flex items-center justify-between text-[10px] uppercase tracking-wider text-rose-900 font-semibold mb-2">
                      <span>Order Proposal #PO-881</span>
                      <span className="text-emerald-700 font-bold">+40% Margin</span>
                    </div>
                    <div className="flex items-center justify-between text-[11px] text-neutral-700 border-b border-neutral-100 pb-2 mb-2">
                      <span>Supplier: XYZ Atelier</span>
                      <span className="text-neutral-900 font-medium">LKR 24,000 net</span>
                    </div>
                    <div className="flex items-center justify-between text-[11px] text-neutral-700">
                      <span>Retail Price for Sarah:</span>
                      <span className="font-bold text-rose-900">LKR 42,000</span>
                    </div>
                  </div>

                  {/* Interactive Approve / Reject buttons */}
                  {orderApproved ? (
                    <div className="flex items-center justify-center gap-2 rounded-xl border border-emerald-300 bg-emerald-50 py-2.5 text-xs font-semibold text-emerald-800 shadow-xs">
                      <CheckCircle2 className="size-4 text-emerald-600" />
                      <span>Order Approved & Drafted</span>
                    </div>
                  ) : (
                    <div className="flex items-center gap-2 pt-1">
                      <Button
                        size="sm"
                        onClick={() => {
                          setOrderApproved(true)
                          setTimeout(() => handleNext(), 800)
                        }}
                        className="flex-1 bg-emerald-700 text-white hover:bg-emerald-800 text-xs font-semibold shadow-xs cursor-pointer"
                      >
                        <CheckCircle2 className="size-3.5 mr-1" />
                        <span>Approve Order</span>
                      </Button>
                      <Button
                        size="sm"
                        variant="outline"
                        onClick={handleNext}
                        className="border-neutral-200 bg-white text-neutral-700 hover:bg-neutral-50 text-xs shadow-xs cursor-pointer"
                      >
                        <XCircle className="size-3.5 mr-1" />
                        <span>Reject</span>
                      </Button>
                    </div>
                  )}
                </motion.div>
              )}

              {/* =========================================================================
                  SLIDE 5: Final Conversation between Staff and Customer (Sarah)
                  ========================================================================= */}
              {activeStep === 5 && (
                <motion.div
                  key="slide-5"
                  initial={{ opacity: 0, scale: 0.98 }}
                  animate={{ opacity: 1, scale: 1 }}
                  exit={{ opacity: 0, scale: 0.98 }}
                  transition={{ duration: 0.35 }}
                  className="flex flex-col gap-2.5 py-1"
                >
                  {/* WhatsApp chat header */}
                  <div className="flex items-center gap-2 rounded-xl bg-white px-3 py-2 border border-neutral-200/80 shadow-xs">
                    <img
                      src="https://images.pexels.com/photos/1239291/pexels-photo-1239291.jpeg?auto=compress&cs=tinysrgb&w=150"
                      alt="Sarah Alwis"
                      className="size-7 rounded-full object-cover border border-emerald-300"
                    />
                    <div className="min-w-0 flex-1">
                      <p className="text-xs font-semibold text-neutral-900 leading-tight">Sarah Alwis</p>
                      <p className="text-[9px] text-emerald-600 font-medium">Online · WhatsApp</p>
                    </div>
                    <Badge variant="outline" className="text-[9px] border-emerald-300 text-emerald-800 bg-emerald-50">
                      Draft Sent
                    </Badge>
                  </div>

                  {/* Staff Outgoing Message (drafted by Aveline) */}
                  <div className="ml-4 rounded-2xl rounded-tr-xs bg-commerce p-3 text-[11px] leading-relaxed text-white shadow-sm">
                    <p className="text-[10px] text-rose-100 font-semibold mb-1 flex items-center gap-1">
                      <Blossom className="size-3" /> Sent via Aveline Draft
                    </p>
                    <p>
                      Hi Sarah! Wonderful to hear from you — so excited for your sister’s wedding in Galle! Elle and Lina sourced this bespoke Peach Raw-Silk Drape Gown just for you. Reserved 1 unit in UK 10.
                    </p>
                    <div className="mt-2 rounded-lg bg-black/20 p-2 flex items-center justify-between text-[10px]">
                      <span>LKR 42,000 · 1 Unit</span>
                      <span className="underline font-semibold text-rose-100">Pay 30% Deposit</span>
                    </div>
                    <span className="mt-1 block text-right text-[9px] text-rose-200/80">10:18 AM · Read</span>
                  </div>

                  {/* Customer (Sarah) Enthusiastic Reply */}
                  <div className="mr-4 rounded-2xl rounded-tl-xs bg-white border border-emerald-200/80 p-3 text-[11px] leading-relaxed text-emerald-950 shadow-xs">
                    <p>
                      Oh Shanali, this is absolutely stunning!! Exactly what I was picturing for the Galle ceremony! Please reserve it for me right away ✨ Sending deposit now!
                    </p>
                    <span className="mt-1 block text-right text-[9px] text-neutral-400">10:19 AM</span>
                  </div>

                  {/* Closing Status Card */}
                  <div className="rounded-xl border border-dashed border-emerald-300 bg-emerald-50/80 p-2.5 text-center text-xs">
                    <p className="font-semibold text-emerald-800 flex items-center justify-center gap-1">
                      <PackageCheck className="size-3.5" /> Deposit Received · Deal Closed
                    </p>
                    <p className="text-[10px] text-neutral-600 mt-0.5">
                      40% margin secured · Total time: 7 minutes
                    </p>
                  </div>

                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => handleStepClick(0)}
                    className="mt-1 border-neutral-200 bg-white text-neutral-700 hover:bg-neutral-50 text-xs shadow-xs cursor-pointer"
                  >
                    <span>Replay Lifecycle</span>
                  </Button>
                </motion.div>
              )}
            </AnimatePresence>
          </div>

          {/* Bottom Indicators & Home Bar */}
          <div className="relative z-20 pb-3 pt-1 px-4 flex flex-col items-center gap-2 bg-gradient-to-t from-[#faf6f5] via-[#faf6f5]/90 to-transparent">
            {/* 6 Step Navigation Dots */}
            <div className="flex items-center gap-1.5">
              {[0, 1, 2, 3, 4, 5].map((i) => (
                <button
                  key={i}
                  type="button"
                  onClick={() => handleStepClick(i)}
                  aria-label={`Jump to step ${i + 1}`}
                  className={cn(
                    'h-1.5 rounded-full transition-all duration-300 cursor-pointer',
                    activeStep === i
                      ? 'w-6 bg-commerce shadow-xs'
                      : 'w-1.5 bg-neutral-300 hover:bg-neutral-400'
                  )}
                />
              ))}
            </div>

            {/* iOS Home Bar */}
            <div className="h-1 w-32 rounded-full bg-neutral-400/80" />
          </div>
        </div>
      </div>
    </div>
  )
}
