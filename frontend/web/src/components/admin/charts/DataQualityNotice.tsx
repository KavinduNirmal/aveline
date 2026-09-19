import { Card, CardContent } from "@/components/ui/card"
import type { DataQualityReport, DataQualityVocabulary } from "@/lib/admin/data-quality"
import { ShieldCheck, TriangleAlert } from "lucide-react"

const VOCABULARY_LABEL: Record<DataQualityVocabulary, string> = {
  system: "Not measured on this host",
  agent: "Agent instrumentation",
  api: "API data quality",
}

/**
 * The honesty notice. It **names its vocabulary** and never borrows another family's wording:
 * the system family says "not measured on this host", the agent family names its five flags, and
 * the API family names its three. Coercing one into another is how a gap becomes a zero.
 */
export function DataQualityNotice({
  report,
  title,
  tone = "info",
}: {
  report: DataQualityReport
  title?: string
  tone?: "info" | "critical"
}) {
  if (report.messages.length === 0) return null

  const Icon = tone === "critical" ? TriangleAlert : ShieldCheck

  return (
    <Card
      className={
        tone === "critical"
          ? "border-destructive/40 bg-destructive/5 shadow-xs"
          : "border-border/60 bg-muted/20 shadow-xs"
      }
      data-vocabulary={report.vocabulary}
    >
      <CardContent className="p-3 text-xs text-muted-foreground flex items-start gap-2">
        <Icon
          className={
            tone === "critical" ? "size-4 text-destructive shrink-0" : "size-4 text-primary shrink-0"
          }
        />
        <div>
          <div className="font-medium text-foreground">
            {title ?? VOCABULARY_LABEL[report.vocabulary]}
          </div>
          <ul className="list-disc pl-4 mt-1 space-y-0.5">
            {report.messages.map((message) => (
              <li key={message}>{message}</li>
            ))}
          </ul>
        </div>
      </CardContent>
    </Card>
  )
}
