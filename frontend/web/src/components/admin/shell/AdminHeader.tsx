import { useLocation, useParams } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { useUserContext } from "@/contexts/UserContext"
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
  const { user } = useUserContext()
  const label = useCurrentRouteLabel()

  /**
   * The operator's identity, from the best available source.
   *
   * `/auth/claims` reported `email: null` for every real bearer token (the A9 defect), so the
   * header fell back to a placeholder even though the console already holds the signed-in user's
   * own record from `GET /users/me`. The claims email is preferred when it is present, and the
   * account record is the fallback, so the header reads correctly on an API build either side of
   * the fix.
   */
  const nonEmpty = (value: string | null | undefined): string | null =>
    typeof value === "string" && value.trim().length > 0 ? value : null

  const displayEmail = nonEmpty(email) ?? nonEmpty(user?.email)
  const accountLabel =
    displayEmail ?? (user?.username ? `@${user.username}` : "signed-in account")

  return (
    <header className="h-16 border-b border-border bg-background/80 backdrop-blur-md px-4 sm:px-6 flex items-center justify-between sticky top-0 z-10">
      <div className="flex items-center gap-3 min-w-0">
        <SidebarTrigger className="lg:hidden" />
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
            {accountLabel}
          </div>
          <div className="text-[10px] text-muted-foreground font-mono truncate max-w-[160px]">
            {userId}
          </div>
        </div>
      </div>
    </header>
  )
}
