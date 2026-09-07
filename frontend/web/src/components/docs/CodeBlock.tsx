import { useState } from 'react'
import { Check, Copy, Terminal } from 'lucide-react'
import { Button } from '@/components/ui/button'

interface CodeBlockProps {
  language?: string
  code: string
  children: React.ReactNode
}

export function CodeBlock({ language, code, children }: CodeBlockProps) {
  const [copied, setCopied] = useState(false)

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(code)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    } catch {
      // ignore clipboard error
    }
  }

  return (
    <div className="not-typeset my-6 overflow-hidden rounded-2xl border border-[#34272a] bg-[#161213] shadow-md">
      <div className="flex items-center justify-between border-b border-[#2e2225] bg-[#1e1719] px-4 py-2 text-xs font-mono text-[#9f8c8f]">
        <div className="flex items-center gap-2">
          <Terminal className="size-3.5 text-commerce" />
          <span className="font-semibold uppercase tracking-wider text-[11px] text-[#e5d4d6]">
            {language || 'code'}
          </span>
        </div>
        <Button
          variant="ghost"
          size="icon-xs"
          onClick={handleCopy}
          aria-label="Copy code to clipboard"
          className="text-[#9f8c8f] hover:text-[#ede5e6] hover:bg-[#2b2023]"
        >
          {copied ? (
            <Check className="size-3.5 text-emerald-400" />
          ) : (
            <Copy className="size-3.5" />
          )}
        </Button>
      </div>
      <pre className="overflow-x-auto p-4 font-mono text-xs leading-relaxed text-[#ede5e6]">
        {children}
      </pre>
    </div>
  )
}
