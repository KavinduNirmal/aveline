import { NavLink, useParams } from "react-router-dom"

import { useAdminSession } from "@/contexts/AdminSessionContext"
import { ADMIN_DOMAINS, routesForDomain, type AdminDomain } from "@/lib/admin/routes"
import { canOpenGate } from "@/lib/admin/role-policies"
import {
  Sidebar,
  SidebarContent,
  SidebarFooter,
  SidebarGroup,
  SidebarGroupContent,
  SidebarGroupLabel,
  SidebarHeader,
  SidebarMenu,
  SidebarMenuButton,
  SidebarMenuItem,
} from "@/components/ui/sidebar"

import { AdminScopeChip } from "./AdminScopeChip"

const DOMAIN_LABEL: Record<AdminDomain, string> = {
  overview: "Overview",
  people: "People",
  organizations: "Organizations",
  operations: "Operations",
  observability: "Observability",
  statistics: "Statistics",
  business: "Business",
}

/**
 * The navigation panel, driven entirely by `lib/admin/routes.ts`.
 *
 * Entries the caller cannot open are **not rendered** — `canOpenGate` is the `.filter`, so a
 * denied domain never appears rather than appearing disabled. The footer's delivered "Live
 * Connection" green dot is gone: it reflected nothing. The real connection state belongs to the
 * log stream (A7), not to a decoration.
 */
export function AdminSidePanel() {
  const { userId } = useParams<{ userId: string }>()
  const { roles, can } = useAdminSession()

  return (
    <Sidebar collapsible="offcanvas" aria-label="Administration navigation">
      <SidebarHeader className="h-16 justify-center px-4">
        <div className="flex items-center gap-2.5">
          <div className="w-8 h-8 rounded-lg bg-primary text-primary-foreground flex items-center justify-center font-serif font-bold text-lg shadow-sm">
            A
          </div>
          <div>
            <div className="font-serif font-medium text-sm tracking-wide text-sidebar-foreground">
              Aveline Console
            </div>
            <div className="text-[10px] uppercase font-semibold tracking-wider text-muted-foreground">
              Administration
            </div>
          </div>
        </div>
      </SidebarHeader>

      <AdminScopeChip />

      <SidebarContent>
        {ADMIN_DOMAINS.map((domain) => {
          const visible = routesForDomain(domain).filter(
            (route) =>
              route.enabled &&
              route.navigable !== false &&
              (route.gate === null || canOpenGate(route.gate, { roles, can })),
          )
          if (visible.length === 0) return null

          return (
            <SidebarGroup key={domain}>
              <SidebarGroupLabel>{DOMAIN_LABEL[domain]}</SidebarGroupLabel>
              <SidebarGroupContent>
                <SidebarMenu>
                  {visible.map((route) => {
                    const Icon = route.icon
                    return (
                      <SidebarMenuItem key={route.id}>
                        <SidebarMenuButton asChild>
                          <NavLink to={`/admin/${userId}/${route.subPath}`}>
                            <Icon className="size-4 shrink-0" />
                            <span>{route.label}</span>
                          </NavLink>
                        </SidebarMenuButton>
                      </SidebarMenuItem>
                    )
                  })}
                </SidebarMenu>
              </SidebarGroupContent>
            </SidebarGroup>
          )
        })}
      </SidebarContent>

      <SidebarFooter className="text-[11px] text-muted-foreground">
        <span className="font-mono text-[10px]">Aveline Admin</span>
      </SidebarFooter>
    </Sidebar>
  )
}
