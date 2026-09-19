import { NavLink, useParams } from "react-router-dom"
import { ADMIN_ROUTES, type AdminRouteGroup } from "@/lib/admin-routes"
import { useAdminSession } from "@/contexts/AdminSessionContext"
import { cn } from "@/lib/utils"

export function AdminSidePanel() {
  const { userId } = useParams<{ userId: string }>()
  const { can, roles, email } = useAdminSession()

  const groups: AdminRouteGroup[] = [
    "Overview",
    "People",
    "Organizations",
    "Operations",
    "Observability",
    "Configuration",
  ]

  return (
    <aside className="w-64 border-r border-border bg-sidebar flex flex-col shrink-0 h-screen sticky top-0 select-none">
      {/* Brand Header */}
      <div className="h-16 px-6 border-b border-border flex items-center justify-between">
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-primary text-primary-foreground flex items-center justify-center font-serif font-bold text-lg shadow-sm">
            A
          </div>
          <div>
            <div className="font-serif font-medium text-sm tracking-wide text-foreground">
              Aveline Console
            </div>
            <div className="text-[10px] uppercase font-semibold tracking-wider text-muted-foreground">
              Administration
            </div>
          </div>
        </div>
      </div>

      {/* Scope Badge */}
      <div className="px-4 py-3 border-b border-border/60 bg-muted/20">
        <div className="text-[11px] font-medium text-muted-foreground truncate">
          Active Operator
        </div>
        <div className="text-xs font-semibold text-foreground truncate">
          {email || "admin@aveline.app"}
        </div>
        <div className="flex gap-1 mt-1 flex-wrap">
          {roles.slice(0, 2).map((r) => (
            <span
              key={r}
              className="text-[10px] px-1.5 py-0.5 rounded bg-primary/10 text-primary font-mono"
            >
              {r}
            </span>
          ))}
        </div>
      </div>

      {/* Navigation Groups */}
      <nav aria-label="Administration Navigation" className="flex-1 overflow-y-auto px-3 py-4 space-y-6">
        {groups.map((group) => {
          const routesInGroup = ADMIN_ROUTES.filter(
            (r) => r.group === group && (!r.permission || can(r.permission)),
          )

          if (routesInGroup.length === 0) return null

          return (
            <div key={group} className="space-y-1">
              <div className="px-3 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground/80">
                {group}
              </div>
              {routesInGroup.map((route) => {
                const targetPath = `/admin/${userId}/${route.subPath}`
                const Icon = route.icon

                return (
                  <NavLink
                    key={route.id}
                    to={targetPath}
                    className={({ isActive }) =>
                      cn(
                        "flex items-center gap-2.5 px-3 py-2 rounded-lg text-xs font-medium transition-colors",
                        "hover:bg-sidebar-accent hover:text-sidebar-accent-foreground",
                        "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
                        isActive
                          ? "bg-primary text-primary-foreground font-semibold shadow-xs"
                          : "text-sidebar-foreground",
                      )
                    }
                  >
                    <Icon className="size-4 shrink-0" />
                    <span>{route.label}</span>
                  </NavLink>
                )
              })}
            </div>
          )
        })}
      </nav>

      {/* Footer / System Status */}
      <div className="p-3 border-t border-border/70 text-[11px] text-muted-foreground flex items-center justify-between">
        <span className="flex items-center gap-1.5">
          <span className="size-2 rounded-full bg-emerald-500 animate-pulse" />
          Live Connection
        </span>
        <span className="font-mono text-[10px]">v1.0.0</span>
      </div>
    </aside>
  )
}
