import { useEffect, useState } from 'react'
import { cn } from '@/lib/utils'

export interface TocItem {
  id: string
  text: string
  level: number
}

interface DocsTocProps {
  toc: TocItem[]
}

export function DocsToc({ toc }: DocsTocProps) {
  const [activeId, setActiveId] = useState<string>('')

  useEffect(() => {
    if (toc.length === 0) return

    const handleScroll = () => {
      const headings = toc
        .map((item) => document.getElementById(item.id))
        .filter((el): el is HTMLElement => el !== null)

      const scrollPosition = window.scrollY + 120

      for (let i = headings.length - 1; i >= 0; i--) {
        const heading = headings[i]
        if (heading.offsetTop <= scrollPosition) {
          setActiveId(heading.id)
          return
        }
      }

      if (headings.length > 0 && activeId === '') {
        setActiveId(headings[0].id)
      }
    }

    window.addEventListener('scroll', handleScroll, { passive: true })
    handleScroll()

    return () => {
      window.removeEventListener('scroll', handleScroll)
    }
  }, [toc, activeId])

  if (toc.length === 0) {
    return null
  }

  return (
    <nav aria-label="Table of contents" className="w-full">
      <div className="flex flex-col gap-2">
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
          On This Page
        </p>
        <div className="flex flex-col gap-1 border-l border-border/80 pl-3">
          {toc.map((item) => {
            const isActive = activeId === item.id
            return (
              <a
                key={item.id}
                href={`#${item.id}`}
                onClick={(e) => {
                  e.preventDefault()
                  const target = document.getElementById(item.id)
                  if (target) {
                    const top = target.getBoundingClientRect().top + window.scrollY - 80
                    window.scrollTo({ top, behavior: 'smooth' })
                    history.pushState(null, '', `#${item.id}`)
                    setActiveId(item.id)
                  }
                }}
                className={cn(
                  'text-xs leading-relaxed transition-colors block py-0.5',
                  item.level === 3 ? 'pl-3' : '',
                  isActive
                    ? 'font-medium text-commerce -ml-[13px] border-l-2 border-commerce pl-[11px]'
                    : 'text-neutral-500 hover:text-neutral-900 dark:text-neutral-400 dark:hover:text-neutral-100'
                )}
              >
                {item.text}
              </a>
            )
          })}
        </div>
      </div>
    </nav>
  )
}
