import { useState } from "react"
import { Outlet, useParams } from "react-router-dom"

import { AuditTrailPanel } from "@/components/admin/AuditTrailPanel"
import { AdminErrorBoundary } from "@/components/admin/guard/AdminErrorBoundary"
import { SidebarInset, SidebarProvider } from "@/components/ui/sidebar"

import { AdminHeader } from "./AdminHeader"
import { AdminSidePanel } from "./AdminSidePanel"

/**
 * The console's frame.
 *
 * One geometry rule, applied **exactly once**: `admin-container` wraps the header and the page
 * content together, so both share the same centred `max-w-[1600px]` column by construction. The
 * delivered shell let the header run full width while the content was capped, which is the
 * measured 192 px misalignment.
 */
export function AdminShell() {
  const { userId } = useParams<{ userId: string }>()
  const [auditOpen, setAuditOpen] = useState(false)

  return (
    <SidebarProvider>
      <AdminSidePanel />
      <SidebarInset>
        <a
          href="#admin-main"
          className="sr-only focus:not-sr-only focus:absolute focus:z-50 focus:top-2 focus:left-2 focus:rounded-md focus:bg-primary focus:px-3 focus:py-2 focus:text-primary-foreground focus:text-xs"
        >
          Skip to main content
        </a>

        <div className="admin-container flex min-h-svh flex-col">
          <AdminHeader onOpenAudit={() => setAuditOpen(true)} />

          <main id="admin-main" className="flex-1 py-8 px-4 sm:px-6">
            <AdminErrorBoundary>
              <Outlet />
            </AdminErrorBoundary>
          </main>
        </div>
      </SidebarInset>

      <AuditTrailPanel
        open={auditOpen}
        onClose={() => setAuditOpen(false)}
        actorUserId={userId}
        title="Admin Activity Log"
      />
    </SidebarProvider>
  )
}
