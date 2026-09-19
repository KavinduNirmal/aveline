import { useNavigate, useParams } from "react-router-dom"
import { Button } from "@/components/ui/button"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import { History, ArrowLeft } from "lucide-react"

interface AdminHeaderProps {
  title?: string
  onOpenAudit: () => void
}

export function AdminHeader({ title, onOpenAudit }: AdminHeaderProps) {
  const { userId } = useParams<{ userId: string }>()
  const { email } = useAdminSession()
  const navigate = useNavigate()

  return (
    <header className="h-16 border-b border-border bg-background/80 backdrop-blur-md px-6 flex items-center justify-between sticky top-0 z-10">
      <div className="flex items-center gap-3">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/app")}
          className="text-xs text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="size-3.5 mr-1" />
          Back to Boutique
        </Button>
        <span className="text-border">|</span>
        <h1 className="font-serif font-medium text-base text-foreground tracking-tight">
          {title || "Administrator Console"}
        </h1>
      </div>

      <div className="flex items-center gap-3">
        <Button
          variant="outline"
          size="sm"
          onClick={onOpenAudit}
          className="h-8 text-xs gap-1.5 border-border/80"
        >
          <History className="size-3.5 text-primary" />
          Audit Log
        </Button>

        <div className="h-4 w-px bg-border" />

        <div className="text-right hidden sm:block">
          <div className="text-xs font-medium text-foreground">{email}</div>
          <div className="text-[10px] text-muted-foreground font-mono truncate max-w-[120px]">
            {userId}
          </div>
        </div>
      </div>
    </header>
  )
}
