import { useLocation, useNavigate, useParams } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { findRouteBySubPath, findRouteById } from "@/lib/admin/routes"
import { Button } from "@/components/ui/button"
import { SidebarTrigger } from "@/components/ui/sidebar"
import {
  Breadcrumb,
  BreadcrumbItem,
  BreadcrumbLink,
  BreadcrumbList,
  BreadcrumbPage,
  BreadcrumbSeparator,
} from "@/components/ui/breadcrumb"
import { History } from "lucide-react"

/** Derives the breadcrumb from the registry rather than from a hand-written title map. */
function useCurrentRouteLabel(): string {
  const { pathname } = useLocation()
  const segment = pathname.split("/").filter(Boolean).slice(2).join("/")
  if (segment.length === 0) return "Dashboard"
  return (
    findRouteBySubPath(segment)?.label ??
    findRouteById(segment)?.label ??
    "Administrator Console"
  )
}

interface AdminHeaderProps {
  onOpenAudit: () => void
}

export function AdminHeader({ onOpenAudit }: AdminHeaderProps) {
  const { userId } = useParams<{ userId: string }>()
  const { email } = useAdminSession()
  const navigate = useNavigate()
  const label = useCurrentRouteLabel()

  return (
    <header className="h-16 border-b border-border bg-background/80 backdrop-blur-md px-4 sm:px-6 flex items-center justify-between sticky top-0 z-10">
      <div className="flex items-center gap-3 min-w-0">
        <SidebarTrigger className="lg:hidden" />
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/app")}
          className="text-xs text-muted-foreground hover:text-foreground hidden sm:inline-flex"
        >
          Back to Boutique
        </Button>
        <Breadcrumb className="min-w-0">
          <BreadcrumbList>
            <BreadcrumbItem className="hidden md:block">
              <BreadcrumbLink href={`/admin/${userId}/dashboard`}>Console</BreadcrumbLink>
            </BreadcrumbItem>
            <BreadcrumbSeparator className="hidden md:block" />
            <BreadcrumbItem>
              <BreadcrumbPage className="truncate">{label}</BreadcrumbPage>
            </BreadcrumbItem>
          </BreadcrumbList>
        </Breadcrumb>
      </div>

      <div className="flex items-center gap-3 min-w-0">
        <Button
          variant="outline"
          size="sm"
          onClick={onOpenAudit}
          className="h-8 text-xs gap-1.5 border-border/80"
        >
          <History className="size-3.5 text-primary" />
          <span className="hidden sm:inline">Audit Log</span>
        </Button>

        <div className="h-4 w-px bg-border" />

        <div className="text-right hidden sm:block min-w-0">
          <div className="text-xs font-medium text-foreground truncate max-w-[160px]">
            {email ?? "unknown account"}
          </div>
          <div className="text-[10px] text-muted-foreground font-mono truncate max-w-[160px]">
            {userId}
          </div>
        </div>
      </div>
    </header>
  )
}
