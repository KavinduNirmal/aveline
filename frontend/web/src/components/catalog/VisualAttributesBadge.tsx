import { Sparkles } from 'lucide-react'
import { Badge } from '@/components/ui/badge'

interface VisualAttributesBadgeProps {
  color?: string
  colorHex?: string
  fabric?: string
  style?: string
  pattern?: string
  confidenceScore?: number
  compact?: boolean
}

export function VisualAttributesBadge({
  color,
  colorHex,
  fabric,
  style,
  pattern,
  confidenceScore,
  compact = false,
}: VisualAttributesBadgeProps) {
  const confidencePct = confidenceScore ? Math.round(confidenceScore * 100) : null

  return (
    <div className="flex flex-wrap items-center gap-1.5">
      {/* Color chip */}
      {color && (
        <Badge
          variant="outline"
          className="flex items-center gap-1.5 border-border/80 bg-background/80 px-2 py-0.5 text-[11px] font-normal text-foreground shadow-2xs"
        >
          {colorHex && (
            <span
              className="inline-block size-2 rounded-full border border-black/10 ring-1 ring-white/20"
              style={{ backgroundColor: colorHex }}
              aria-hidden
            />
          )}
          <span>{color}</span>
        </Badge>
      )}

      {/* Fabric badge */}
      {fabric && (
        <Badge
          variant="outline"
          className="border-border/80 bg-muted/40 px-2 py-0.5 text-[11px] font-normal text-muted-foreground"
        >
          {fabric}
        </Badge>
      )}

      {/* Pattern or Style */}
      {(pattern || style) && !compact && (
        <Badge
          variant="outline"
          className="border-border/60 bg-muted/20 px-2 py-0.5 text-[11px] font-normal text-muted-foreground"
        >
          {pattern ?? style}
        </Badge>
      )}

      {/* Vision AI Confidence Badge */}
      {confidencePct !== null && (
        <Badge
          variant="secondary"
          className="flex items-center gap-1 border-primary/20 bg-primary/5 px-1.5 py-0.5 text-[10px] font-medium text-primary"
          title={`Vision AI analyzed visual attributes with ${confidencePct}% confidence`}
        >
          <Sparkles className="size-2.5 text-primary" />
          <span>{confidencePct}% AI</span>
        </Badge>
      )}
    </div>
  )
}
