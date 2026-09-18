import { useEffect, useRef, useState } from 'react'

interface TypewriterTextProps {
  text: string
  /** Words revealed per tick. */
  wordsPerTick?: number
  /** Delay between ticks in ms. */
  tickMs?: number
  /** Called whenever the visible text grows, so the thread can keep the tail in view. */
  onProgress?: () => void
}

/**
 * Reveals text word by word (a typewriter effect) like a streaming LLM reply.
 */
export function TypewriterText({
  text,
  wordsPerTick = 1,
  tickMs = 40,
  onProgress,
}: TypewriterTextProps) {
  const words = text.split(' ')
  const [count, setCount] = useState(0)
  const done = count >= words.length
  const onProgressRef = useRef(onProgress)
  onProgressRef.current = onProgress

  useEffect(() => {
    if (done) return
    const timer = setTimeout(() => {
      setCount((c) => Math.min(c + wordsPerTick, words.length))
      onProgressRef.current?.()
    }, tickMs)
    return () => clearTimeout(timer)
  }, [count, done, tickMs, words.length, wordsPerTick])

  const shown = words.slice(0, count).join(' ')

  return <p className="whitespace-pre-wrap text-sm leading-relaxed">{shown}</p>
}
