import * as React from 'react'
import { Toaster as Sonner, type ToasterProps } from 'sonner'

import { cn } from '@/lib/utils'

/**
 * Lightweight theme-mode hook. Aveline has no next-themes provider, so we mirror the
 * presence of the `.dark` class on <html> and stay in sync with a MutationObserver.
 */
function useThemeMode(): 'light' | 'dark' {
  const [theme, setTheme] = React.useState<'light' | 'dark'>(() =>
    typeof document !== 'undefined' && document.documentElement.classList.contains('dark')
      ? 'dark'
      : 'light',
  )

  React.useEffect(() => {
    const node = document.documentElement
    const apply = () => setTheme(node.classList.contains('dark') ? 'dark' : 'light')
    apply()
    const observer = new MutationObserver(apply)
    observer.observe(node, { attributes: true, attributeFilter: ['class'] })
    return () => observer.disconnect()
  }, [])

  return theme
}

/**
 * Sonner-based toast host themed to the Aveline "Quiet Luxury" palette. Toasts render as
 * fully rounded "pill" cards using the popover/foreground/border tokens in both light and
 * dark mode. Mount once, typically at the app root.
 */
function Toaster({ className, ...props }: ToasterProps) {
  const theme = useThemeMode()

  return (
    <Sonner
      theme={theme}
      className={cn('toaster group', className)}
      position="top-right"
      offset={16}
      gap={8}
      closeButton
      toastOptions={{
        classNames: {
          toast: cn(
            'group toast !rounded-full !border-border bg-popover !px-4 text-popover-foreground shadow-lg',
            '[&:has([data-icon])]:!pl-3.5',
          ),
          description: '!text-popover-foreground/80',
          actionButton: '!rounded-full bg-primary text-primary-foreground',
          cancelButton: '!rounded-full bg-muted text-muted-foreground',
          error: '!bg-popover !text-popover-foreground [&_[data-icon]]:!text-destructive',
          success:
            '!bg-popover !text-popover-foreground [&_[data-icon]]:!text-primary',
          closeButton: '!bg-transparent !text-popover-foreground/70 hover:!text-popover-foreground',
        },
      }}
      style={
        {
          '--normal-bg': 'var(--popover)',
          '--normal-text': 'var(--popover-foreground)',
          '--normal-border': 'var(--border)',
          '--success-bg': 'var(--popover)',
          '--success-text': 'var(--popover-foreground)',
          '--success-border': 'var(--border)',
          '--error-bg': 'var(--popover)',
          '--error-text': 'var(--popover-foreground)',
          '--error-border': 'var(--border)',
          '--warning-bg': 'var(--popover)',
          '--warning-text': 'var(--popover-foreground)',
          '--warning-border': 'var(--border)',
          '--info-bg': 'var(--popover)',
          '--info-text': 'var(--popover-foreground)',
          '--info-border': 'var(--border)',
        } as React.CSSProperties
      }
      {...props}
    />
  )
}

export { Toaster }
