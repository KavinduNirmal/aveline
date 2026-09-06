import { useEffect, useId, useState } from 'react'
import mermaid from 'mermaid'
import { GitBranch, AlertCircle } from 'lucide-react'

interface MermaidProps {
  chart: string
}

export function Mermaid({ chart }: MermaidProps) {
  const rawId = useId()
  const id = 'mermaid-' + rawId.replace(/[^a-zA-Z0-9_-]/g, '')
  const [svg, setSvg] = useState<string>('')
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let isMounted = true

    mermaid.initialize({
      startOnLoad: false,
      securityLevel: 'loose',
      theme: 'base',
      themeVariables: {
        primaryColor: '#ffffff',
        primaryTextColor: '#1e1b1b',
        primaryBorderColor: '#8b2e42',
        lineColor: '#534244',
        secondaryColor: '#fff8f7',
        tertiaryColor: '#ffffff',
        edgeLabelBackground: '#fff8f7',
        clusterBkg: '#f5eceb',
        clusterBorder: '#d9c1c3',
        fontFamily: 'DM Sans, -apple-system, BlinkMacSystemFont, sans-serif',
        fontSize: '14px',
      },
    })

    const renderChart = async () => {
      try {
        setError(null)
        const { svg: renderedSvg } = await mermaid.render(id, chart)
        if (isMounted) {
          setSvg(renderedSvg)
        }
      } catch (err: unknown) {
        if (isMounted) {
          console.error('Mermaid render error:', err)
          setError(err instanceof Error ? err.message : 'Failed to render Mermaid diagram')
        }
      }
    }

    void renderChart()

    return () => {
      isMounted = false
    }
  }, [chart, id])

  if (error) {
    return (
      <div className="my-6 rounded-2xl border border-destructive/30 bg-destructive/5 p-4">
        <div className="flex items-center gap-2 text-xs font-semibold text-destructive">
          <AlertCircle className="size-4" />
          <span>Diagram Rendering Error</span>
        </div>
        <pre className="mt-2 overflow-x-auto text-xs font-mono text-muted-foreground">
          {chart}
        </pre>
      </div>
    )
  }

  return (
    <div className="my-8 overflow-hidden rounded-2xl border border-dashed border-border bg-card/80 shadow-sm">
      <div className="flex items-center justify-between border-b border-dashed border-border/70 bg-muted/30 px-4 py-2 text-xs text-muted-foreground">
        <div className="flex items-center gap-1.5 font-medium">
          <GitBranch className="size-3.5 text-commerce" />
          <span>Architecture & Flow Diagram</span>
        </div>
        <span className="text-[11px] font-mono uppercase tracking-wider text-muted-foreground/70">Mermaid</span>
      </div>
      <div
        className="flex justify-center items-center overflow-x-auto p-6 md:p-8 [&_svg]:max-w-full [&_svg]:h-auto min-h-[160px]"
        dangerouslySetInnerHTML={{ __html: svg }}
      />
    </div>
  )
}
